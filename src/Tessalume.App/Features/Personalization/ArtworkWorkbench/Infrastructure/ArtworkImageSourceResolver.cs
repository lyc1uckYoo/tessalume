using System.IO;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal enum ArtworkImageSourceKind
{
    ThemeOriginal,
    LocalReplacement,
}

internal sealed record ArtworkImageSource(
    string AbsolutePath,
    ArtworkImageSourceKind SourceKind,
    string DisplayName);

/// <summary>
/// Resolves the effective preview source without changing the persisted image
/// reference. Missing or invalid personal images fall back to the theme asset.
/// </summary>
internal static class ArtworkImageSourceResolver
{
    public static ArtworkImageSource? Resolve(
        ThemePackage themePackage,
        PersonalImageStore personalImageStore,
        ArtworkRegion region,
        ArtworkColorMode mode,
        ThemeArtworkAdjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(themePackage);
        ArgumentNullException.ThrowIfNull(personalImageStore);
        ArgumentNullException.ThrowIfNull(adjustment);

        var personalImage = TryResolvePersonalImage(personalImageStore, adjustment.CustomImagePath);
        if (personalImage is not null)
        {
            return new ArtworkImageSource(
                personalImage,
                ArtworkImageSourceKind.LocalReplacement,
                "本地图片");
        }

        var assetKey = adjustment.Normalize().ThemeAssetKey ?? GetAssetKey(region, mode);
        if (!TryGetAssetPath(themePackage.AssetPaths, assetKey, out var storedAssetPath))
        {
            return null;
        }

        var themeAsset = TryResolveThemeAsset(themePackage.RootDirectory, storedAssetPath);
        return themeAsset is null
            ? null
            : new ArtworkImageSource(
                themeAsset,
                ArtworkImageSourceKind.ThemeOriginal,
                "主题原图");
    }

    internal static string GetAssetKey(ArtworkRegion region, ArtworkColorMode mode)
    {
        var regionKey = region switch
        {
            ArtworkRegion.Hero => "hero",
            ArtworkRegion.Sidebar => "sidebar",
            ArtworkRegion.Chat => "chat",
            _ => throw new ArgumentOutOfRangeException(nameof(region), region, null),
        };
        var modeKey = mode switch
        {
            ArtworkColorMode.Light => "light",
            ArtworkColorMode.Dark => "dark",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
        return $"{regionKey}-{modeKey}";
    }

    private static string? TryResolvePersonalImage(
        PersonalImageStore personalImageStore,
        string? storedPath)
    {
        try
        {
            var path = personalImageStore.ResolvePath(storedPath);
            return path is null ? null : Path.GetFullPath(path);
        }
        catch (Exception exception) when (IsRecoverablePathFailure(exception))
        {
            return null;
        }
    }

    private static string? TryResolveThemeAsset(string rootDirectory, string storedPath)
    {
        try
        {
            var path = Path.IsPathRooted(storedPath)
                ? Path.GetFullPath(storedPath)
                : Path.GetFullPath(Path.Combine(rootDirectory, storedPath));
            return File.Exists(path) ? path : null;
        }
        catch (Exception exception) when (IsRecoverablePathFailure(exception))
        {
            return null;
        }
    }

    private static bool TryGetAssetPath(
        IReadOnlyDictionary<string, string> assetPaths,
        string assetKey,
        out string path)
    {
        if (assetPaths.TryGetValue(assetKey, out var exactPath) &&
            !string.IsNullOrWhiteSpace(exactPath))
        {
            path = exactPath;
            return true;
        }

        foreach (var (key, candidate) in assetPaths)
        {
            if (string.Equals(key, assetKey, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static bool IsRecoverablePathFailure(Exception exception) =>
        exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException;
}
