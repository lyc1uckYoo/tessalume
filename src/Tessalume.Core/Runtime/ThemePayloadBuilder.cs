using System.Text.Json;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Runtime;

public sealed record ThemeRuntimeAssets(
    string RuntimePath,
    string SharedTemplateStylePath,
    string CompatibilityProfilePath);

public sealed class ThemePayloadBuilder
{
    public const string OpenRuntimeAdapterKey = "$runtime-v2";
    public const string SharedTemplateStyleFileName = "theme-template-v1.css";
    public const string CompatibilityProfileFileName = "compatibility-profile-v3.json";

    private readonly Func<ThemeRuntimeAssets> _runtimeAssetsProvider;

    public ThemePayloadBuilder(IReadOnlyDictionary<string, string> runtimeAdapters)
        : this(() => ResolveLegacyAssets(runtimeAdapters))
    {
    }

    public ThemePayloadBuilder(Func<ThemeRuntimeAssets> runtimeAssetsProvider)
    {
        ArgumentNullException.ThrowIfNull(runtimeAssetsProvider);
        _runtimeAssetsProvider = runtimeAssetsProvider;
    }

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
        var runtimeAssets = _runtimeAssetsProvider();
        var runtime = await File.ReadAllTextAsync(runtimeAssets.RuntimePath, cancellationToken);
        var templateStylePath = runtimeAssets.SharedTemplateStylePath;
        if (package.Manifest.UsesSharedTemplateV1 && !File.Exists(templateStylePath))
        {
            throw new InvalidOperationException("The shared Template 1.0 stylesheet is not installed.");
        }
        var compatibilityProfile = await ReadCompatibilityProfileAsync(
            runtimeAssets.CompatibilityProfilePath,
            cancellationToken);
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
            .Replace("__TESSALUME_PAYLOAD_THEME_ID_JSON__", JsonSerializer.Serialize(package.Manifest.Id), StringComparison.Ordinal)
            .Replace("__TESSALUME_PAYLOAD_COMPATIBILITY_PROFILE_JSON__", compatibilityProfile, StringComparison.Ordinal)
            .Replace("__TESSALUME_PAYLOAD_TEMPLATE_CSS_JSON__", JsonSerializer.Serialize(templateCss), StringComparison.Ordinal)
            .Replace("__TESSALUME_PAYLOAD_CSS_JSON__", JsonSerializer.Serialize(css), StringComparison.Ordinal)
            .Replace(
                "__TESSALUME_PAYLOAD_HAS_SCRIPT__",
                string.IsNullOrWhiteSpace(script) ? "false" : "true",
                StringComparison.Ordinal)
            .Replace(
                "__TESSALUME_PAYLOAD_SCRIPT_BODY__",
                script,
                StringComparison.Ordinal)
            .Replace("__TESSALUME_PAYLOAD_ASSETS_JSON__", JsonSerializer.Serialize(assets), StringComparison.Ordinal)
            .Replace("__TESSALUME_PAYLOAD_CONFIG_JSON__", JsonSerializer.Serialize(package.Manifest.Config), StringComparison.Ordinal)
            .Replace("__TESSALUME_PAYLOAD_ALLOW_PET_OVERLAY__", package.Manifest.Compatibility.PetOverlay ? "true" : "false", StringComparison.Ordinal)
            .Replace("__TESSALUME_PAYLOAD_FINGERPRINT_JSON__", JsonSerializer.Serialize(fingerprint), StringComparison.Ordinal);
        EnsureComplete(payload);
        return payload;
    }

    private static ThemeRuntimeAssets ResolveLegacyAssets(
        IReadOnlyDictionary<string, string> runtimeAdapters)
    {
        if (!runtimeAdapters.TryGetValue(OpenRuntimeAdapterKey, out var runtimePath))
        {
            throw new InvalidOperationException("The schema v3 theme runtime is not installed.");
        }

        var directory = Path.GetDirectoryName(runtimePath)
            ?? throw new InvalidOperationException("The schema v3 runtime path has no parent directory.");
        return new ThemeRuntimeAssets(
            runtimePath,
            Path.Combine(directory, SharedTemplateStyleFileName),
            Path.Combine(directory, CompatibilityProfileFileName));
    }

    private static async Task<string> ReadCompatibilityProfileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The versioned compatibility profile is not installed.");
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var schemaVersion) ||
            schemaVersion.GetInt32() != 1 ||
            !root.TryGetProperty("profileVersion", out var profileVersion) ||
            profileVersion.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(profileVersion.GetString()) ||
            !root.TryGetProperty("runtimeContractVersion", out var contractVersion) ||
            contractVersion.GetInt32() != ThemeRuntime.ContractVersion ||
            !root.TryGetProperty("selectors", out var selectors) ||
            selectors.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The compatibility profile does not match the current runtime contract.");
        }

        return root.GetRawText();
    }

    private static void EnsureComplete(string payload)
    {
        if (payload.Contains("__DREAM_", StringComparison.Ordinal) ||
            payload.Contains("__TESSALUME_PAYLOAD_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The theme runtime payload contains unresolved placeholders.");
        }
    }

    internal static async Task<string> ReadDataUrlAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return CreateDataUrl(path, bytes);
    }

    internal static string CreateDataUrl(string path, byte[] bytes)
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
        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }
}
