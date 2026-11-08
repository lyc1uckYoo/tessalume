using System.Text.Json;
using System.Text.Json.Nodes;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Runtime;

public sealed partial class ThemeRuntime : IAsyncDisposable
{
    public const int ContractVersion = 4;
    private static readonly TimeSpan TargetReconciliationInterval = TimeSpan.FromMilliseconds(750);
    private const int KnownTargetVerificationInterval = 8;
    private const string ThemeScriptFailureMarker = "TESSALUME_THEME_SCRIPT:";
    private const string RemoveCompatibleRuntimesScript = """
        (async () => {
          const candidates = new Set();
          for (const direct of [window.__TESSALUME_RUNTIME__, window.__CODEX_THEME_STUDIO_RUNTIME__]) {
            if (direct && typeof direct.dispose === 'function') candidates.add(direct);
          }
          for (const key of Object.getOwnPropertyNames(window)) {
            try {
              const descriptor = Object.getOwnPropertyDescriptor(window, key);
              const candidate = descriptor && 'value' in descriptor ? descriptor.value : null;
              if (
                candidate &&
                typeof candidate === 'object' &&
                typeof candidate.dispose === 'function' &&
                typeof candidate.themeId === 'string' &&
                typeof candidate.fingerprint === 'string' &&
                candidate.context &&
                typeof candidate.context.mountCanonicalTheme === 'function'
              ) {
                candidates.add(candidate);
              }
            } catch { continue; }
          }
          for (const candidate of candidates) await candidate.dispose();
          window.__CODEX_DREAM_SKIN_STATE__?.cleanup?.();
          delete window.__TESSALUME_RUNTIME__;
          delete window.__TESSALUME_THEME_ID__;
          delete window.__TESSALUME_STAGED_ASSETS__;
          delete window.__TESSALUME_STAGED_VISUAL_SETTINGS__;
          delete window.__TESSALUME_STAGED_VISUAL_IMAGES__;
          delete window.__CODEX_THEME_STUDIO_RUNTIME__;
          delete window.__CODEX_THEME_STUDIO_THEME_ID__;
          delete window.__CODEX_THEME_STUDIO_STAGED_ASSETS__;
          for (const name of Array.from(document.documentElement.style)) {
            if (name.startsWith('--tessalume-visual-')) {
              document.documentElement.style.removeProperty(name);
            }
          }
          delete document.documentElement.dataset.tessalumeReadability;
          delete document.documentElement.dataset.tessalumeMotion;
          delete document.documentElement.dataset.tessalumeTextScale;
          delete document.documentElement.dataset.tessalumeDensity;
          delete document.documentElement.dataset.tessalumeVisualPlacement;
          return true;
        })()
        """;

    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly SemaphoreSlim _watcherLifecycleLock = new(1, 1);
    private readonly LoopbackCdpDiscovery _discovery;
    private readonly ThemePayloadBuilder _payloadBuilder;
    private readonly ImageDataUrlCache _visualImageDataUrlCache;
    private readonly Func<ThemeVisualSettings, ThemeVisualSettings>? _visualSettingsResolver;
    private readonly ThemeRuntimeAcceptanceProbe _acceptanceProbe;
    private CancellationTokenSource? _watcherCancellation;
    private Task? _watcherTask;
    private int _disposeStarted;
    private bool _disposed;
    private static readonly JsonSerializerOptions VisualSettingsJsonOptions = new(JsonSerializerDefaults.Web);

    public ThemeRuntime(
        LoopbackCdpDiscovery discovery,
        ThemePayloadBuilder payloadBuilder,
        Func<ThemeVisualSettings, ThemeVisualSettings>? visualSettingsResolver = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _payloadBuilder = payloadBuilder ?? throw new ArgumentNullException(nameof(payloadBuilder));
        _visualImageDataUrlCache = new ImageDataUrlCache();
        _visualSettingsResolver = visualSettingsResolver;
        _acceptanceProbe = new ThemeRuntimeAcceptanceProbe(_discovery);
    }

    public event EventHandler<string>? StatusChanged;
    public async Task StartAsync(int port, ThemePackage package, CancellationToken cancellationToken = default)
        => await StartAsync(port, package, new ThemeVisualSettings(), cancellationToken);

    public async Task StartAsync(
        int port,
        ThemePackage package,
        ThemeVisualSettings visualSettings,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var payload = await BuildPayloadAsync(package, cancellationToken);
        var visualSettingsPayload = await BuildVisualSettingsPayloadAsync(visualSettings, cancellationToken);

        await _watcherLifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopWatcherCoreAsync();

            var processedTargets = new HashSet<string>(StringComparer.Ordinal);
            await ApplyToAllAsync(
                port,
                package,
                payload,
                visualSettingsPayload,
                force: true,
                processedTargets,
                cancellationToken);

            var watcherCancellation = new CancellationTokenSource();
            _watcherCancellation = watcherCancellation;
            _watcherTask = ReconcileTargetsLoopAsync(
                port,
                package,
                payload,
                visualSettingsPayload,
                processedTargets,
                watcherCancellation.Token);
        }
        finally
        {
            _watcherLifecycleLock.Release();
        }

        StatusChanged?.Invoke(this, $"{package.Manifest.Name} 已应用");
    }
    public async Task PreflightAsync(
        int port,
        ThemePackage package,
        CancellationToken cancellationToken = default)
    {
        _ = await BuildPayloadAsync(package, cancellationToken);

        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var targets = await _discovery.DiscoverAsync(port, cancellationToken);
            if (targets.Count == 0)
            {
                throw new ThemeRuntimeException(
                    ThemeRuntimeFailureStage.PageTargetsMissing,
                    $"本机端口 {port} 已打开，但没有发现可注入的 Codex 页面。");
            }

            foreach (var target in targets)
            {
                await using var session = new CdpSession();
                await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
                _ = await session.EvaluateAsync("!!document.documentElement", cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ThemeRuntimeException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ThemeRuntimeException(
                ThemeRuntimeFailureStage.RuntimeInjectionFailed,
                "已发现 Codex 页面，但主题运行时无法建立安全的本机连接。",
                exception);
        }
        finally
        {
            _applyLock.Release();
        }
    }
    public async Task RemoveAsync(int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _watcherLifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await StopWatcherCoreAsync();
            await _applyLock.WaitAsync(cancellationToken);
            try
            {
                var targets = await _discovery.DiscoverAsync(port, cancellationToken);
                foreach (var target in targets)
                {
                    await using var session = new CdpSession();
                    await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
                    await session.EvaluateAsync(RemoveCompatibleRuntimesScript, cancellationToken);
                }
            }
            finally
            {
                _applyLock.Release();
            }
        }
        finally
        {
            _watcherLifecycleLock.Release();
        }

        StatusChanged?.Invoke(this, "已恢复 Codex 默认外观");
    }
    public async Task<bool> ReadColorSchemeAsync(int port, CancellationToken cancellationToken = default)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var targets = await _discovery.DiscoverAsync(port, cancellationToken);
            if (targets.Count == 0)
            {
                throw new InvalidOperationException($"本机端口 {port} 尚未发现 Codex 页面");
            }

            CdpTarget? mainTarget = null;
            foreach (var target in targets.OrderBy(target =>
                         target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            {
                await using var session = new CdpSession();
                await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
                var isMain = await session.EvaluateAsync(
                    "!!document.querySelector('main') && " +
                    "!document.documentElement.classList.contains('compact-window') && " +
                    "!new URLSearchParams(location.search).has('initialRoute')",
                    cancellationToken);
                if (isMain.ValueKind == JsonValueKind.True)
                {
                    mainTarget = target;
                    break;
                }
            }

            mainTarget ??= targets.FirstOrDefault(target =>
                !target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase)) ?? targets[0];
            await using var mainSession = new CdpSession();
            await mainSession.ConnectAsync(mainTarget.WebSocketDebuggerUrl, cancellationToken);
            var result = await mainSession.EvaluateAsync(
                "window.electronBridge?.getSystemThemeVariant?.() === 'dark'",
                cancellationToken);
            if (result.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidOperationException("Codex 返回了无法识别的外观状态");
            }

            return result.GetBoolean();
        }
        finally
        {
            _applyLock.Release();
        }
    }
    public async Task<ThemeRuntimeAcceptanceSnapshot> InspectAcceptanceAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            return await _acceptanceProbe.InspectAsync(port, cancellationToken);
        }
        finally
        {
            _applyLock.Release();
        }
    }
    public async Task<IReadOnlyList<ThemeRuntimeAcceptanceSnapshot>> InspectResponsiveAcceptanceAsync(
        int port,
        IReadOnlyList<int> viewportWidths,
        CancellationToken cancellationToken = default)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            return await _acceptanceProbe.InspectResponsiveAsync(port, viewportWidths, cancellationToken);
        }
        finally
        {
            _applyLock.Release();
        }
    }
    public async Task<bool> ToggleColorSchemeAsync(int port, CancellationToken cancellationToken = default)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var targets = await _discovery.DiscoverAsync(port, cancellationToken);
            if (targets.Count == 0)
            {
                throw new InvalidOperationException($"本机端口 {port} 尚未发现 Codex 页面");
            }

            CdpTarget? mainTarget = null;
            foreach (var target in targets.OrderBy(target =>
                         target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            {
                await using var session = new CdpSession();
                await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
                var isMain = await session.EvaluateAsync(
                    "!!document.querySelector('main') && " +
                    "!document.documentElement.classList.contains('compact-window') && " +
                    "!new URLSearchParams(location.search).has('initialRoute')",
                    cancellationToken);
                if (isMain.ValueKind == JsonValueKind.True)
                {
                    mainTarget = target;
                    break;
                }
            }

            mainTarget ??= targets.FirstOrDefault(target =>
                !target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase)) ?? targets[0];
            await using var mainSession = new CdpSession();
            await mainSession.ConnectAsync(mainTarget.WebSocketDebuggerUrl, cancellationToken);
            var result = await mainSession.EvaluateAsync(
                """
                (async () => {
                  const bridge = window.electronBridge;
                  if (typeof bridge?.getSystemThemeVariant !== 'function') {
                    throw new Error('当前 Codex 版本未提供原生外观设置接口');
                  }

                  const current = bridge.getSystemThemeVariant() === 'dark' ? 'dark' : 'light';
                  const next = current === 'dark' ? 'light' : 'dark';
                  const findSettingsService = async () => {
                    for (const script of Array.from(document.scripts)) {
                      if (!script.src) continue;
                      try {
                        const entry = await import(script.src);
                        const require = entry?.__webpack_require__;
                        if (typeof require !== 'function') continue;
                        for (const cachedModule of Object.values(require.c || {})) {
                          const settings = cachedModule?.exports?.appServices?.settings;
                          if (typeof settings?.write === 'function') return settings;
                        }
                      } catch {
                        // Entry chunks can include non-module preload scripts. Keep scanning.
                      }
                    }
                    return null;
                  };
                  const writeWithLegacyBridge = async () => {
                    if (typeof bridge?.sendMessageFromView !== 'function') {
                      throw new Error('当前 Codex 版本未提供可用的外观设置服务');
                    }
                    const requestId = crypto.randomUUID();
                    await new Promise((resolve, reject) => {
                      const timeout = setTimeout(() => {
                        window.removeEventListener('message', onMessage);
                        reject(new Error('Codex 原生外观设置响应超时'));
                      }, 5000);
                      const onMessage = event => {
                        const message = event.data;
                        if (message?.type !== 'fetch-response' || message.requestId !== requestId) return;
                        clearTimeout(timeout);
                        window.removeEventListener('message', onMessage);
                        if (message.responseType === 'error') {
                          reject(new Error(message.error || 'Codex 拒绝了外观设置'));
                        } else {
                          resolve(message);
                        }
                      };
                      window.addEventListener('message', onMessage);
                      bridge.sendMessageFromView({
                        type: 'fetch',
                        requestId,
                        method: 'POST',
                        url: 'vscode://codex/set-setting',
                        headers: { 'content-type': 'application/json' },
                        body: JSON.stringify({ key: 'appearanceTheme', value: next })
                      }).catch(error => {
                        clearTimeout(timeout);
                        window.removeEventListener('message', onMessage);
                        reject(error);
                      });
                    });
                  };

                  const settings = await findSettingsService();
                  if (settings) {
                    await settings.write('appearanceTheme', next);
                  } else {
                    // Codex builds before the settings service exposed its renderer API.
                    await writeWithLegacyBridge();
                  }

                  const findQueryClient = () => {
                    const rootNode = document.querySelector('#root') || document.body;
                    const fiberKey = Object.keys(rootNode).find(key =>
                      key.startsWith('__reactContainer$') || key.startsWith('__reactFiber$'));
                    const rootFiber = fiberKey ? rootNode[fiberKey] : null;
                    const queue = rootFiber ? [rootFiber] : [];
                    const seen = new Set();
                    while (queue.length && seen.size < 12000) {
                      const fiber = queue.shift();
                      if (!fiber || seen.has(fiber)) continue;
                      seen.add(fiber);
                      let hook = fiber.memoizedState;
                      for (let index = 0; hook && index < 24; index++, hook = hook.next) {
                        const candidate = hook.memoizedState;
                        if (candidate &&
                            typeof candidate.getQueryCache === 'function' &&
                            typeof candidate.invalidateQueries === 'function') {
                          return candidate;
                        }
                      }
                      if (fiber.child) queue.push(fiber.child);
                      if (fiber.sibling) queue.push(fiber.sibling);
                    }
                    return null;
                  };

                  const queryClient = findQueryClient();
                  if (!queryClient) {
                    throw new Error('无法定位 Codex 当前使用的设置缓存');
                  }
                  const settingsKey = ['vscode', 'get-settings'];
                  await queryClient.invalidateQueries({ queryKey: settingsKey, exact: true });
                  await queryClient.refetchQueries({ queryKey: settingsKey, exact: true, type: 'active' });

                  const deadline = Date.now() + 5000;
                  const resolvedTheme = () => document.documentElement.getAttribute('data-theme') ||
                    (document.documentElement.classList.contains('electron-dark') ? 'dark' : 'light');
                  while ((resolvedTheme() !== next ||
                          bridge.getSystemThemeVariant() !== next) && Date.now() < deadline) {
                    await new Promise(resolve => setTimeout(resolve, 50));
                  }
                  if (resolvedTheme() !== next ||
                      bridge.getSystemThemeVariant() !== next) {
                    throw new Error('Codex 未完成原生外观切换');
                  }
                  // Application versions before 1.0.2 wrote this inline value directly.
                  // Native Codex owns appearance now, so remove the stale override and
                  // let its resolved theme class and design tokens drive every component.
                  document.documentElement.style.removeProperty('color-scheme');
                  return next === 'dark';
                })()
                """,
                cancellationToken);

            if (result.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidOperationException("Codex 返回了无法识别的外观状态");
            }

            var useDark = result.GetBoolean();
            StatusChanged?.Invoke(this, useDark ? "Codex 已切换为暗色" : "Codex 已切换为亮色");
            return useDark;
        }
        finally
        {
            _applyLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _watcherLifecycleLock.WaitAsync();
        try
        {
            await StopWatcherCoreAsync();
        }
        finally
        {
            _watcherLifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;

        await _watcherLifecycleLock.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            await StopWatcherCoreAsync();
        }
        finally
        {
            _watcherLifecycleLock.Release();
        }

        _visualImageDataUrlCache.Dispose();
        _applyLock.Dispose();
        _discovery.Dispose();
        _watcherLifecycleLock.Dispose();
    }

    private async Task ApplyToAllAsync(
        int port,
        ThemePackage package,
        string payload,
        VisualSettingsPayload visualSettings,
        bool force,
        HashSet<string>? processedTargets,
        CancellationToken cancellationToken)
        => await ApplyToAllAsync(
            port,
            package,
            payload,
            visualSettings,
            force,
            processedTargets,
            skipKnownTargets: false,
            cancellationToken);

    private async Task ApplyToAllAsync(
        int port,
        ThemePackage package,
        string payload,
        VisualSettingsPayload visualSettings,
        bool force,
        HashSet<string>? processedTargets,
        bool skipKnownTargets,
        CancellationToken cancellationToken)
    {
        await _applyLock.WaitAsync(cancellationToken);
        IReadOnlyList<CdpTarget> targets = [];
        try
        {
            targets = await _discovery.DiscoverAsync(port, cancellationToken);
            if (targets.Count == 0)
            {
                throw new ThemeRuntimeException(
                    ThemeRuntimeFailureStage.PageTargetsMissing,
                    $"本机端口 {port} 已打开，但没有发现可注入的 Codex 页面。");
            }

            if (processedTargets is not null)
            {
                var currentTargetKeys = targets.Select(GetTargetKey).ToHashSet(StringComparer.Ordinal);
                processedTargets.IntersectWith(currentTargetKeys);
            }

            foreach (var target in targets)
            {
                var targetKey = GetTargetKey(target);
                if (!force && skipKnownTargets && processedTargets?.Contains(targetKey) == true)
                {
                    continue;
                }

                await ApplyTargetAsync(
                    target,
                    package,
                    payload,
                    visualSettings,
                    force,
                    processedTargets,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupTargetsAsync(targets);
            throw;
        }
        catch (ThemeRuntimeException)
        {
            await CleanupTargetsAsync(targets);
            throw;
        }
        catch (Exception exception)
        {
            await CleanupTargetsAsync(targets);
            throw new ThemeRuntimeException(
                ThemeRuntimeFailureStage.RuntimeInjectionFailed,
                "主题运行时无法完成本机注入，所有 Codex 页面已统一恢复默认外观。",
                exception);
        }
        finally
        {
            _applyLock.Release();
        }
    }

}
