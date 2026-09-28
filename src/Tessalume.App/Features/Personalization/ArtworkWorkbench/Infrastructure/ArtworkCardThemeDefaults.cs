using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

/// <summary>
/// Adapts existing native card assets without rewriting the six authored artwork
/// defaults. Theme composition leaves native runtime card CSS and motion intact;
/// these neutral recommendations are used when editing a replacement image.
/// </summary>
internal static class ArtworkCardThemeDefaults
{
    public static ThemeArtworkDefaultsDocument Adapt(ThemeArtworkDefaultsDocument document, ThemePackage package) =>
        document with
        {
            Slots = document.Slots with
            {
                TaskLeft = Modes(package, ArtworkRegion.TaskLeft),
                Memory = Modes(package, ArtworkRegion.Memory),
                TaskRightSecondary = Modes(package, ArtworkRegion.TaskRightSecondary),
                TaskRightPrimary = Modes(package, ArtworkRegion.TaskRightPrimary),
            },
        };

    private static ThemeArtworkDefaultSlotModes Modes(ThemePackage package, ArtworkRegion region) => new()
    {
        Light = Slot(package, region, ArtworkColorMode.Light),
        Dark = Slot(package, region, ArtworkColorMode.Dark),
    };

    private static ThemeArtworkDefaultSlot Slot(ThemePackage package, ArtworkRegion region, ArtworkColorMode mode) => new()
    {
        Asset = ArtworkImageSourceResolver.GetAssetKey(package, region, mode),
        Placement = new ThemeArtworkCssPlacement
        {
            Size = new ThemeArtworkCssSize { Width = "cover", Height = "auto" },
            Position = new ThemeArtworkCssPosition { X = "center", Y = "center" },
        },
    };
}
