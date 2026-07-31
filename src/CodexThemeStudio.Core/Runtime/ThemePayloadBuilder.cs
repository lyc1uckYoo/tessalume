using System.Text.Json;
using CodexThemeStudio.Core.Themes;

namespace CodexThemeStudio.Core.Runtime;

public sealed class ThemePayloadBuilder(IReadOnlyDictionary<string, string> runtimeAdapters)
{
    public const string OpenRuntimeAdapterKey = "$runtime-v2";
    public const string SharedTemplateStyleFileName = "theme-template-v1.css";

    public async Task<string> BuildAsync(ThemePackage package, CancellationToken cancellationToken = default) =>
        await BuildThemeAsync(package, embedAssets: true, cancellationToken);

    public async Task<string> BuildRuntimeAsync(
        ThemePackage package,
        CancellationToken cancellationToken = default) =>
        await BuildThemeAsync(package, embedAssets: false, cancellationToken);

    private async Task<string> BuildThemeAsync(
        ThemePackage package,
        bool embedAssets,
        CancellationToken cancellationToken)
    {
        if (!runtimeAdapters.TryGetValue(OpenRuntimeAdapterKey, out var runtimePath))
        {
            throw new InvalidOperationException("The schema v2 theme runtime is not installed.");
        }

        var runtime = await File.ReadAllTextAsync(runtimePath, cancellationToken);
        var templateStylePath = Path.Combine(
            Path.GetDirectoryName(runtimePath)
                ?? throw new InvalidOperationException("The schema v2 runtime path has no parent directory."),
            SharedTemplateStyleFileName);
        if (package.Manifest.UsesSharedTemplateV1 && !File.Exists(templateStylePath))
        {
            throw new InvalidOperationException("The shared Template 1.0 stylesheet is not installed.");
        }
        var templateCss = package.Manifest.UsesSharedTemplateV1
            ? await File.ReadAllTextAsync(templateStylePath, cancellationToken)
            : string.Empty;
        var css = package.CssPath is null
            ? string.Empty
            : await File.ReadAllTextAsync(package.CssPath, cancellationToken);
        var script = package.ScriptPath is null
            ? string.Empty
            : await File.ReadAllTextAsync(package.ScriptPath, cancellationToken);
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (embedAssets)
        {
            foreach (var (name, path) in package.AssetPaths)
            {
                assets[name] = await ReadDataUrlAsync(path, cancellationToken);
            }
        }

        var fingerprint = package.Manifest.UsesSharedTemplateV1
            ? await ThemeFingerprintCalculator.CalculateEffectiveAsync(
                package,
                templateStylePath,
                cancellationToken)
            : await ThemeFingerprintCalculator.CalculateAsync(package, cancellationToken);
        var payload = runtime
            .Replace("__CTS_THEME_ID_JSON__", JsonSerializer.Serialize(package.Manifest.Id), StringComparison.Ordinal)
            .Replace("__CTS_TEMPLATE_CSS_JSON__", JsonSerializer.Serialize(templateCss), StringComparison.Ordinal)
            .Replace("__CTS_CSS_JSON__", JsonSerializer.Serialize(css), StringComparison.Ordinal)
            .Replace("__CTS_SCRIPT_JSON__", JsonSerializer.Serialize(script), StringComparison.Ordinal)
            .Replace("__CTS_ASSETS_JSON__", JsonSerializer.Serialize(assets), StringComparison.Ordinal)
            .Replace("__CTS_CONFIG_JSON__", JsonSerializer.Serialize(package.Manifest.Config), StringComparison.Ordinal)
            .Replace("__CTS_ALLOW_PET_OVERLAY__", package.Manifest.Compatibility.PetOverlay ? "true" : "false", StringComparison.Ordinal)
            .Replace("__CTS_FINGERPRINT_JSON__", JsonSerializer.Serialize(fingerprint), StringComparison.Ordinal);
        EnsureComplete(payload);
        return payload;
    }

    private static void EnsureComplete(string payload)
    {
        if (payload.Contains("__DREAM_", StringComparison.Ordinal) ||
            payload.Contains("__CTS_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The theme runtime payload contains unresolved placeholders.");
        }
    }

    internal static async Task<string> ReadDataUrlAsync(string path, CancellationToken cancellationToken)
    {
        var mimeType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".json" => "application/json",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".txt" or ".md" => "text/plain",
            _ => "image/jpeg",
        };
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }
}
