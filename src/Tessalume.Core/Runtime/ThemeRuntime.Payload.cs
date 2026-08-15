using System.Text.Json;
using System.Text.Json.Nodes;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Runtime;

public sealed partial class ThemeRuntime
{
    internal sealed record VisualSettingsPayload(
        string SettingsJson,
        IReadOnlyDictionary<string, string> ImagePaths);

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

    internal Task<VisualSettingsPayload> BuildVisualSettingsPayloadAsync(
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
        var imagePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var modeName in new[] { "light", "dark" })
        {
            if (root[modeName] is not JsonObject mode) continue;
            foreach (var regionName in new[] { "hero", "sidebar", "chat" })
            {
                if (mode[regionName] is not JsonObject adjustment) continue;
                var path = adjustment["customImagePath"]?.GetValue<string?>();
                adjustment.Remove("customImagePath");
                if (string.IsNullOrWhiteSpace(path)) continue;
                cancellationToken.ThrowIfCancellationRequested();
                var imageKey = ImageDataUrlCache.GetFingerprint(path);
                adjustment["customImageKey"] = imageKey;
                imagePaths.TryAdd(imageKey, path);
            }
        }
        return Task.FromResult(new VisualSettingsPayload(
            root.ToJsonString(VisualSettingsJsonOptions),
            imagePaths));
    }

    private async Task StageVisualSettingsAsync(
        CdpSession session,
        VisualSettingsPayload visualSettings,
        CancellationToken cancellationToken)
    {
        await session.EvaluateAsync(
            $"window.__TESSALUME_STAGED_VISUAL_SETTINGS__ = {visualSettings.SettingsJson}; " +
            "window.__TESSALUME_STAGED_VISUAL_IMAGES__ = Object.create(null); true",
            cancellationToken);
        try
        {
            foreach (var (key, path) in visualSettings.ImagePaths)
            {
                var image = await _visualImageDataUrlCache.GetPayloadAsync(path, cancellationToken);
                if (!string.Equals(image.Key, key, StringComparison.Ordinal))
                {
                    throw new IOException("本地图像在主题应用期间发生变化，请重试。");
                }
                await session.EvaluateAsync(
                    $"window.__TESSALUME_STAGED_VISUAL_IMAGES__[{JsonSerializer.Serialize(key)}] = " +
                    $"{JsonSerializer.Serialize(image.DataUrl)}; true",
                    cancellationToken);
            }
        }
        catch
        {
            await ClearStagedVisualSettingsAsync(session);
            throw;
        }
    }

    private static async Task ClearStagedVisualSettingsAsync(CdpSession session)
    {
        try
        {
            await session.EvaluateAsync(
                "delete window.__TESSALUME_STAGED_VISUAL_SETTINGS__; " +
                "delete window.__TESSALUME_STAGED_VISUAL_IMAGES__; true",
                CancellationToken.None);
        }
        catch
        {
        }
    }

    private static async Task ClearStagedVisualImagesAsync(CdpSession session)
    {
        try
        {
            await session.EvaluateAsync(
                "delete window.__TESSALUME_STAGED_VISUAL_IMAGES__; true",
                CancellationToken.None);
        }
        catch
        {
        }
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
