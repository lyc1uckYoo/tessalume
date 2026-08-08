using System.Text.Json;
using System.Text.Json.Nodes;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Runtime;

public sealed partial class ThemeRuntime
{
    private async Task<string> BuildPayloadAsync(
        ThemePackage package,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _payloadBuilder.BuildRuntimeAsync(package, cancellationToken);
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

    private async Task<string> SerializeVisualSettingsAsync(
        ThemeVisualSettings? settings,
        CancellationToken cancellationToken)
    {
        var normalized = (settings ?? new ThemeVisualSettings()).Normalize();
        if (_visualSettingsResolver is not null)
        {
            normalized = _visualSettingsResolver(normalized).Normalize();
        }

        var root = JsonSerializer.SerializeToNode(normalized, VisualSettingsJsonOptions)?.AsObject()
            ?? throw new InvalidOperationException("无法序列化个性化视觉参数。");
        foreach (var modeName in new[] { "light", "dark" })
        {
            if (root[modeName] is not JsonObject mode) continue;
            foreach (var regionName in new[] { "hero", "sidebar", "chat" })
            {
                if (mode[regionName] is not JsonObject adjustment) continue;
                var path = adjustment["customImagePath"]?.GetValue<string?>();
                adjustment.Remove("customImagePath");
                if (string.IsNullOrWhiteSpace(path)) continue;
                adjustment["customImageDataUrl"] = await ThemePayloadBuilder.ReadDataUrlAsync(
                    path,
                    cancellationToken);
            }
        }
        return root.ToJsonString(VisualSettingsJsonOptions);
    }

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
