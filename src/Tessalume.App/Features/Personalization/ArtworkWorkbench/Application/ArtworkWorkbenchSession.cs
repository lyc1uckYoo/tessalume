using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;

internal sealed class ArtworkWorkbenchSession
{
    public ArtworkWorkbenchSession(
        ArtworkHistoryService? history = null)
    {
        History = history ?? new ArtworkHistoryService();
    }

    public ArtworkHistoryService History { get; }

    public ThemeVisualSettings Mutate(
        string themeId,
        ThemeVisualSettings current,
        Func<ThemeVisualSettings, ThemeVisualSettings> mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        var before = current.Normalize();
        var after = (mutation(before) ?? before).Normalize();
        _ = History.RecordDiscrete(themeId, before, after);
        return after;
    }

    public bool BeginGesture(string themeId, ThemeVisualSettings current) =>
        History.BeginGesture(themeId, current);

    public static ThemeVisualSettings UpdateGesture(
        ThemeVisualSettings current,
        Func<ThemeVisualSettings, ThemeVisualSettings> mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        var normalized = current.Normalize();
        return (mutation(normalized) ?? normalized).Normalize();
    }

    public bool EndGesture(string themeId, ThemeVisualSettings current) =>
        History.EndGesture(themeId, current);

    public ThemeVisualSettings Reset(
        string themeId,
        ThemeVisualSettings current,
        ArtworkResetRequest request) =>
        Mutate(themeId, current, settings => ArtworkSettingsReducer.Reset(settings, request));

    public ThemeVisualSettings PasteRegion(
        string themeId,
        ThemeVisualSettings current,
        ArtworkColorMode targetMode,
        ArtworkRegion targetRegion,
        ThemeArtworkAdjustment copiedParameters) =>
        Mutate(
            themeId,
            current,
            settings => ArtworkSettingsReducer.PasteRegion(
                settings,
                targetMode,
                targetRegion,
                copiedParameters));

    public ThemeVisualSettings CopyMode(
        string themeId,
        ThemeVisualSettings current,
        ArtworkColorMode sourceMode,
        ArtworkColorMode targetMode) =>
        Mutate(
            themeId,
            current,
            settings => ArtworkSettingsReducer.CopyMode(settings, sourceMode, targetMode));

    public ThemeVisualSettings ApplyPreset(
        string themeId,
        ThemeVisualSettings current,
        ArtworkColorMode targetMode,
        ThemeVisualModeSettings preset) =>
        Mutate(
            themeId,
            current,
            settings => ArtworkSettingsReducer.ApplyPreset(settings, targetMode, preset));

    public bool TryUndo(
        string themeId,
        ThemeVisualSettings current,
        out ThemeVisualSettings restored) =>
        History.TryUndo(themeId, current, out restored);

    public bool TryRedo(
        string themeId,
        ThemeVisualSettings current,
        out ThemeVisualSettings restored) =>
        History.TryRedo(themeId, current, out restored);
}
