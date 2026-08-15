using System.Text.Json;

namespace Tessalume.Core.Runtime;

public sealed partial class ThemeRuntime
{
    public async Task ApplyVisualSettingsAsync(
        int port,
        string themeId,
        ThemeVisualSettings visualSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        var themeIdJson = JsonSerializer.Serialize(themeId);
        var visualPayload = await BuildVisualSettingsPayloadAsync(visualSettings, cancellationToken);

        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var targets = await _discovery.DiscoverAsync(port, cancellationToken);
            if (targets.Count == 0)
            {
                throw new InvalidOperationException($"本机端口 {port} 尚未发现 Codex 页面");
            }

            var applied = false;
            foreach (var target in targets)
            {
                await using var session = new CdpSession();
                await session.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
                var cachedKeysResult = await session.EvaluateAsync($$"""
                    (() => {
                      const runtime = window.__TESSALUME_RUNTIME__;
                      if (runtime?.themeId !== {{themeIdJson}} ||
                          runtime.visualImageProtocolVersion !== 1 ||
                          typeof runtime.setVisualSettings !== 'function' ||
                          typeof runtime.getVisualImageKeys !== 'function') return null;
                      return runtime.getVisualImageKeys();
                    })()
                    """, cancellationToken);
                if (cachedKeysResult.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var cachedKeys = cachedKeysResult
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .ToHashSet(StringComparer.Ordinal);
                var missingImagePaths = visualPayload.ImagePaths
                    .Where(pair => !cachedKeys.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                var stagedImages = false;
                try
                {
                    if (missingImagePaths.Count > 0)
                    {
                        await session.EvaluateAsync(
                            "window.__TESSALUME_STAGED_VISUAL_IMAGES__ = Object.create(null); true",
                            cancellationToken);
                        stagedImages = true;
                        foreach (var (key, path) in missingImagePaths)
                        {
                            var image = await _visualImageDataUrlCache.GetPayloadAsync(path, cancellationToken);
                            if (!string.Equals(image.Key, key, StringComparison.Ordinal))
                            {
                                throw new IOException("本地图像在实时应用期间发生变化，请重试。");
                            }
                            await session.EvaluateAsync(
                                $"window.__TESSALUME_STAGED_VISUAL_IMAGES__[{JsonSerializer.Serialize(key)}] = " +
                                $"{JsonSerializer.Serialize(image.DataUrl)}; true",
                                cancellationToken);
                        }
                    }

                    var result = await session.EvaluateAsync($$"""
                        (async () => {
                          const runtime = window.__TESSALUME_RUNTIME__;
                          const stagedImages = window.__TESSALUME_STAGED_VISUAL_IMAGES__;
                          try {
                            if (runtime?.themeId !== {{themeIdJson}} ||
                                runtime.visualImageProtocolVersion !== 1 ||
                                typeof runtime.setVisualSettings !== 'function') return false;
                            return await runtime.setVisualSettings(
                              {{visualPayload.SettingsJson}},
                              stagedImages || Object.create(null));
                          } finally {
                            delete window.__TESSALUME_STAGED_VISUAL_IMAGES__;
                          }
                        })()
                        """, cancellationToken);
                    stagedImages = false;
                    applied |= result.ValueKind == JsonValueKind.True;
                }
                finally
                {
                    if (stagedImages) await ClearStagedVisualImagesAsync(session);
                }
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
}
