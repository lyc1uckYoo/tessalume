using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization;

internal sealed class PersonalImageStore
{
    public const long MaximumImageBytes = 32L * 1024 * 1024;

    internal const long MaximumImagePixels = 64_000_000;

    internal const int MaximumImageDimension = 32_768;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
            ".gif",
            ".bmp",
        };

    private readonly string _dataDirectory;
    private readonly string _imagesDirectory;

    public PersonalImageStore(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _imagesDirectory = Path.Combine(_dataDirectory, "personalization", "images");
    }

    public async Task<string> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var source = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidDataException("请选择 PNG、JPG、WebP、GIF 或 BMP 图片。");
        }

        var info = new FileInfo(source);
        if (!info.Exists)
        {
            throw new FileNotFoundException("找不到选择的图片。", source);
        }
        if (info.Length is <= 0 or > MaximumImageBytes)
        {
            throw new InvalidDataException("个人图片必须大于 0 B 且不超过 32 MiB。");
        }
        await EnsureSupportedImageSignatureAsync(source, extension, cancellationToken);
        await ValidateDecodableImageAsync(source, cancellationToken);

        var hash = await ComputeHashAsync(source, cancellationToken);
        var fileName = $"{hash[..24].ToLowerInvariant()}{extension}";
        var relativePath = Path.Combine("personalization", "images", fileName)
            .Replace('\\', '/');
        var destination = Path.Combine(_imagesDirectory, fileName);
        Directory.CreateDirectory(_imagesDirectory);
        if (File.Exists(destination)) return relativePath;

        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = new FileStream(
                             source,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporary, destination);
            return relativePath;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public string? ResolvePath(string? storedPath)
    {
        var value = (storedPath ?? string.Empty).Trim();
        if (value.Length == 0) return null;
        var candidate = Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(
                _dataDirectory,
                value.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(_imagesDirectory, candidate);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative) ||
            !SupportedExtensions.Contains(Path.GetExtension(candidate)) ||
            !File.Exists(candidate))
        {
            return null;
        }
        return candidate;
    }

    public ThemeVisualSettings ResolveForRuntime(ThemeVisualSettings settings)
    {
        var normalized = settings.Normalize();
        return normalized with
        {
            Light = ResolveMode(normalized.Light),
            Dark = ResolveMode(normalized.Dark),
        };
    }

    private ThemeVisualModeSettings ResolveMode(ThemeVisualModeSettings mode) => mode with
    {
        Hero = ResolveAdjustment(mode.Hero),
        Sidebar = ResolveAdjustment(mode.Sidebar),
        Chat = ResolveAdjustment(mode.Chat),
    };

    private ThemeArtworkAdjustment ResolveAdjustment(ThemeArtworkAdjustment adjustment) =>
        adjustment with { CustomImagePath = ResolvePath(adjustment.CustomImagePath) };

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task EnsureSupportedImageSignatureAsync(
        string path,
        string extension,
        CancellationToken cancellationToken)
    {
        var signature = new byte[12];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            signature.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = await stream.ReadAsync(signature, cancellationToken);
        var valid = extension switch
        {
            ".png" => length >= 8 && signature.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => length >= 3 &&
                signature[0] == 0xFF && signature[1] == 0xD8 && signature[2] == 0xFF,
            ".webp" => length >= 12 &&
                signature.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                signature.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            ".gif" => length >= 6 &&
                (signature.AsSpan(0, 6).SequenceEqual("GIF87a"u8) ||
                 signature.AsSpan(0, 6).SequenceEqual("GIF89a"u8)),
            ".bmp" => length >= 2 && signature[0] == (byte)'B' && signature[1] == (byte)'M',
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidDataException("图片内容与文件扩展名不匹配，或文件已经损坏。");
        }
    }

    private static async Task ValidateDecodableImageAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(
                () => ValidateDecodableImage(path, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            FileFormatException or
            InvalidOperationException or
            NotSupportedException or
            COMException or
            OutOfMemoryException)
        {
            throw new InvalidDataException(
                "图片无法安全解码，请换用有效且尺寸合理的 PNG、JPG、WebP、GIF 或 BMP 图片。",
                exception);
        }
    }

    private static void ValidateDecodableImage(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.SequentialScan);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("图片不包含可解码的画面。");
        }

        var frame = decoder.Frames[0];
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;
        if (width <= 0 || height <= 0 ||
            width > MaximumImageDimension || height > MaximumImageDimension ||
            (long)width * height > MaximumImagePixels)
        {
            throw new InvalidDataException(
                $"图片像素尺寸过大；单边不得超过 {MaximumImageDimension:N0} px，" +
                $"总像素不得超过 {MaximumImagePixels:N0}。"
            );
        }

        var bytesPerPixel = Math.Max(1, (frame.Format.BitsPerPixel + 7) / 8);
        var probe = new byte[bytesPerPixel];
        frame.CopyPixels(new Int32Rect(0, 0, 1, 1), probe, bytesPerPixel, 0);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
