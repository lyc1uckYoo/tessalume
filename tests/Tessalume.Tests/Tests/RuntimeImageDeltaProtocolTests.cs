using System.Text.Json;
using Tessalume.Core.Runtime;

internal static partial class TestSuite
{
    private static readonly string[] RuntimeVisualSettingsSourceFiles =
    [
        "ThemeRuntime.cs",
        "ThemeRuntime.Payload.cs",
        "ThemeRuntime.VisualSettings.cs",
    ];

    static async Task RuntimeArtworkImagesUseFingerprintDeltasAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-runtime-artwork-delta-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var imagePath = Path.Combine(root, "shared.png");
            await File.WriteAllBytesAsync(imagePath, Enumerable.Repeat((byte)0x31, 128).ToArray());
            await using var runtime = new ThemeRuntime(
                new LoopbackCdpDiscovery(),
                new ThemePayloadBuilder(new Dictionary<string, string>
                {
                    [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
                }));

            var settings = new ThemeVisualSettings
            {
                Light = new ThemeVisualModeSettings
                {
                    Hero = new ThemeArtworkAdjustment { CustomImagePath = imagePath, Brightness = 91 },
                    Sidebar = new ThemeArtworkAdjustment { CustomImagePath = imagePath, Contrast = 112 },
                },
            };
            var first = await runtime.BuildVisualSettingsPayloadAsync(settings, CancellationToken.None);
            using var firstDocument = JsonDocument.Parse(first.SettingsJson);
            var firstHero = firstDocument.RootElement.GetProperty("light").GetProperty("hero");
            var firstSidebar = firstDocument.RootElement.GetProperty("light").GetProperty("sidebar");
            var firstKey = firstHero.GetProperty("customImageKey").GetString();
            Ensure(!string.IsNullOrWhiteSpace(firstKey) &&
                   firstKey == firstSidebar.GetProperty("customImageKey").GetString() &&
                   first.ImagePaths.Count == 1 &&
                   first.ImagePaths.ContainsKey(firstKey),
                "Two slots using the same local file must share one fingerprint and one lazy image source.");
            Ensure(!first.SettingsJson.Contains("customImagePath", StringComparison.Ordinal) &&
                   !first.SettingsJson.Contains("customImageDataUrl", StringComparison.Ordinal) &&
                   first.SettingsJson.Length < 16 * 1024,
                "The hot visual-settings JSON must contain neither machine paths nor embedded image bytes.");

            var parameterOnly = settings with
            {
                Light = settings.Light with
                {
                    Hero = settings.Light.Hero with { Brightness = 127 },
                },
            };
            var second = await runtime.BuildVisualSettingsPayloadAsync(parameterOnly, CancellationToken.None);
            using var secondDocument = JsonDocument.Parse(second.SettingsJson);
            Ensure(secondDocument.RootElement.GetProperty("light").GetProperty("hero")
                       .GetProperty("customImageKey").GetString() == firstKey &&
                   second.ImagePaths.Keys.Single() == firstKey,
                "A parameter-only change must retain the stable image fingerprint for renderer-side reuse.");

            var previousWriteTime = File.GetLastWriteTimeUtc(imagePath);
            await File.WriteAllBytesAsync(imagePath, Enumerable.Repeat((byte)0x52, 128).ToArray());
            File.SetLastWriteTimeUtc(imagePath, previousWriteTime.AddSeconds(2));
            var changed = await runtime.BuildVisualSettingsPayloadAsync(parameterOnly, CancellationToken.None);
            Ensure(changed.ImagePaths.Keys.Single() != firstKey,
                "Changing local image content must produce a new fingerprint and a corresponding image delta.");

            var restored = await runtime.BuildVisualSettingsPayloadAsync(
                new ThemeVisualSettings(),
                CancellationToken.None);
            Ensure(restored.ImagePaths.Count == 0 &&
                   !restored.SettingsJson.Contains("customImageKey", StringComparison.Ordinal),
                "Returning to theme artwork must explicitly omit custom keys and carry no image payload.");

            var runtimeDirectory = Path.Combine(
                repositoryRoot, "src", "Tessalume.Core", "Runtime");
            var runtimeCoreSource = string.Join(
                '\n',
                await Task.WhenAll(RuntimeVisualSettingsSourceFiles.Select(
                    file => File.ReadAllTextAsync(Path.Combine(runtimeDirectory, file)))));
            var compatibilitySource = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
            Ensure(runtimeCoreSource.Contains("runtime.getVisualImageKeys()", StringComparison.Ordinal) &&
                   runtimeCoreSource.Contains("missingImagePaths", StringComparison.Ordinal) &&
                   runtimeCoreSource.Contains("await ClearStagedVisualSettingsAsync(session);", StringComparison.Ordinal) &&
                   runtimeCoreSource.Contains("delete window.__TESSALUME_STAGED_VISUAL_IMAGES__;", StringComparison.Ordinal) &&
                   !runtimeCoreSource.Contains("_syncedVisualImage", StringComparison.Ordinal),
                "Every target must report its committed image keys before Core chooses deltas; failed sends must neither update an optimistic sync map nor retain full-start staging.");
            Ensure(compatibilitySource.Contains("visualImageProtocolVersion: 1", StringComparison.Ordinal) &&
                   compatibilitySource.Contains("preparedImageUrls", StringComparison.Ordinal) &&
                   compatibilitySource.Contains("activeImageKeys", StringComparison.Ordinal) &&
                   compatibilitySource.Contains("URL.revokeObjectURL(objectUrl)", StringComparison.Ordinal) &&
                   compatibilitySource.Contains("delete window.__TESSALUME_STAGED_VISUAL_SETTINGS__", StringComparison.Ordinal) &&
                   compatibilitySource.Contains("delete window.__TESSALUME_STAGED_VISUAL_IMAGES__", StringComparison.Ordinal) &&
                   !compatibilitySource.Contains("adjustment.customImageDataUrl", StringComparison.Ordinal),
                "The renderer must preflight deltas, reuse committed fingerprints, revoke withdrawn images, and reject the legacy per-update Data URL field.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
