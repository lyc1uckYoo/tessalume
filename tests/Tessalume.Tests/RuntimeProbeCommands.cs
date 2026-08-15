using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal static partial class TestSuite
{
    static async Task<int> CheckLiveUpdateAsync(Version currentVersion)
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-live-update-{Guid.NewGuid():N}");
        try
        {
            using var client = new ReleaseUpdateClient(
                BrandInfo.RepositoryOwner,
                BrandInfo.RepositoryName,
                dataDirectory,
                currentVersion);
            var release = await client.CheckLatestAsync();
            if (release is null)
            {
                Console.Error.WriteLine($"No release newer than {currentVersion} was found.");
                return 1;
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Version = release.Version.ToString(3),
                release.VersionLabel,
                DownloadUri = release.DownloadUri.AbsoluteUri,
                release.DownloadSize,
                release.Sha256,
            }));
            return 0;
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    static async Task<int> ProbeVisualControlsAsync(
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

        var testAdjustment = new ThemeArtworkAdjustment
        {
            Brightness = 91,
            Contrast = 112,
            Saturation = 77,
            Opacity = 83,
            Zoom = 117,
            OffsetX = 23,
            OffsetY = -11,
            Grayscale = 14,
            HueRotation = 27,
            Blur = 1.5,
        };
        var testMode = new ThemeVisualModeSettings
        {
            Hero = testAdjustment,
            Sidebar = testAdjustment,
            Chat = testAdjustment,
        };
        var testSettings = new ThemeVisualSettings { Light = testMode, Dark = testMode };
        var repositoryRoot = FindRepositoryRoot();
        await using var runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>
            {
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
            }));

        JsonElement? probe = null;
        try
        {
            await runtime.StartAsync(port, package, originalSettings);
            await runtime.ApplyVisualSettingsAsync(port, package.Manifest.Id, testSettings);
            var targets = await new LoopbackCdpDiscovery().DiscoverAsync(port);
            foreach (var target in targets)
            {
                await using var session = new CdpSession();
                await session.ConnectAsync(target.WebSocketDebuggerUrl);
                var result = await session.EvaluateAsync(
                    """
                    (() => {
                      if (!window.__TESSALUME_RUNTIME__) return null;
                      const style = document.documentElement.style;
                      const read = name => style.getPropertyValue(name).trim();
                      return {
                        filter: read('--tessalume-visual-hero-light-filter'),
                        opacity: read('--tessalume-visual-sidebar-dark-opacity'),
                        translate: read('--tessalume-visual-chat-light-translate'),
                        scale: read('--tessalume-visual-chat-dark-scale'),
                        supportsTranslate: CSS.supports('translate', '23px -11px'),
                        supportsScale: CSS.supports('scale', '1.17')
                      };
                    })()
                    """);
                if (result.ValueKind == JsonValueKind.Object)
                {
                    probe = result.Clone();
                    break;
                }
            }
        }
        finally
        {
            try
            {
                await runtime.ApplyVisualSettingsAsync(port, package.Manifest.Id, originalSettings);
            }
            finally
            {
                await runtime.StopAsync();
            }
        }

        if (probe is not { } value ||
            !value.GetProperty("filter").GetString()!.Contains("grayscale(0.14)", StringComparison.Ordinal) ||
            !value.GetProperty("filter").GetString()!.Contains("hue-rotate(27deg)", StringComparison.Ordinal) ||
            !value.GetProperty("filter").GetString()!.Contains("blur(1.5px)", StringComparison.Ordinal) ||
            value.GetProperty("opacity").GetString() != "0.83" ||
            value.GetProperty("translate").GetString() != "23px -11px" ||
            value.GetProperty("scale").GetString() != "1.17" ||
            !value.GetProperty("supportsTranslate").GetBoolean() ||
            !value.GetProperty("supportsScale").GetBoolean())
        {
            Console.Error.WriteLine(probe?.GetRawText() ?? "No themed Codex target returned visual settings.");
            return 3;
        }

        Console.WriteLine(value.GetRawText());
        Console.WriteLine("Original visual settings restored.");
        return 0;
    }

    static async Task<int> ProbeRuntimeAsync(int port)
    {
        using var discovery = new LoopbackCdpDiscovery();
        var targets = await discovery.DiscoverAsync(port);
        if (targets.Count == 0)
        {
            Console.Error.WriteLine($"No Codex targets found on {port}.");
            return 2;
        }

        foreach (var target in targets.OrderBy(target =>
                     target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            var result = await session.EvaluateAsync(
                "({ themeId: window.__TESSALUME_THEME_ID__ || null, installed: !!window.__TESSALUME_THEME_ID__, runtime: !!window.__TESSALUME_RUNTIME__, root: !!document.getElementById('tessalume-theme-root'), style: !!document.getElementById('tessalume-theme-style') || !!document.getElementById('codex-dream-skin-style'), chrome: !!document.getElementById('codex-dream-skin-chrome'), title: document.querySelector('.dream-brand b')?.textContent || document.querySelector('.example-theme-widget b')?.textContent || null, exampleMounted: document.documentElement.getAttribute('data-example-theme-mounted') })");
            Console.WriteLine(result);
        }

        return 0;
    }

    static async Task<int> ProbeComposerAsync(int port, bool applyAlias = false)
    {
        using var discovery = new LoopbackCdpDiscovery();
        var targets = await discovery.DiscoverAsync(port);
        foreach (var target in targets.Where(target =>
                     !target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase)))
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            if (applyAlias)
            {
                await session.EvaluateAsync(
                    """
                    (() => {
                      const editor = document.querySelector('[data-codex-composer="true"]');
                      const surface = editor?.closest('[class*="ComposerLayoutRoot"]');
                      surface?.classList.add('composer-surface-chrome');
                      return Boolean(surface);
                    })()
                    """);
            }
            var result = await session.EvaluateAsync(
                """
                (() => {
                  const styleOf = element => {
                    const style = getComputedStyle(element);
                    const box = element.getBoundingClientRect();
                    return {
                      tag: element.tagName,
                      className: typeof element.className === 'string' ? element.className : '',
                      role: element.getAttribute('role'),
                      ariaLabel: element.getAttribute('aria-label'),
                      tessalumeSurface: element.getAttribute('data-tessalume-surface'),
                      background: style.background,
                      backgroundColor: style.backgroundColor,
                      backgroundImage: style.backgroundImage,
                      boxShadow: style.boxShadow,
                      border: style.border,
                      color: style.color,
                      display: style.display,
                      visibility: style.visibility,
                      opacity: style.opacity,
                      position: style.position,
                      zIndex: style.zIndex,
                      overflow: style.overflow,
                      box: { left: Math.round(box.left), top: Math.round(box.top), width: Math.round(box.width), height: Math.round(box.height) }
                    };
                  };
                  const editors = Array.from(document.querySelectorAll('textarea,input,[contenteditable="true"]'))
                    .filter(element => {
                      const box = element.getBoundingClientRect();
                      const style = getComputedStyle(element);
                      return box.width > 240 && box.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
                    })
                    .sort((left, right) => right.getBoundingClientRect().bottom - left.getBoundingClientRect().bottom);
                  const editor = editors[0] || null;
                  const composer = document.querySelector('.composer-surface-chrome');
                  if (!editor && !composer) return null;
                  const ancestors = [];
                  for (let node = editor || composer, depth = 0; node && depth < 14; node = node.parentElement, depth += 1) {
                    ancestors.push(styleOf(node));
                  }
                  const surface = composer || (ancestors.length > 3
                    ? (editor?.parentElement?.parentElement?.parentElement || editor)
                    : editor);
                  const descendants = Array.from(surface.querySelectorAll('*'))
                    .filter(element => {
                      const style = getComputedStyle(element);
                      return element.matches('textarea,input,[contenteditable="true"],button,[role="button"]') ||
                        style.backgroundImage !== 'none' ||
                        style.backgroundColor !== 'rgba(0, 0, 0, 0)' ||
                        style.boxShadow !== 'none';
                    })
                    .slice(0, 80)
                    .map(styleOf);
                  const rules = [];
                  for (const sheet of Array.from(document.styleSheets)) {
                    let cssRules;
                    try { cssRules = sheet.cssRules; } catch { continue; }
                    for (const rule of Array.from(cssRules || [])) {
                      if (rule.cssText && rule.cssText.includes('composer-surface-chrome')) {
                        rules.push({ owner: sheet.ownerNode?.id || sheet.ownerNode?.tagName || '', text: rule.cssText });
                      }
                    }
                  }
                  return {
                    url: location.href,
                    themeId: window.__TESSALUME_THEME_ID__ || null,
                    htmlClasses: document.documentElement.className,
                    editor: editor ? styleOf(editor) : null,
                    composer: composer ? styleOf(composer) : null,
                    composerFooter: composer ? (() => {
                      const footer = composer.querySelector('[class*="ComposerLayoutFooter"], [class*="_footer_"]');
                      return footer ? styleOf(footer) : null;
                    })() : null,
                    before: composer ? (() => { const s = getComputedStyle(composer, '::before'); return { content: s.content, background: s.background, boxShadow: s.boxShadow, display: s.display }; })() : null,
                    after: composer ? (() => { const s = getComputedStyle(composer, '::after'); return { content: s.content, background: s.background, boxShadow: s.boxShadow, display: s.display }; })() : null,
                    ancestors,
                    descendants,
                    rules,
                    outerHTML: (composer || editor).outerHTML.slice(0, 24000)
                  };
                })()
                """);
            if (result.ValueKind == JsonValueKind.Object)
            {
                Console.WriteLine(result.GetRawText());
                return 0;
            }
        }

        Console.Error.WriteLine("No live Codex composer was found.");
        return 2;
    }

    static async Task<int> RemoveRuntimeAsync(int port)
    {
        await using var runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>()));
        await runtime.RemoveAsync(port);
        Console.WriteLine("Theme removed.");
        return 0;
    }

    static async Task<int> ApplyRuntimeAsync(int port)
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = await LoadRepresentativePackageAsync(repositoryRoot);
        await using var runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>
            {
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
            }));
        await runtime.StartAsync(port, package);
        await runtime.StopAsync();
        Console.WriteLine("Theme applied.");
        return 0;
    }

    static async Task<int> ApplyPackageRuntimeAsync(int port, string packagePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = (await new ThemePackageLoader().LoadAsync(packagePath)).Package
            ?? throw new InvalidOperationException("The requested theme package could not be loaded.");
        await using var runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>
            {
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
            }));
        await runtime.StartAsync(port, package);
        await runtime.StopAsync();
        Console.WriteLine($"Theme applied: {package.Manifest.Id}");
        return 0;
    }

    static async Task<int> ApplyPackageDefaultsRuntimeAsync(int port, string packagePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = (await new ThemePackageLoader().LoadAsync(packagePath)).Package
            ?? throw new InvalidOperationException("The requested theme package could not be loaded.");
        var defaults = await new ArtworkThemeDefaultsStore().LoadAsync(package);
        var settings = ThemeArtworkSettingsResolver.Resolve(defaults.Defaults, null).Settings;
        await using var runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>
            {
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
            }));
        await runtime.StartAsync(port, package, settings);
        Console.WriteLine($"Theme defaults applied: {package.Manifest.Id}");
        return 0;
    }

    static async Task<int> ProbeThemeSwitchContinuityAsync(
        int port,
        string fromPackagePath,
        string toPackagePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var loader = new ThemePackageLoader();
        var fromPackage = (await loader.LoadAsync(fromPackagePath)).Package
            ?? throw new InvalidOperationException("The source theme package could not be loaded.");
        var toPackage = (await loader.LoadAsync(toPackagePath)).Package
            ?? throw new InvalidOperationException("The destination theme package could not be loaded.");
        var defaultsStore = new ArtworkThemeDefaultsStore();
        var fromSettings = ThemeArtworkSettingsResolver.Resolve(
            (await defaultsStore.LoadAsync(fromPackage)).Defaults,
            null).Settings;
        var toSettings = ThemeArtworkSettingsResolver.Resolve(
            (await defaultsStore.LoadAsync(toPackage)).Defaults,
            null).Settings;
        await using var runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>
            {
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
            }));

        await runtime.StartAsync(port, fromPackage, fromSettings);
        using var discovery = new LoopbackCdpDiscovery();
        var targets = await discovery.DiscoverAsync(port);
        var monitoredTargets = new List<CdpTarget>();
        foreach (var target in targets.Where(target =>
                     !target.Url.Contains("avatar-overlay", StringComparison.OrdinalIgnoreCase)))
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            var installed = await session.EvaluateAsync(
                "!!window.__TESSALUME_RUNTIME__ && !!document.getElementById('tessalume-theme-style')");
            if (installed.ValueKind != JsonValueKind.True) continue;
            await session.EvaluateAsync(
                """
                (() => {
                  const samples = [];
                  let running = true;
                  const sample = timestamp => {
                    const html = document.documentElement;
                    samples.push({
                      timestamp,
                      themeId: window.__TESSALUME_THEME_ID__ || null,
                      active: html.classList.contains('tessalume-theme-active'),
                      styleCount: document.querySelectorAll('#tessalume-theme-style').length,
                      rootCount: document.querySelectorAll('#tessalume-theme-root').length,
                      styleThemes: Array.from(document.querySelectorAll('#tessalume-theme-style'))
                        .map(node => node.dataset.themeId || null),
                      rootThemes: Array.from(document.querySelectorAll('#tessalume-theme-root'))
                        .map(node => node.dataset.themeId || null),
                      artworkAsset: Array.from(html.style).some(name =>
                        name.startsWith('--tessalume-asset-') &&
                        html.style.getPropertyValue(name).trim().startsWith('url(')),
                    });
                    if (running && samples.length < 600) requestAnimationFrame(sample);
                  };
                  window.__TESSALUME_HANDOFF_MONITOR__ = {
                    samples,
                    stop() { running = false; },
                  };
                  requestAnimationFrame(sample);
                  return true;
                })()
                """);
            monitoredTargets.Add(target);
        }

        if (monitoredTargets.Count == 0)
        {
            Console.Error.WriteLine("No themed Codex surface was available for continuity monitoring.");
            return 2;
        }

        await Task.Delay(80);
        var switchTimer = System.Diagnostics.Stopwatch.StartNew();
        await runtime.StartAsync(port, toPackage, toSettings);
        switchTimer.Stop();
        await Task.Delay(250);

        var passed = true;
        foreach (var target in monitoredTargets)
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            var result = await session.EvaluateAsync(
                """
                (() => {
                  const monitor = window.__TESSALUME_HANDOFF_MONITOR__;
                  monitor?.stop();
                  const samples = monitor?.samples || [];
                  const blankFrames = samples.filter(sample =>
                    !sample.active || sample.styleCount < 1 || sample.rootCount < 1 || !sample.artworkAsset);
                  const mixedFrames = samples.filter(sample => {
                    const visible = [...sample.styleThemes, ...sample.rootThemes].filter(Boolean);
                    return new Set(visible).size > 1 ||
                      (sample.themeId && visible.some(themeId => themeId !== sample.themeId));
                  });
                  return {
                    frameCount: samples.length,
                    blankFrameCount: blankFrames.length,
                    mixedFrameCount: mixedFrames.length,
                    firstThemeId: samples[0]?.themeId || null,
                    lastThemeId: samples.at(-1)?.themeId || null,
                    appearanceHandoffVersion: window.__TESSALUME_RUNTIME__?.appearanceHandoffVersion || 0,
                    blankFrames: blankFrames.slice(0, 8),
                    mixedFrames: mixedFrames.slice(0, 8),
                  };
                })()
                """);
            Console.WriteLine(result.GetRawText());
            passed &= result.GetProperty("frameCount").GetInt32() > 0 &&
                      result.GetProperty("blankFrameCount").GetInt32() == 0 &&
                      result.GetProperty("mixedFrameCount").GetInt32() == 0 &&
                      result.GetProperty("lastThemeId").GetString() == toPackage.Manifest.Id &&
                      result.GetProperty("appearanceHandoffVersion").GetInt32() == 2;
        }

        Console.WriteLine(
            $"Theme handoff: {fromPackage.Manifest.Id} -> {toPackage.Manifest.Id} " +
            $"({switchTimer.ElapsedMilliseconds} ms)");
        return passed ? 0 : 3;
    }

    static async Task<string> BuildPayloadAsync(string repositoryRoot, ThemePackage package) =>
        await new ThemePayloadBuilder(new Dictionary<string, string>
        {
            [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
        }).BuildAsync(package);

    static async Task<int> ProbeThemeModesAsync(int port)
    {
        using var discovery = new LoopbackCdpDiscovery();
        var targets = await discovery.DiscoverAsync(port);
        foreach (var target in targets)
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            var installed = await session.EvaluateAsync(
                "!!window.__TESSALUME_RUNTIME__ && document.documentElement.classList.contains('tessalume-theme-active')");
            if (installed.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            var result = await session.EvaluateAsync(
                """
                (() => {
                  const root = document.documentElement;
                  const wasDark = root.classList.contains('electron-dark');
                  const sample = () => ({
                    colorScheme: getComputedStyle(root).colorScheme,
                    textColor: getComputedStyle(document.body).color,
                    composerBackground: getComputedStyle(document.querySelector('.composer-surface-chrome') || document.body).backgroundColor
                  });
                  root.classList.remove('electron-dark');
                  const light = sample();
                  root.classList.add('electron-dark');
                  const dark = sample();
                  root.classList.toggle('electron-dark', wasDark);
                  return { light, dark, different: JSON.stringify(light) !== JSON.stringify(dark) };
                })()
                """);
            Console.WriteLine(result);
            return result.GetProperty("different").GetBoolean() ? 0 : 3;
        }

        Console.Error.WriteLine("No themed Codex target found.");
        return 2;
    }

    static async Task<int> ToggleColorSchemeAsync(int port)
    {
        await using var runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>()));
        var dark = await runtime.ToggleColorSchemeAsync(port);
        Console.WriteLine(dark ? "dark" : "light");
        return 0;
    }

    static async Task<int> ProbeAppearanceStateAsync(int port)
    {
        using var discovery = new LoopbackCdpDiscovery();
        var targets = await discovery.DiscoverAsync(port);
        foreach (var target in targets.OrderBy(target =>
                     target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            var result = await session.EvaluateAsync(
                """
                (() => {
                  const root = document.documentElement;
                  const interesting = ([key]) => /theme|appearance|color|dark|light|scheme/i.test(key);
                  const storage = store => Object.entries(store).filter(interesting);
                  return {
                    isMain: !!document.querySelector('main') &&
                      !root.classList.contains('compact-window') &&
                      !new URLSearchParams(location.search).has('initialRoute'),
                    url: location.href,
                    rootClass: root.className,
                    rootAttributes: Object.fromEntries([...root.attributes].map(x => [x.name, x.value])),
                    bodyAttributes: Object.fromEntries([...document.body.attributes].map(x => [x.name, x.value])),
                    colorScheme: getComputedStyle(root).colorScheme,
                    prefersDark: matchMedia('(prefers-color-scheme: dark)').matches,
                    localStorage: storage(localStorage),
                    sessionStorage: storage(sessionStorage),
                    globalKeys: Object.keys(window).filter(key => /theme|appearance|color|dark|light|scheme/i.test(key)).slice(0, 100)
                  };
                })()
                """);
            if (result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("isMain", out var isMain) && isMain.GetBoolean())
            {
                Console.WriteLine(result);
                return 0;
            }
        }

        Console.Error.WriteLine("No main Codex target found.");
        return 2;
    }

    static async Task<int> ProbeAppearanceBundlesAsync(int port)
    {
        using var discovery = new LoopbackCdpDiscovery();
        var targets = await discovery.DiscoverAsync(port);
        foreach (var target in targets)
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            var result = await session.EvaluateAsync(
                """
                (async () => {
                  if (!document.querySelector('main')) return null;
                  const urls = [...new Set([
                    ...[...document.scripts].map(x => x.src),
                    ...performance.getEntriesByType('resource').map(x => x.name)
                  ].filter(url => /\.m?js(?:\?|$)/i.test(url)))];
                  const needles = ['electron-dark', 'color-scheme', 'appearance', 'setTheme', 'themePreference'];
                  const matches = [];
                  for (const url of urls) {
                    let text;
                    try { text = await fetch(url).then(response => response.text()); } catch { continue; }
                    for (const needle of needles) {
                      let offset = 0;
                      for (let count = 0; count < 4; count++) {
                        const index = text.indexOf(needle, offset);
                        if (index < 0) break;
                        matches.push({ url, needle, index, snippet: text.slice(Math.max(0,index-500),index+900) });
                        offset = index + needle.length;
                      }
                    }
                  }
                  return matches.slice(0, 60);
                })()
                """);
            if (result.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(result);
                return 0;
            }
        }

        Console.Error.WriteLine("No main Codex target found.");
        return 2;
    }

    static async Task<int> ProbeQueryClientsAsync(int port)
    {
        using var discovery = new LoopbackCdpDiscovery();
        var targets = await discovery.DiscoverAsync(port);
        foreach (var target in targets)
        {
            await using var session = new CdpSession();
            await session.ConnectAsync(target.WebSocketDebuggerUrl);
            var result = await session.EvaluateAsync(
                """
                (() => {
                  if (!document.querySelector('main')) return null;
                  const rootNode = document.querySelector('#root') || document.body;
                  const fiberKey = Object.keys(rootNode).find(key => key.startsWith('__reactContainer$') || key.startsWith('__reactFiber$'));
                  let root = fiberKey ? rootNode[fiberKey] : null;
                  const seenFibers = new Set();
                  const seenObjects = new WeakSet();
                  const clients = [];
                  const inspect = (value, path, depth = 0) => {
                    if (!value || (typeof value !== 'object' && typeof value !== 'function') || seenObjects.has(value) || depth > 5) return;
                    seenObjects.add(value);
                    try {
                      if (typeof value.getQueryCache === 'function' && typeof value.invalidateQueries === 'function') {
                        const queries = value.getQueryCache().getAll().map(q => ({ hash: q.queryHash, key: q.queryKey, state: q.state?.status }));
                        clients.push({ path, queries: queries.filter(q => JSON.stringify(q).includes('settings')).slice(0, 20), total: queries.length });
                        return;
                      }
                    } catch {}
                    let entries;
                    try { entries = Object.entries(value); } catch { return; }
                    for (const [key, child] of entries.slice(0, 80)) {
                      if (/^(return|child|sibling|stateNode|alternate|_owner)$/.test(key)) continue;
                      inspect(child, `${path}.${key}`, depth + 1);
                    }
                  };
                  const queue = root ? [root] : [];
                  while (queue.length && seenFibers.size < 12000 && clients.length < 10) {
                    const fiber = queue.shift();
                    if (!fiber || seenFibers.has(fiber)) continue;
                    seenFibers.add(fiber);
                    inspect(fiber.memoizedProps, 'fiber.memoizedProps');
                    inspect(fiber.memoizedState, 'fiber.memoizedState');
                    inspect(fiber.dependencies, 'fiber.dependencies');
                    if (fiber.child) queue.push(fiber.child);
                    if (fiber.sibling) queue.push(fiber.sibling);
                  }
                  return { fiberKey, fibers: seenFibers.size, clients };
                })()
                """);
            if (result.ValueKind == JsonValueKind.Object)
            {
                Console.WriteLine(result);
                return 0;
            }
        }

        Console.Error.WriteLine("No main Codex target found.");
        return 2;
    }

}
