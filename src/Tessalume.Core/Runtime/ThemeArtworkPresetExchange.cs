using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tessalume.Core.Runtime;

public static class ThemeArtworkPresetExchange
{
    public const string FormatId = "tessalume-artwork-preset";
    public const int CurrentSchemaVersion = 1;
    public const string FileExtension = ".tessalume-look.json";
    public const long MaximumFileBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task ExportAsync(
        string destinationPath,
        ThemeArtworkPreset preset,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(preset);
        var normalized = ValidatePreset(preset);
        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("图像方案保存路径无效。");
        Directory.CreateDirectory(directory);

        var temporaryPath = destination + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new ArtworkPresetDocument
                    {
                        Format = FormatId,
                        SchemaVersion = CurrentSchemaVersion,
                        Preset = normalized,
                    },
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destination, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    public static async Task<ThemeArtworkPreset> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var source = Path.GetFullPath(sourcePath);
        await using var stream = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumFileBytes)
        {
            throw new InvalidDataException("图像方案文件为空或超过 64 KB 安全限制。");
        }

        ArtworkPresetDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<ArtworkPresetDocument>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("图像方案文件格式无效或包含不支持的字段。", exception);
        }

        if (document is null ||
            !string.Equals(document.Format, FormatId, StringComparison.Ordinal) ||
            document.SchemaVersion != CurrentSchemaVersion ||
            document.Preset is null)
        {
            throw new InvalidDataException("这不是当前版本支持的 Tessalume 图像方案文件。");
        }

        return ValidatePreset(document.Preset);
    }

    private static ThemeArtworkPreset ValidatePreset(ThemeArtworkPreset preset)
    {
        var name = (preset.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 32)
        {
            throw new InvalidDataException("图像方案名称必须为 1 到 32 个字符。");
        }
        if (preset.Settings is null)
        {
            throw new InvalidDataException("图像方案缺少三个区域的参数。");
        }

        ValidateAdjustment(preset.Settings.Hero, "首页横幅");
        ValidateAdjustment(preset.Settings.Sidebar, "左栏图片");
        ValidateAdjustment(preset.Settings.Chat, "聊天背景");
        return preset with
        {
            Name = name,
            Settings = preset.Settings.Normalize(),
        };
    }

    private static void ValidateAdjustment(ThemeArtworkAdjustment? adjustment, string region)
    {
        if (adjustment is null || !AllFinite(adjustment) || adjustment != adjustment.Normalize())
        {
            throw new InvalidDataException($"{region}包含超出支持范围的图像参数。");
        }
    }

    private static bool AllFinite(ThemeArtworkAdjustment adjustment) =>
        double.IsFinite(adjustment.Brightness) &&
        double.IsFinite(adjustment.Contrast) &&
        double.IsFinite(adjustment.Saturation) &&
        double.IsFinite(adjustment.Opacity) &&
        double.IsFinite(adjustment.Zoom) &&
        double.IsFinite(adjustment.OffsetX) &&
        double.IsFinite(adjustment.OffsetY) &&
        double.IsFinite(adjustment.Grayscale) &&
        double.IsFinite(adjustment.HueRotation) &&
        double.IsFinite(adjustment.Blur);

    private sealed record ArtworkPresetDocument
    {
        public string Format { get; init; } = string.Empty;

        public int SchemaVersion { get; init; }

        public ThemeArtworkPreset? Preset { get; init; }
    }
}
