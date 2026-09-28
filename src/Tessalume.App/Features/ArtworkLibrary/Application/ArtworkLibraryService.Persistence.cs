using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.ArtworkLibrary.Infrastructure;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

namespace Tessalume.App.Features.ArtworkLibrary.Application;

internal sealed partial class ArtworkLibraryService
{
    private const int MaximumCatalogBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<ArtworkLibraryDocument> ReadDocumentAsync(List<string> diagnostics, CancellationToken cancellationToken)
    {
        _readOnlyReason = null;
        if (!File.Exists(_catalogPath)) return new();
        try
        {
            if (new FileInfo(_catalogPath).Length > MaximumCatalogBytes || IsReparsePoint(_catalogPath))
                throw new InvalidDataException("图库索引超出读取限制或位于链接路径。");
            await using var stream = File.OpenRead(_catalogPath);
            var document = await JsonSerializer.DeserializeAsync<ArtworkLibraryDocument>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("图库索引为空。");
            if (document.SchemaVersion != 1) throw new InvalidDataException("图库索引来自不支持的版本。");
            if (document.Images is null || document.HiddenImagePaths is null || document.Favorites is null || document.Compositions is null || document.PreparedImages is null ||
                document.PreparedMetadata is null || document.SourceFingerprints is null ||
                document.Images.Count > 10000 || document.HiddenImagePaths.Count > 10000 || document.Favorites.Count > 20000 || document.Compositions.Count > 50000 ||
                document.PreparedImages.Count > 10000 || document.PreparedMetadata.Count > 10000 || document.SourceFingerprints.Count > 10000)
                throw new InvalidDataException("图库索引结构或条目数量无效。");
            var images = document.Images.Where(image => image is not null &&
                    ValidText(image.Id) && ValidText(image.StoredPath) && IsPersonalRelativePath(image.StoredPath))
                .GroupBy(image => image.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .Select(image => image with
                {
                    Name = CleanText(image.Name, "个人图片"),
                    CharacterId = CleanText(image.CharacterId, "personal"),
                    CharacterName = CleanText(image.CharacterName, "我的图片"),
                    Regions = (image.Regions ?? []).Where(Enum.IsDefined).Distinct().ToList(),
                    Modes = (image.Modes ?? []).Where(Enum.IsDefined).Distinct().ToList(),
                }).ToList();
            if (images.Count != document.Images.Count) diagnostics.Add("图库中无效或重复的图片记录已跳过。");
            return document with
            {
                Images = images,
                HiddenImagePaths = document.HiddenImagePaths.Where(path => ValidText(path) && IsPersonalRelativePath(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Favorites = document.Favorites.Where(ValidText).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                PreparedImages = document.PreparedImages.Where(pair => ValidText(pair.Key) && IsPersonalRelativePath(pair.Value))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                PreparedMetadata = document.PreparedMetadata.Where(pair => ValidText(pair.Key) && pair.Value is not null &&
                        IsPersonalRelativePath(pair.Value.StoredPath))
                    .ToDictionary(pair => pair.Key, pair => pair.Value with
                    {
                        Id = pair.Key,
                        Name = CleanText(pair.Value.Name, "已保留的主题图片"),
                        CharacterId = CleanText(pair.Value.CharacterId, "personal"),
                        CharacterName = CleanText(pair.Value.CharacterName, "我的图片"),
                        Regions = (pair.Value.Regions ?? []).Where(Enum.IsDefined).Distinct().ToList(),
                        Modes = (pair.Value.Modes ?? []).Where(Enum.IsDefined).Distinct().ToList(),
                    }, StringComparer.OrdinalIgnoreCase),
                SourceFingerprints = document.SourceFingerprints.Where(pair => ValidText(pair.Key) && pair.Value is not null &&
                        pair.Value.Length > 0 && pair.Value.LastWriteTicks > 0 &&
                        pair.Value.Sha256 is { Length: 64 } && pair.Value.Sha256.All(Uri.IsHexDigit))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                Compositions = document.Compositions.Where(value => value is not null && ValidText(value.ItemId) &&
                        ValidText(value.ThemeId) && Enum.IsDefined(value.Region) && Enum.IsDefined(value.Mode) && value.Adjustment is not null)
                    .Select(value => value with { Adjustment = value.Adjustment.Normalize() with { CustomImagePath = null, ThemeAssetKey = null } }).ToList(),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _readOnlyReason = $"图库索引暂时无法读取，原文件已保留。{exception.Message}";
            diagnostics.Add(_readOnlyReason);
            return new();
        }
    }

    private async Task WriteDocumentAsync(ArtworkLibraryDocument document, CancellationToken cancellationToken)
    {
        EnsureWritable();
        if (document.Images.Count > 10000 || document.HiddenImagePaths.Count > 10000 || document.Favorites.Count > 20000 ||
            document.Compositions.Count > 50000 || document.PreparedImages.Count > 10000 ||
            document.PreparedMetadata.Count > 10000 || document.SourceFingerprints.Count > 10000)
            throw new InvalidDataException("图库条目已达到容量限制。");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumCatalogBytes) throw new InvalidDataException("图库索引已达到容量限制。");
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
        var temporary = _catalogPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _catalogPath, overwrite: true);
            _document = document;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string? ResolvePersonal(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try
        {
            var path = _imageStore.ResolvePath(stored);
            return path is not null && !IsReparsePoint(path) && !IsReparsePoint(Path.GetDirectoryName(path)!) ? path : null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        { return null; }
    }

    private static string? ResolveThemePath(string root, string asset)
    {
        try
        {
            var path = Path.GetFullPath(Path.IsPathRooted(asset) ? asset : Path.Combine(root, asset));
            var relative = Path.GetRelativePath(Path.GetFullPath(root), path);
            return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                   Path.IsPathRooted(relative) || !File.Exists(path) || IsReparsePoint(path) ? null : path;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        { return null; }
    }

    private static bool IsPersonalRelativePath(string? path) => !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("personalization/images/", StringComparison.Ordinal) &&
        path.Split('/').Length == 3 && !path.Contains('\\') && !path.Contains(':') &&
        path.Split('/')[2] is not "." and not ".." && path.Split('/')[2].IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    private string ToStoredPath(string path) => Path.GetRelativePath(_dataDirectory, path).Replace('\\', '/');
    private static string PersonalId(string stored) => "personal:" + Hash(stored.ToLowerInvariant());
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
    private static string CleanText(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim()[..Math.Min(value.Trim().Length, 256)];
    private static bool ValidText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512;
    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static string RegionName(ArtworkRegion region) => region switch
    {
        ArtworkRegion.Hero => "首页",
        ArtworkRegion.Sidebar => "左栏",
        ArtworkRegion.Chat => "聊天背景",
        ArtworkRegion.TaskLeft => "左上角色卡",
        ArtworkRegion.Memory => "左下记忆卡",
        ArtworkRegion.TaskRightSecondary => "右侧次卡",
        ArtworkRegion.TaskRightPrimary => "右侧主卡",
        _ => throw new ArgumentOutOfRangeException(nameof(region)),
    };
    private static void EnsureTarget(ArtworkRegion region, ArtworkColorMode mode)
    {
        if (!Enum.IsDefined(region) || !Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(region));
    }
    private void EnsureWritable()
    {
        if (!_loaded) throw new InvalidOperationException("请先加载角色图库。");
        if (_readOnlyReason is not null) throw new InvalidDataException(_readOnlyReason);
        EnsureSafePersonalizationRoot();
    }
    private void EnsureSafePersonalizationRoot()
    {
        var personalization = Path.Combine(_dataDirectory, "personalization");
        var images = Path.Combine(personalization, "images");
        if ((Directory.Exists(personalization) && IsReparsePoint(personalization)) ||
            (Directory.Exists(images) && IsReparsePoint(images)))
            throw new InvalidDataException("图库存储目录不能是指向其他位置的链接。");
    }
    private ArtworkLibraryItem EnsureKnownItem(string id) => _items.FirstOrDefault(item => item.Id == id)
        ?? throw new InvalidDataException("这张图片已不在当前图库中，请刷新后重试。");
}
