using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;

internal enum ArtworkSurfaceMetricsProbeDisposition
{
    IgnoreStale,
    ClearCurrent,
    Apply,
}

internal static class ArtworkSurfaceMetricsProbeGate
{
    internal const int SupportedCompositionProtocolVersion = 1;

    // A theme handoff is atomic in the renderer, but the first workbench probe can
    // still land just before that commit. Keep the Artwork Studio refresh cadence
    // below one second so a connected standard preview is replaced by live geometry
    // without requiring another click or edit.
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(800);

    public static ArtworkSurfaceMetricsProbeDisposition Evaluate(
        ThemeArtworkSurfaceMetricsSnapshot? snapshot,
        int probeVersion,
        int currentVersion,
        string requestedThemeId,
        string? contextThemeId,
        bool editingDarkMode)
    {
        if (probeVersion != currentVersion ||
            !string.Equals(
                requestedThemeId,
                contextThemeId,
                StringComparison.OrdinalIgnoreCase))
        {
            return ArtworkSurfaceMetricsProbeDisposition.IgnoreStale;
        }
        if (snapshot is null ||
            !string.Equals(
                requestedThemeId,
                snapshot.ThemeId,
                StringComparison.OrdinalIgnoreCase) ||
            snapshot.DarkMode != editingDarkMode ||
            snapshot.ArtworkCompositionProtocolVersion !=
                SupportedCompositionProtocolVersion)
        {
            return ArtworkSurfaceMetricsProbeDisposition.ClearCurrent;
        }
        return ArtworkSurfaceMetricsProbeDisposition.Apply;
    }
}
