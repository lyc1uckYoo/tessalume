using System.IO;
using System.Text.Json;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal sealed record ArtworkThemeDefaultsLoadResult(
    ThemeArtworkDefaultsDocument Defaults,
    bool IsExact,
    string? Diagnostic)
{
    public static ArtworkThemeDefaultsLoadResult StandardFallback(string themeId, string diagnostic) =>
        new(CreateFallback(themeId), false, diagnostic);

    private static ThemeArtworkDefaultsDocument CreateFallback(string themeId) => new()
    {
        ThemeId = themeId,
        DefaultsVersion = "standard-fallback",
        Slots = new ThemeArtworkDefaultSlots
        {
            Hero = CreateModes("hero"),
            Sidebar = CreateModes("sidebar"),
            Chat = CreateModes("chat"),
        },
    };

    private static ThemeArtworkDefaultSlotModes CreateModes(string region) => new()
    {
        Light = CreateSlot($"{region}-light"),
        Dark = CreateSlot($"{region}-dark"),
    };

    private static ThemeArtworkDefaultSlot CreateSlot(string asset) => new()
    {
        Asset = asset,
        Placement = new ThemeArtworkCssPlacement
        {
            Size = new ThemeArtworkCssSize { Width = "contain", Height = "auto" },
        },
    };
}

internal sealed class ArtworkThemeDefaultsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<ArtworkThemeDefaultsLoadResult> LoadAsync(
        ThemePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        var entry = package.Manifest.EntryPoints.ArtworkDefaults;
        if (string.IsNullOrWhiteSpace(entry))
        {
            return ArtworkThemeDefaultsLoadResult.StandardFallback(
                package.Manifest.Id,
                "主题没有声明 artworkDefaults；使用标准预览，需要在线校准。");
        }

        string path;
        try
        {
            path = ResolveContainedPath(package.RootDirectory, entry);
        }
        catch (InvalidDataException exception)
        {
            return ArtworkThemeDefaultsLoadResult.StandardFallback(
                package.Manifest.Id,
                exception.Message);
        }
        if (!File.Exists(path))
        {
            return ArtworkThemeDefaultsLoadResult.StandardFallback(
                package.Manifest.Id,
                "主题推荐构图文件不存在；使用标准预览，需要在线校准。");
        }

        var info = new FileInfo(path);
        var signature = new FileSignature(info.Length, info.LastWriteTimeUtc.Ticks);
        Task<ArtworkThemeDefaultsLoadResult> task;
        lock (_gate)
        {
            if (!_cache.TryGetValue(path, out var cached) || cached.Signature != signature)
            {
                task = ReadAsync(path, package);
                _cache[path] = new CacheEntry(signature, task);
                while (_cache.Count > 24) _cache.Remove(_cache.Keys.First());
            }
            else
            {
                task = cached.Task;
            }
        }
        return await task.WaitAsync(cancellationToken);
    }

    private static async Task<ArtworkThemeDefaultsLoadResult> ReadAsync(
        string path,
        ThemePackage package)
    {
        var themeId = package.Manifest.Id;
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<ThemeArtworkDefaultsDocument>(
                stream,
                JsonOptions);
            if (document is null ||
                document.SchemaVersion != 1 ||
                !string.Equals(document.ThemeId, themeId, StringComparison.OrdinalIgnoreCase) ||
                !Version.TryParse(document.DefaultsVersion, out _))
            {
                throw new InvalidDataException(
                    "主题推荐构图的 schema、themeId 或 defaultsVersion 无效。");
            }
            ThemeArtworkDefaultsValidator.Validate(document);
            var normalized = document.Normalize();
            ValidateAssets(normalized, package);
            return new ArtworkThemeDefaultsLoadResult(normalized, true, null);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return ArtworkThemeDefaultsLoadResult.StandardFallback(
                themeId,
                $"主题推荐构图无法精确读取：{exception.Message}");
        }
    }

    private static void ValidateAssets(
        ThemeArtworkDefaultsDocument document,
        ThemePackage package)
    {
        var slots = new[]
        {
            document.Slots.Hero.Light,
            document.Slots.Hero.Dark,
            document.Slots.Sidebar.Light,
            document.Slots.Sidebar.Dark,
            document.Slots.Chat.Light,
            document.Slots.Chat.Dark,
        };
        if (slots.Any(slot =>
                string.IsNullOrWhiteSpace(slot.Asset) ||
                !package.AssetPaths.TryGetValue(slot.Asset, out var path) ||
                !File.Exists(path)))
        {
            throw new InvalidDataException("主题推荐构图引用了缺失的原始 asset key。");
        }
    }

    private static string ResolveContainedPath(string rootDirectory, string entry)
    {
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, entry));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("artworkDefaults 必须位于主题目录内。");
        }
        return path;
    }

    private readonly record struct FileSignature(long Length, long LastWriteTicks);

    private sealed record CacheEntry(
        FileSignature Signature,
        Task<ArtworkThemeDefaultsLoadResult> Task);
}
