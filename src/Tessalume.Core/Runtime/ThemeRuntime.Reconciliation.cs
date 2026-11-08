using System.Text.Json;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Runtime;

public sealed partial class ThemeRuntime
{
    private async Task ReconcileTargetsLoopAsync(
        int port,
        ThemePackage package,
        string payload,
        VisualSettingsPayload visualSettings,
        HashSet<string> processedTargets,
        CancellationToken cancellationToken)
    {
        var iteration = 0;
        try
        {
            while (true)
            {
                await Task.Delay(TargetReconciliationInterval, cancellationToken);
                iteration++;
                await ReconcileTargetsAsync(
                    port,
                    package,
                    payload,
                    visualSettings,
                    processedTargets,
                    verifyKnownTargets: iteration % KnownTargetVerificationInterval == 0,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ReconcileTargetsAsync(
        int port,
        ThemePackage package,
        string payload,
        VisualSettingsPayload visualSettings,
        HashSet<string> processedTargets,
        bool verifyKnownTargets,
        CancellationToken cancellationToken)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var targets = await _discovery.DiscoverAsync(port, cancellationToken);
            var currentTargetKeys = targets.Select(GetTargetKey).ToHashSet(StringComparer.Ordinal);
            processedTargets.IntersectWith(currentTargetKeys);

            foreach (var target in targets)
            {
                var targetKey = GetTargetKey(target);
                if (!verifyKnownTargets && processedTargets.Contains(targetKey))
                {
                    continue;
                }

                try
                {
                    await ApplyTargetAsync(
                        target,
                        package,
                        payload,
                        visualSettings,
                        force: false,
                        processedTargets,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is ThemeRuntimeException or InvalidOperationException or UriFormatException or
                        System.Net.WebSockets.WebSocketException or IOException)
                {
                    // Codex replaces renderer targets during startup and navigation. A target can
                    // disappear between discovery and connection; keep the working pages intact and
                    // retry this target on the next reconciliation pass.
                    processedTargets.Remove(targetKey);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Discovery is best effort after the initial successful application. Never roll back
            // healthy pages because Codex is temporarily replacing or restarting its renderer.
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private async Task StopWatcherCoreAsync()
    {
        var cancellation = _watcherCancellation;
        var task = _watcherTask;
        _watcherCancellation = null;
        _watcherTask = null;
        if (cancellation is null) return;

        cancellation.Cancel();
        try
        {
            if (task is not null) await task;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task ApplyTargetAsync(
        CdpTarget target,
        ThemePackage package,
        string payload,
        VisualSettingsPayload visualSettings,
        bool force,
        HashSet<string>? processedTargets,
        CancellationToken cancellationToken)
    {
        var themeId = package.Manifest.Id;
        var targetKey = GetTargetKey(target);
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);

        if (!package.Manifest.Compatibility.PetOverlay &&
            target.Url.Contains("avatar-overlay", StringComparison.OrdinalIgnoreCase))
        {
            await session.EvaluateAsync(
                $"window.__TESSALUME_THEME_ID__ = {JsonSerializer.Serialize(themeId)}; true",
                cancellationToken);
            processedTargets?.Add(targetKey);
            return;
        }

        if (!force)
        {
            var marker = await session.EvaluateAsync(
                "window.__TESSALUME_RUNTIME__?.themeId === window.__TESSALUME_THEME_ID__ && " +
                "document.documentElement.classList.contains('tessalume-theme-active') " +
                "? window.__TESSALUME_THEME_ID__ : null",
                cancellationToken);
            if (marker.ValueKind == JsonValueKind.String && marker.GetString() == themeId)
            {
                processedTargets?.Add(targetKey);
                return;
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

        await StageVisualSettingsAsync(session, visualSettings, cancellationToken);

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
        finally
        {
            await ClearStagedVisualSettingsAsync(session);
        }
        processedTargets?.Add(targetKey);
    }
}
