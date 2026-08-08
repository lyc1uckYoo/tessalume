internal static partial class TestSuite
{
    static async Task<int> ProbeDisplayPreferencesAsync(
        int port,
        string packagePath,
        string dataDirectory)
    {
        var package = (await new ThemePackageLoader().LoadAsync(packagePath)).Package
            ?? throw new InvalidOperationException("The requested theme package could not be loaded.");
        ThemeVisualSettings originalSettings;
        using (var preferences = new UiPreferencesStore(dataDirectory))
        {
            var loaded = preferences.Load();
            originalSettings = loaded.ThemeVisualSettings.TryGetValue(package.Manifest.Id, out var settings)
                ? settings.Normalize()
                : new ThemeVisualSettings();
        }

        var repositoryRoot = FindRepositoryRoot();
        await DisposeLiveThemeRuntimeAsync(port);
        await using var runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>
            {
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
            }));

        try
        {
            var baselineSettings = originalSettings with
            {
                Display = new ThemeDisplayPreferences
                {
                    MotionIntensity = "full",
                    TextScale = "standard",
                    Density = "comfortable",
                },
            };
            await runtime.StartAsync(port, package, baselineSettings);
            await Task.Delay(350);
            var baseline = await ReadDisplayPreferenceSnapshotAsync(port);

            var reducedSettings = originalSettings with
            {
                Display = new ThemeDisplayPreferences
                {
                    MotionIntensity = "reduced",
                    TextScale = "large",
                    Density = "spacious",
                },
            };
            await runtime.ApplyVisualSettingsAsync(port, package.Manifest.Id, reducedSettings);
            await Task.Delay(350);
            var reduced = await ReadDisplayPreferenceSnapshotAsync(port);

            var minimalSettings = originalSettings with
            {
                Display = new ThemeDisplayPreferences
                {
                    MotionIntensity = "off",
                    TextScale = "small",
                    Density = "compact",
                },
            };
            await runtime.ApplyVisualSettingsAsync(port, package.Manifest.Id, minimalSettings);
            await Task.Delay(350);
            var minimal = await ReadDisplayPreferenceSnapshotAsync(port);

            var baselineFont = baseline.GetProperty("fontSize").GetDouble();
            var reducedFont = reduced.GetProperty("fontSize").GetDouble();
            var minimalFont = minimal.GetProperty("fontSize").GetDouble();
            var baselineRate = baseline.GetProperty("animationRate").GetDouble();
            var reducedRate = reduced.GetProperty("animationRate").GetDouble();
            var baselineRange = baseline.GetProperty("animationTransformRange").GetDouble();
            var reducedRange = reduced.GetProperty("animationTransformRange").GetDouble();
            var spaciousPadding = reduced.GetProperty("messagePadding").GetDouble();
            var compactPadding = minimal.GetProperty("messagePadding").GetDouble();
            var spaciousRowHeight = reduced.GetProperty("sidebarRowHeight").GetDouble();
            var compactRowHeight = minimal.GetProperty("sidebarRowHeight").GetDouble();
            var offAnimationCount = minimal.GetProperty("animationCount").GetInt32();

            if (!(baselineFont > 0) ||
                !(reducedFont >= baselineFont * 1.1) ||
                !(minimalFont <= baselineFont * .94) ||
                !(baselineRate > 0) ||
                Math.Abs(reducedRate / baselineRate - .55) > .08 ||
                !(baselineRange > 0) ||
                !(reducedRange > 0 && reducedRange < baselineRange * .7) ||
                !(spaciousPadding > compactPadding + 8) ||
                !(spaciousRowHeight > compactRowHeight + 8) ||
                offAnimationCount != 0)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    Baseline = baseline,
                    ReducedLargeSpacious = reduced,
                    OffSmallCompact = minimal,
                }));
                return 3;
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Baseline = baseline,
                ReducedLargeSpacious = reduced,
                OffSmallCompact = minimal,
            }));
            return 0;
        }
        finally
        {
            try
            {
                await runtime.ApplyVisualSettingsAsync(port, package.Manifest.Id, originalSettings);
            }
            catch
            {
                // The live Codex window may close during a manual QA probe.
            }
            await runtime.StopAsync();
        }
    }

    static async Task DisposeLiveThemeRuntimeAsync(int port)
    {
        using var discovery = new LoopbackCdpDiscovery();
        foreach (var target in await discovery.DiscoverAsync(port))
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            await session.EvaluateAsync(
                "(async () => { await window.__TESSALUME_RUNTIME__?.dispose?.(); return true; })()");
        }
    }

    static async Task<JsonElement> ReadDisplayPreferenceSnapshotAsync(int port)
    {
        using var discovery = new LoopbackCdpDiscovery();
        var targets = await discovery.DiscoverAsync(port);
        foreach (var target in targets.Where(target =>
                     !target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase)))
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            var result = await session.EvaluateAsync(
                """
                (() => {
                  const themeRoot = document.getElementById('tessalume-theme-root');
                  if (!window.__TESSALUME_RUNTIME__ || !themeRoot) return null;
                  const visible = (node) => {
                    if (!node) return false;
                    const box = node.getBoundingClientRect();
                    const style = getComputedStyle(node);
                    return box.width > 0 && box.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
                  };
                  const text = Array.from(document.querySelectorAll(
                    '[data-tessalume-surface="markdown"] :is(p,li),[data-user-message-bubble="true"]',
                  )).find(visible) || document.querySelector('[data-codex-composer="true"]');
                  const message = Array.from(document.querySelectorAll('[data-tessalume-message]')).find(visible);
                  const sidebarRow = Array.from(document.querySelectorAll(
                    '[data-tessalume-surface="sidebar"] :is([data-app-action-sidebar-project-row],[data-app-action-sidebar-thread-row])',
                  )).find(visible) || Array.from(document.querySelectorAll(
                    '[data-tessalume-surface="sidebar"] :is(button,a,[role="button"])',
                  )).find(visible);
                  const animations = themeRoot.getAnimations({ subtree:true });
                  const transformRange = (animation) => {
                    const frames = animation.effect?.getKeyframes?.()
                      .filter((frame) => frame.transform && frame.transform !== 'none') || [];
                    if (frames.length < 2) return 0;
                    try {
                      const origin = new DOMMatrixReadOnly(frames[0].transform);
                      return Math.max(...frames.slice(1).map((frame) => {
                        const target = new DOMMatrixReadOnly(frame.transform);
                        return [
                          'm11','m12','m13','m14','m21','m22','m23','m24',
                          'm31','m32','m33','m34','m41','m42','m43','m44',
                        ].reduce((sum, name) => sum + Math.abs(target[name] - origin[name]), 0);
                      }));
                    } catch { return 0; }
                  };
                  if (!text || !sidebarRow) return null;
                  const textStyle = getComputedStyle(text);
                  const messageStyle = message ? getComputedStyle(message) : null;
                  const sidebarStyle = getComputedStyle(sidebarRow);
                  return {
                    motion: document.documentElement.dataset.tessalumeMotion,
                    textScale: document.documentElement.dataset.tessalumeTextScale,
                    density: document.documentElement.dataset.tessalumeDensity,
                    fontSize: Number.parseFloat(textStyle.fontSize),
                    messagePadding: messageStyle
                      ? Number.parseFloat(messageStyle.paddingTop) + Number.parseFloat(messageStyle.paddingBottom)
                      : 0,
                    messageMargin: messageStyle
                      ? Number.parseFloat(messageStyle.marginTop) + Number.parseFloat(messageStyle.marginBottom)
                      : 0,
                    sidebarRowHeight: sidebarRow.getBoundingClientRect().height,
                    sidebarMinHeight: Number.parseFloat(sidebarStyle.minHeight),
                    markdownCount: document.querySelectorAll('[data-tessalume-surface="markdown"]').length,
                    semanticMessageCount: document.querySelectorAll('[data-tessalume-message]').length,
                    projectRowCount: document.querySelectorAll('[data-app-action-sidebar-project-row]').length,
                    threadRowCount: document.querySelectorAll('[data-app-action-sidebar-thread-row]').length,
                    animationCount: animations.length,
                    animationRate: animations[0]?.playbackRate || 0,
                    animationTransformRange: Math.max(0, ...animations.map(transformRange)),
                  };
                })()
                """);
            if (result.ValueKind == JsonValueKind.Object)
            {
                return result.Clone();
            }
        }

        throw new InvalidOperationException("No live themed Codex target exposed display-preference samples.");
    }
}
