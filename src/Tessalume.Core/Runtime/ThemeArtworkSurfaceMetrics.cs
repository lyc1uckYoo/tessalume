using System.Text.Json;

namespace Tessalume.Core.Runtime;

public sealed record ThemeArtworkSurfaceRect(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record ThemeArtworkComputedStyle(
    string BackgroundSize,
    string BackgroundPosition,
    string BackgroundImage,
    string Transform,
    string Translate,
    string Scale,
    string Filter,
    string Opacity);

public sealed record ThemeArtworkSurfaceMetric(
    bool Available,
    string Region,
    string Pseudo,
    ThemeArtworkSurfaceRect? Rect,
    ThemeArtworkComputedStyle? Computed,
    string? UnavailableReason);

public sealed record ThemeArtworkSurfaceMetricsSnapshot(
    string ThemeId,
    bool DarkMode,
    string Route,
    double DevicePixelRatio,
    double ViewportWidth,
    double ViewportHeight,
    ThemeArtworkSurfaceMetric Hero,
    ThemeArtworkSurfaceMetric Sidebar,
    ThemeArtworkSurfaceMetric Chat)
{
    public int ArtworkCompositionProtocolVersion { get; init; }
}

public sealed partial class ThemeRuntime
{
    private static readonly JsonSerializerOptions SurfaceMetricsJsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads live Codex geometry and computed pseudo-element artwork styles without
    /// modifying the renderer. Missing route-specific targets are returned as explicit
    /// unavailable surfaces rather than substituted fixed dimensions.
    /// </summary>
    public async Task<ThemeArtworkSurfaceMetricsSnapshot?> InspectArtworkSurfaceMetricsAsync(
        int port,
        string themeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(port, 0);
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        var themeIdJson = JsonSerializer.Serialize(themeId);
        var targets = await _discovery.DiscoverAsync(port, cancellationToken);
        foreach (var target in targets)
        {
            await using var session = new CdpSession();
            try
            {
                await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
                var value = await session.EvaluateAsync($$"""
                    (() => {
                      const runtime = window.__TESSALUME_RUNTIME__;
                      if (runtime?.themeId !== {{themeIdJson}}) return null;
                      const inspect = (region, selector, pseudo) => {
                        const element = document.querySelector(selector);
                        if (!element) {
                          return {
                            available: false,
                            region,
                            pseudo,
                            rect: null,
                            computed: null,
                            unavailableReason: "target-not-present-on-current-route",
                          };
                        }
                        const rect = element.getBoundingClientRect();
                        const style = getComputedStyle(element, pseudo);
                        return {
                          available: rect.width > 0 && rect.height > 0,
                          region,
                          pseudo,
                          rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
                          computed: {
                            backgroundSize: style.backgroundSize,
                            backgroundPosition: style.backgroundPosition,
                            backgroundImage: style.backgroundImage,
                            transform: style.transform,
                            translate: style.translate,
                            scale: style.scale,
                            filter: style.filter,
                            opacity: style.opacity,
                          },
                          unavailableReason: rect.width > 0 && rect.height > 0
                            ? null
                            : "target-has-zero-size",
                        };
                      };
                      const html = document.documentElement;
                      return {
                        themeId: runtime.themeId,
                        artworkCompositionProtocolVersion:
                          Number(runtime.artworkCompositionProtocolVersion) || 0,
                        darkMode: html.classList.contains("electron-dark"),
                        route: html.classList.contains("tessalume-is-home")
                          ? "home"
                          : html.classList.contains("tessalume-is-task") ? "task" : "other",
                        devicePixelRatio: window.devicePixelRatio || 1,
                        viewportWidth: window.innerWidth,
                        viewportHeight: window.innerHeight,
                        hero: inspect(
                          "hero",
                          '[data-tessalume-surface="home"]>div:first-child>div:first-child>div:first-child',
                          "::before"),
                        sidebar: inspect(
                          "sidebar",
                          '[data-tessalume-surface="sidebar"]',
                          "::after"),
                        chat: inspect(
                          "chat",
                          'main[data-tessalume-surface="main"]',
                          "::before"),
                      };
                    })()
                    """, cancellationToken);
                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                var snapshot = JsonSerializer.Deserialize<ThemeArtworkSurfaceMetricsSnapshot>(
                    value.GetRawText(),
                    SurfaceMetricsJsonOptions);
                if (snapshot is not null) return snapshot;
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or UriFormatException or
                System.Net.WebSockets.WebSocketException)
            {
                // Another target can remain valid while a window is closing.
            }
        }
        return null;
    }
}
