namespace Tessalume.Core.Runtime;

public sealed record ThemeRuntimeAcceptanceSnapshot(
    string? ThemeId,
    bool IsDarkMode,
    string PageKind,
    bool RuntimeReady,
    bool ThemeMounted,
    bool MainSurfaceReady,
    bool ComposerPresent,
    bool ComposerDecorated,
    int MessageCount,
    int DecoratedMessageCount,
    bool ResponsiveLayoutReady,
    string ResponsiveLayout,
    int ViewportWidth,
    int ViewportHeight);
