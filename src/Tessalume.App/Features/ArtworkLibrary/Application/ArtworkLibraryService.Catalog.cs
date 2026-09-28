using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.ArtworkLibrary.Infrastructure;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.App.Features.ArtworkLibrary.Application;

internal sealed partial class ArtworkLibraryService
{
    private readonly Dictionary<string, (long Length, long Ticks, int Width, int Height)> _dimensions =
        new(StringComparer.OrdinalIgnoreCase);

    public static ArtworkLibraryCharacter GetCharacter(ThemePackage package)
    {
        var manifest = package.Manifest;
        var name = manifest.Name.Split(['·', '｜', '|'], StringSplitOptions.TrimEntries)[0];
        var id = manifest.Id.Contains('.') ? manifest.Id.Split('.')[0] : manifest.Id;
        if (manifest.Config.TryGetValue("characterId", out var characterId) && characterId.ValueKind == JsonValueKind.String)
            id = characterId.GetString();
        if (manifest.Config.TryGetValue("characterName", out var characterName) && characterName.ValueKind == JsonValueKind.String)
            name = characterName.GetString();
        return new ArtworkLibraryCharacter(CleanText(id, "theme-" + Hash(manifest.Id)), CleanText(name, manifest.Name), 0);
    }

    private async Task<IReadOnlyList<ArtworkLibraryItem>> IndexPackageAsync(ThemePackage package,
        List<string> diagnostics, CancellationToken cancellationToken)
    {
        var defaults = await _defaultsStore.LoadAsync(package, cancellationToken);
        if (!defaults.IsExact && defaults.Diagnostic is not null)
            diagnostics.Add($"{package.Manifest.Name}：{defaults.Diagnostic}");
        var settings = ThemeArtworkSettingsResolver.Resolve(defaults.Defaults, null).Settings;
        var character = GetCharacter(package);
        var items = new Dictionary<string, ArtworkLibraryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var region in Enum.GetValues<ArtworkRegion>())
            foreach (var mode in Enum.GetValues<ArtworkColorMode>())
            {
                if ((mode == ArtworkColorMode.Light && !package.Manifest.Capabilities.Light) ||
                    (mode == ArtworkColorMode.Dark && !package.Manifest.Capabilities.Dark)) continue;
                if (!ArtworkImageSourceResolver.IsRegionSupported(package, region, mode)) continue;
                var adjustment = ArtworkSettingsAccessor.GetAdjustment(settings, mode, region);
                var key = adjustment.ThemeAssetKey ?? ArtworkImageSourceResolver.GetAssetKey(package, region, mode);
                if (!package.AssetPaths.TryGetValue(key, out var asset)) continue;
                var path = ResolveThemePath(package.RootDirectory, asset);
                if (path is null) continue;
                var recommendation = new ArtworkLibraryRecommendation(region, mode, adjustment with
                {
                    CompositionMode = ThemeArtworkCompositionMode.Custom,
                    CustomImagePath = null,
                    ThemeAssetKey = null,
                });
                if (items.TryGetValue(path, out var existing))
                {
                    items[path] = existing with
                    {
                        Regions = existing.Regions.Append(region).Distinct().ToArray(),
                        Modes = existing.Modes.Append(mode).Distinct().ToArray(),
                        Recommendations = existing.Recommendations.Append(recommendation).ToArray(),
                    };
                    continue;
                }
                var fingerprint = await ResolveSourceFingerprintAsync($"{package.Manifest.Id}:{key}", path, cancellationToken);
                var id = $"theme:{package.Manifest.Id}:{key}:{fingerprint[..24]}";
                var size = ReadDimensions(path);
                _document.PreparedImages.TryGetValue(id, out var prepared);
                items[path] = new ArtworkLibraryItem
                {
                    Id = id,
                    Name = $"{character.Name} · {RegionName(region)} · {(mode == ArtworkColorMode.Dark ? "暗色" : "亮色")}",
                    CharacterId = character.Id,
                    CharacterName = character.Name,
                    AbsolutePath = path,
                    StoredPath = ResolvePersonal(prepared) is null ? null : prepared,
                    SourceThemeId = package.Manifest.Id,
                    SourceThemeName = package.Manifest.Name,
                    IsBuiltIn = true,
                    PixelWidth = size.Width,
                    PixelHeight = size.Height,
                    Regions = [region],
                    Modes = [mode],
                    Tags = ["主题原图", RegionName(region), mode == ArtworkColorMode.Dark ? "暗色" : "亮色"],
                    Recommendations = [recommendation],
                };
            }
        return items.Values.ToArray();
    }

    private ArtworkLibraryItem CreatePersonalItem(ArtworkLibraryStoredImage image, string path)
    {
        var size = ReadDimensions(path);
        return new ArtworkLibraryItem
        {
            Id = image.Id,
            Name = image.Name,
            CharacterId = image.CharacterId,
            CharacterName = image.CharacterName,
            AbsolutePath = path,
            StoredPath = image.StoredPath,
            AddedAt = image.AddedAt,
            Regions = image.Regions.Count > 0 ? image.Regions : Enum.GetValues<ArtworkRegion>(),
            Modes = image.Modes.Count > 0 ? image.Modes : Enum.GetValues<ArtworkColorMode>(),
            Tags = ["个人图片"],
            PixelWidth = size.Width,
            PixelHeight = size.Height,
        };
    }

    private List<ArtworkLibraryUsage> FindUsages(ArtworkLibraryItem item,
        IReadOnlyList<ThemePackage> packages, IReadOnlyDictionary<string, ThemeVisualSettings> themeSettings)
    {
        var usages = new List<ArtworkLibraryUsage>();
        foreach (var package in packages)
            foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                foreach (var region in Enum.GetValues<ArtworkRegion>())
                {
                    if (!ArtworkImageSourceResolver.IsRegionSupported(package, region, mode)) continue;
                    themeSettings.TryGetValue(package.Manifest.Id, out var settings);
                    var adjustment = ArtworkSettingsAccessor.GetAdjustment(settings ?? new(), mode, region);
                    var personalPath = ResolvePersonal(adjustment.CustomImagePath);
                    var usesPersonal = personalPath is not null && item.StoredPath is not null &&
                        string.Equals(personalPath, ResolvePersonal(item.StoredPath), StringComparison.OrdinalIgnoreCase);
                    var assetKey = adjustment.ThemeAssetKey ?? ArtworkImageSourceResolver.GetAssetKey(package, region, mode);
                    var usesOriginal = personalPath is null && item.IsBuiltIn && package.Manifest.Id == item.SourceThemeId &&
                        package.AssetPaths.TryGetValue(assetKey, out var asset) &&
                        string.Equals(ResolveThemePath(package.RootDirectory, asset), item.AbsolutePath, StringComparison.OrdinalIgnoreCase);
                    if (usesPersonal || usesOriginal) usages.Add(new(package.Manifest.Id, package.Manifest.Name, region, mode));
                }
        return usages;
    }

    private (string Id, string Name, List<ArtworkRegion> Regions, List<ArtworkColorMode> Modes) FindExistingOwner(
        string stored, IReadOnlyList<ThemePackage> packages, IReadOnlyDictionary<string, ThemeVisualSettings> themeSettings)
    {
        foreach (var package in packages)
        {
            if (!themeSettings.TryGetValue(package.Manifest.Id, out var settings)) continue;
            var matches = (from region in Enum.GetValues<ArtworkRegion>()
                           from mode in Enum.GetValues<ArtworkColorMode>()
                           let path = ResolvePersonal(ArtworkSettingsAccessor.GetAdjustment(settings, mode, region).CustomImagePath)
                           where path is not null && string.Equals(ToStoredPath(path), stored, StringComparison.OrdinalIgnoreCase)
                           select new ArtworkTarget(mode, region)).ToArray();
            if (matches.Length == 0) continue;
            var character = GetCharacter(package);
            return (character.Id, character.Name, matches.Select(target => target.Region).Distinct().ToList(),
                matches.Select(target => target.Mode).Distinct().ToList());
        }
        return ("personal", "我的图片", [], []);
    }

    private (int Width, int Height) ReadDimensions(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (_dimensions.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Ticks == info.LastWriteTimeUtc.Ticks)
                return (cached.Width, cached.Height);
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var size = (Width: decoder.Frames[0].PixelWidth, Height: decoder.Frames[0].PixelHeight);
            _dimensions[path] = (info.Length, info.LastWriteTimeUtc.Ticks, size.Width, size.Height);
            return size;
        }
        catch (Exception exception) when (exception is IOException or FormatException or NotSupportedException or InvalidOperationException or
            System.Runtime.InteropServices.COMException or ArgumentException)
        { return (0, 0); }
    }

}
