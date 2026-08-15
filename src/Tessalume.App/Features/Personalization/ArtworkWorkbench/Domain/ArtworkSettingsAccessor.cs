using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

internal static class ArtworkSettingsAccessor
{
    public static ThemeVisualModeSettings GetMode(
        ThemeVisualSettings settings,
        ArtworkColorMode mode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        return mode == ArtworkColorMode.Dark ? normalized.Dark : normalized.Light;
    }

    public static ThemeVisualSettings SetMode(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ThemeVisualModeSettings replacement)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(replacement);
        var normalized = settings.Normalize();
        var normalizedReplacement = replacement.Normalize();
        return (mode == ArtworkColorMode.Dark
            ? normalized with { Dark = normalizedReplacement }
            : normalized with { Light = normalizedReplacement }).Normalize();
    }

    public static ThemeArtworkAdjustment GetAdjustment(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region) =>
        GetAdjustment(GetMode(settings, mode), region);

    public static ThemeArtworkAdjustment GetAdjustment(
        ThemeVisualModeSettings mode,
        ArtworkRegion region)
    {
        ArgumentNullException.ThrowIfNull(mode);
        var normalized = mode.Normalize();
        return region switch
        {
            ArtworkRegion.Sidebar => normalized.Sidebar,
            ArtworkRegion.Chat => normalized.Chat,
            _ => normalized.Hero,
        };
    }

    public static ThemeVisualModeSettings SetAdjustment(
        ThemeVisualModeSettings mode,
        ArtworkRegion region,
        ThemeArtworkAdjustment replacement)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(replacement);
        var normalized = mode.Normalize();
        var normalizedReplacement = replacement.Normalize();
        return (region switch
        {
            ArtworkRegion.Sidebar => normalized with { Sidebar = normalizedReplacement },
            ArtworkRegion.Chat => normalized with { Chat = normalizedReplacement },
            _ => normalized with { Hero = normalizedReplacement },
        }).Normalize();
    }

    public static ThemeVisualSettings SetAdjustment(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ThemeArtworkAdjustment replacement)
    {
        var currentMode = GetMode(settings, mode);
        return SetMode(settings, mode, SetAdjustment(currentMode, region, replacement));
    }
}
