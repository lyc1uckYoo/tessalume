using System.Text.Json;

namespace Tessalume.Core.Runtime;

internal sealed class ThemeRuntimeAcceptanceProbe(LoopbackCdpDiscovery discovery)
{
    public async Task<ThemeRuntimeAcceptanceSnapshot> InspectAsync(
        int port,
        CancellationToken cancellationToken)
    {
        var targets = await discovery.DiscoverAsync(port, cancellationToken);
        if (targets.Count == 0)
        {
            throw new InvalidOperationException($"本机端口 {port} 尚未发现 Codex 页面。");
        }

        var target = await FindMainTargetAsync(targets, cancellationToken);
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
        var value = await session.EvaluateAsync(InspectionExpression, cancellationToken);
        return JsonSerializer.Deserialize<ThemeRuntimeAcceptanceSnapshot>(value.GetRawText(), SerializerOptions)
            ?? throw new InvalidOperationException("Codex 返回了空的验收快照。");
    }

    public async Task<IReadOnlyList<ThemeRuntimeAcceptanceSnapshot>> InspectResponsiveAsync(
        int port,
        IReadOnlyList<int> viewportWidths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewportWidths);
        if (viewportWidths.Count == 0 || viewportWidths.Any(width => width is < 640 or > 2560))
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidths), "响应式验收视口必须位于 640 到 2560 像素之间。");
        }
        var targets = await discovery.DiscoverAsync(port, cancellationToken);
        if (targets.Count == 0) throw new InvalidOperationException($"本机端口 {port} 尚未发现 Codex 页面。");
        var target = await FindMainTargetAsync(targets, cancellationToken);
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
        var snapshots = new List<ThemeRuntimeAcceptanceSnapshot>(viewportWidths.Count);
        try
        {
            foreach (var width in viewportWidths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await session.SendCommandAsync(
                    "Emulation.setDeviceMetricsOverride",
                    new
                    {
                        width,
                        height = 900,
                        deviceScaleFactor = 1,
                        mobile = false,
                        screenWidth = width,
                        screenHeight = 900,
                    },
                    cancellationToken);
                await session.EvaluateAsync(
                    "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(resolve, 60))))",
                    cancellationToken);
                var value = await session.EvaluateAsync(InspectionExpression, cancellationToken);
                snapshots.Add(JsonSerializer.Deserialize<ThemeRuntimeAcceptanceSnapshot>(
                    value.GetRawText(),
                    SerializerOptions) ?? throw new InvalidOperationException("Codex 返回了空的响应式验收快照。"));
            }
            return snapshots;
        }
        finally
        {
            try
            {
                await session.SendCommandAsync(
                    "Emulation.clearDeviceMetricsOverride",
                    parameters: null,
                    CancellationToken.None);
                _ = await session.EvaluateAsync(
                    "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))",
                    CancellationToken.None);
            }
            catch (Exception)
            {
                // Closing Codex during acceptance makes restoration impossible; the
                // emulation override disappears with that renderer session.
            }
        }
    }

    private static async Task<CdpTarget> FindMainTargetAsync(
        IReadOnlyList<CdpTarget> targets,
        CancellationToken cancellationToken)
    {
        foreach (var target in targets.OrderBy(candidate =>
                     candidate.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
            var isMain = await session.EvaluateAsync(
                "!!document.querySelector('main') && " +
                "!document.documentElement.classList.contains('compact-window') && " +
                "!new URLSearchParams(location.search).has('initialRoute')",
                cancellationToken);
            if (isMain.ValueKind == JsonValueKind.True) return target;
        }

        return targets.FirstOrDefault(candidate =>
            !candidate.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase)) ?? targets[0];
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private const string InspectionExpression = """
        (() => {
          const html = document.documentElement;
          const runtime = window.__TESSALUME_RUNTIME__;
          const root = document.querySelector('#tessalume-theme-root');
          const main = document.querySelector('main.main-surface, main');
          const mainBox = main?.getBoundingClientRect();
          const composer = document.querySelector('[data-tessalume-surface="composer"]') ||
            document.querySelector('.composer-surface-chrome') ||
            document.querySelector('[data-codex-composer="true"]')?.closest('[class*="ComposerLayoutRoot"], [class*="ComposerLayoutBody"]');
          const messages = Array.from(document.querySelectorAll(
            '[data-content-search-unit-key], [data-user-message-bubble="true"]'));
          const messageUnits = Array.from(new Set(messages.map(node =>
            node.closest('[data-content-search-unit-key]') || node)));
          const pageKind = html.classList.contains('tessalume-is-home')
            ? 'home'
            : html.classList.contains('tessalume-is-settings')
              ? 'settings'
              : 'task';
          const layout = root?.getAttribute('data-tessalume-task-layout');
          return {
            themeId: runtime?.themeId || window.__TESSALUME_THEME_ID__ || null,
            isDarkMode: html.classList.contains('electron-dark'),
            pageKind,
            runtimeReady: Boolean(runtime?.context &&
              typeof runtime.context.mountCanonicalTheme === 'function'),
            themeMounted: Boolean(root && runtime?.themeId),
            mainSurfaceReady: Boolean(mainBox && mainBox.width > 0 && mainBox.height > 0),
            composerPresent: Boolean(composer),
            composerDecorated: composer?.getAttribute('data-tessalume-surface') === 'composer',
            messageCount: messageUnits.length,
            decoratedMessageCount: messageUnits.filter(node =>
              node.hasAttribute('data-tessalume-message')).length,
            responsiveLayoutReady: Boolean(root && (
              pageKind !== 'task' || ['full', 'reduced', 'minimal'].includes(layout))),
            responsiveLayout: layout || '',
            viewportWidth: Math.round(window.innerWidth),
            viewportHeight: Math.round(window.innerHeight)
          };
        })()
        """;
}
