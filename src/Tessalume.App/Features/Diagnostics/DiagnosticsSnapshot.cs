namespace Tessalume.App.Features.Diagnostics;

internal sealed record DiagnosticsThemeStatus(
    string? ThemeId,
    string Name,
    bool IsValid);

internal sealed record DiagnosticsSnapshot(
    string ApplicationRoot,
    string ThemesDirectory,
    bool CodexRunning,
    int? Port,
    bool PortReady,
    int TotalThemes,
    int ValidThemes,
    bool ThemeEnabled,
    string? ActiveThemeName,
    CompatibilityHealthSnapshot Compatibility,
    DateTimeOffset CheckedAt)
{
    public int InvalidThemes => Math.Max(0, TotalThemes - ValidThemes);
}
