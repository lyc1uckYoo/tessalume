using System.Text.Json;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Runtime;

public sealed class ThemeRuntime(
    LoopbackCdpDiscovery discovery,
    ThemePayloadBuilder payloadBuilder) : IAsyncDisposable
{
    public const int ContractVersion = 2;

    private const string ThemeScriptFailureMarker = "TESSALUME_THEME_SCRIPT:";

    private const string RemoveCompatibleRuntimesScript = """
        (async () => {
          const candidates = new Set();
          for (const direct of [window.__TESSALUME_RUNTIME__, window.__CODEX_THEME_STUDIO_RUNTIME__]) {
            if (direct && typeof direct.dispose === 'function') candidates.add(direct);
          }
          for (const key of Object.getOwnPropertyNames(window)) {
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
          }
          for (const candidate of candidates) await candidate.dispose();
          window.__CODEX_DREAM_SKIN_STATE__?.cleanup?.();
          delete window.__TESSALUME_RUNTIME__;
          delete window.__TESSALUME_THEME_ID__;
          delete window.__TESSALUME_STAGED_ASSETS__;
          delete window.__CODEX_THEME_STUDIO_RUNTIME__;
          delete window.__CODEX_THEME_STUDIO_THEME_ID__;
          delete window.__CODEX_THEME_STUDIO_STAGED_ASSETS__;
          for (const name of Array.from(document.documentElement.style)) {
            if (name.startsWith('--tessalume-visual-')) {
              document.documentElement.style.removeProperty(name);
            }
          }
          return true;
        })()
        """;

    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private static readonly JsonSerializerOptions VisualSettingsJsonOptions = new(JsonSerializerDefaults.Web);

    public event EventHandler<string>? StatusChanged;

    public async Task StartAsync(int port, ThemePackage package, CancellationToken cancellationToken = default)
        => await StartAsync(port, package, new ThemeVisualSettings(), cancellationToken);

    public async Task StartAsync(
        int port,
        ThemePackage package,
        ThemeVisualSettings visualSettings,
        CancellationToken cancellationToken = default)
    {
        var payload = await BuildPayloadAsync(package, cancellationToken);
        await ApplyToAllAsync(
            port,
            package,
            payload,
            SerializeVisualSettings(visualSettings),
            force: true,
            processedTargets: null,
            cancellationToken);

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
            var targets = await discovery.DiscoverAsync(port, cancellationToken);
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

    public async Task ApplyVisualSettingsAsync(
        int port,
        string themeId,
        ThemeVisualSettings visualSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        var themeIdJson = JsonSerializer.Serialize(themeId);
        var settingsJson = SerializeVisualSettings(visualSettings);
        var expression = $$"""
            (() => {
              const runtime = window.__TESSALUME_RUNTIME__;
              if (runtime?.themeId !== {{themeIdJson}} ||
                  typeof runtime.setVisualSettings !== 'function') return false;
              runtime.setVisualSettings({{settingsJson}});
              return true;
            })()
            """;

        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var targets = await discovery.DiscoverAsync(port, cancellationToken);
            if (targets.Count == 0)
            {
                throw new InvalidOperationException($"本机端口 {port} 尚未发现 Codex 页面");
            }

            var applied = false;
            foreach (var target in targets)
            {
                await using var session = new CdpSession();
                await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
                var result = await session.EvaluateAsync(expression, cancellationToken);
                applied |= result.ValueKind == JsonValueKind.True;
            }

            if (!applied)
            {
                throw new InvalidOperationException("当前 Codex 页面尚未加载对应的 Tessalume 主题");
            }
        }
        finally
        {
            _applyLock.Release();
        }
    }

    public async Task RemoveAsync(int port, CancellationToken cancellationToken = default)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var targets = await discovery.DiscoverAsync(port, cancellationToken);
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

        StatusChanged?.Invoke(this, "已恢复 Codex 默认外观");
    }

    public async Task<bool> ReadColorSchemeAsync(int port, CancellationToken cancellationToken = default)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var targets = await discovery.DiscoverAsync(port, cancellationToken);
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

    public async Task<bool> ToggleColorSchemeAsync(int port, CancellationToken cancellationToken = default)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var targets = await discovery.DiscoverAsync(port, cancellationToken);
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
                  if (typeof bridge?.sendMessageFromView !== 'function' ||
                      typeof bridge?.getSystemThemeVariant !== 'function') {
                    throw new Error('当前 Codex 版本未提供原生外观设置接口');
                  }

                  const current = bridge.getSystemThemeVariant() === 'dark' ? 'dark' : 'light';
                  const next = current === 'dark' ? 'light' : 'dark';
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
                  while ((!document.documentElement.classList.contains(`electron-${next}`) ||
                          bridge.getSystemThemeVariant() !== next) && Date.now() < deadline) {
                    await new Promise(resolve => setTimeout(resolve, 50));
                  }
                  if (!document.documentElement.classList.contains(`electron-${next}`) ||
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
        await _applyLock.WaitAsync();
        _applyLock.Release();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        _applyLock.Dispose();
        discovery.Dispose();
    }

    private async Task ApplyToAllAsync(
        int port,
        ThemePackage package,
        string payload,
        string visualSettingsJson,
        bool force,
        HashSet<string>? processedTargets,
        CancellationToken cancellationToken)
        => await ApplyToAllAsync(
            port,
            package,
            payload,
            visualSettingsJson,
            force,
            processedTargets,
            skipKnownTargets: false,
            cancellationToken);

    private async Task ApplyToAllAsync(
        int port,
        ThemePackage package,
        string payload,
        string visualSettingsJson,
        bool force,
        HashSet<string>? processedTargets,
        bool skipKnownTargets,
        CancellationToken cancellationToken)
    {
        await _applyLock.WaitAsync(cancellationToken);
        IReadOnlyList<CdpTarget> targets = [];
        try
        {
            var themeId = package.Manifest.Id;
            targets = await discovery.DiscoverAsync(port, cancellationToken);
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

                await using var session = new CdpSession();
                await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);

                if (!package.Manifest.Compatibility.PetOverlay &&
                    target.Url.Contains("avatar-overlay", StringComparison.OrdinalIgnoreCase))
                {
                    await session.EvaluateAsync(
                        $"window.__TESSALUME_THEME_ID__ = {JsonSerializer.Serialize(themeId)}; true",
                        cancellationToken);
                    processedTargets?.Add(targetKey);
                    continue;
                }

                if (!force)
                {
                    var marker = await session.EvaluateAsync("window.__TESSALUME_THEME_ID__ || null", cancellationToken);
                    if (marker.ValueKind == JsonValueKind.String && marker.GetString() == themeId)
                    {
                        processedTargets?.Add(targetKey);
                        continue;
                    }
                }

                try
                {
                    await StageAssetsAsync(session, package.AssetPaths, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new ThemeRuntimeException(
                        ThemeRuntimeFailureStage.ResourcePreflightFailed,
                        "主题素材在发送到 Codex 前未能完整读取，当前页面已统一恢复默认外观。",
                        exception);
                }

                await session.EvaluateAsync(
                    $"window.__TESSALUME_STAGED_VISUAL_SETTINGS__ = {visualSettingsJson}; true",
                    cancellationToken);

                try
                {
                    await session.EvaluateAsync(payload, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    var themeScriptFailed = exception.Message.Contains(
                        ThemeScriptFailureMarker,
                        StringComparison.Ordinal);
                    throw new ThemeRuntimeException(
                        themeScriptFailed
                            ? ThemeRuntimeFailureStage.ThemeScriptFailed
                            : ThemeRuntimeFailureStage.RuntimeInjectionFailed,
                        themeScriptFailed
                            ? "主题脚本执行失败，所有 Codex 页面已统一恢复默认外观。"
                            : "主题运行时注入失败，所有 Codex 页面已统一恢复默认外观。",
                        exception);
                }
                processedTargets?.Add(targetKey);
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

    private async Task<string> BuildPayloadAsync(
        ThemePackage package,
        CancellationToken cancellationToken)
    {
        try
        {
            return await payloadBuilder.BuildRuntimeAsync(package, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ThemeRuntimeException(
                ThemeRuntimeFailureStage.ResourcePreflightFailed,
                "主题源码或素材未能完整读取，尚未更改 Codex 当前外观。",
                exception);
        }
    }

    private static async Task CleanupTargetsAsync(IReadOnlyList<CdpTarget> targets)
    {
        foreach (var target in targets)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await using var session = new CdpSession();
                await session.ConnectAsync(target.WebSocketDebuggerUrl, timeout.Token);
                _ = await session.EvaluateAsync(RemoveCompatibleRuntimesScript, timeout.Token);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or UriFormatException or
                    System.Net.WebSockets.WebSocketException or OperationCanceledException)
            {
                // Best effort: another Codex page may already have closed while rollback runs.
            }
        }
    }

    private static string GetTargetKey(CdpTarget target) =>
        string.IsNullOrWhiteSpace(target.Id) ? target.WebSocketDebuggerUrl : target.Id;

    private static string SerializeVisualSettings(ThemeVisualSettings? settings) =>
        JsonSerializer.Serialize((settings ?? new ThemeVisualSettings()).Normalize(), VisualSettingsJsonOptions);

    private static async Task StageAssetsAsync(
        CdpSession session,
        IReadOnlyDictionary<string, string> assetPaths,
        CancellationToken cancellationToken)
    {
        await session.EvaluateAsync(
            "window.__TESSALUME_STAGED_ASSETS__ = Object.create(null); true",
            cancellationToken);
        try
        {
            foreach (var (name, path) in assetPaths)
            {
                var dataUrl = await ThemePayloadBuilder.ReadDataUrlAsync(path, cancellationToken);
                var expression =
                    $"window.__TESSALUME_STAGED_ASSETS__['{name}'] = '{dataUrl}'; true";
                await session.EvaluateAsync(expression, cancellationToken);
            }
        }
        catch
        {
            try
            {
                await session.EvaluateAsync(
                    "delete window.__TESSALUME_STAGED_ASSETS__; true",
                    CancellationToken.None);
            }
            catch
            {
            }

            throw;
        }
    }
}
