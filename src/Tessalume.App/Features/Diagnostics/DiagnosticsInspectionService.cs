using Tessalume.App.Infrastructure;
using Tessalume.Core.Compatibility;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Diagnostics;

internal sealed class DiagnosticsInspectionService(
    PortableLayout layout,
    StudioStateStore stateStore,
    CompatibilityPackStore compatibilityPacks,
    CodexPackageLauncher launcher)
{
    public async Task<DiagnosticsSnapshot> InspectAsync(
        int? activePort,
        IReadOnlyCollection<DiagnosticsThemeStatus> themes,
        CancellationToken cancellationToken = default)
    {
        var state = await stateStore.LoadAsync(cancellationToken);
        var compatibility = await CompatibilityHealthService.InspectAsync(
            state,
            compatibilityPacks.Resolve(),
            cancellationToken);
        var port = activePort ?? state?.Port;
        var portReady = port is not null &&
            await launcher.IsDebugPortReadyAsync(port.Value, cancellationToken);
        var activeTheme = state?.Enabled == true
            ? themes.FirstOrDefault(theme =>
                string.Equals(theme.ThemeId, state.ThemeId, StringComparison.OrdinalIgnoreCase))?.Name
            : null;

        return new DiagnosticsSnapshot(
            layout.RootDirectory,
            layout.ThemesDirectory,
            CodexPackageLauncher.IsCodexRunning(),
            port,
            portReady,
            themes.Count,
            themes.Count(theme => theme.IsValid),
            state?.Enabled == true,
            activeTheme,
            compatibility,
            DateTimeOffset.Now);
    }
}
