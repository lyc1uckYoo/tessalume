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
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
                    repositoryRoot,
                    "src",
                    "Tessalume.App",
                    "Compatibility",
                    "theme-runtime-v2.js"),
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
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
                    repositoryRoot,
                    "src",
                    "Tessalume.App",
                    "Compatibility",
                    "theme-runtime-v2.js"),
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
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
                    repositoryRoot,
                    "src",
                    "Tessalume.App",
                    "Compatibility",
                    "theme-runtime-v2.js"),
            }));
        await runtime.StartAsync(port, package);
        await runtime.StopAsync();
        Console.WriteLine($"Theme applied: {package.Manifest.Id}");
        return 0;
    }

    static async Task<string> BuildPayloadAsync(string repositoryRoot, ThemePackage package) =>
        await new ThemePayloadBuilder(new Dictionary<string, string>
        {
            [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
                repositoryRoot,
                "src",
                "Tessalume.App",
                "Compatibility",
                "theme-runtime-v2.js"),
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
