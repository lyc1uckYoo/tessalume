using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tessalume.Core.Themes;

public sealed partial class ThemePackageLoader
{
    public const string ManifestFileName = "manifest.json";
    public const int LatestSchemaVersion = 2;
    public const int SupportedEngineVersion = 2;

    private const long MaximumManifestBytes = 256 * 1024;
    private const long MaximumCssBytes = 2 * 1024 * 1024;
    private const long MaximumScriptBytes = 2 * 1024 * 1024;
    private const long MaximumAssetBytes = 25 * 1024 * 1024;
    private const long MaximumPackageAssetBytes = 100 * 1024 * 1024;

    private static readonly HashSet<string> RasterExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif", ".bmp", ".ico",
    };

    private static readonly HashSet<string> OpenThemeAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif", ".bmp", ".ico", ".svg",
        ".woff", ".woff2", ".ttf", ".otf",
        ".json", ".txt", ".md",
        ".mp3", ".wav", ".ogg", ".m4a",
        ".mp4", ".webm",
    };

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public async Task<ThemeLoadResult> LoadAsync(string themeDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDirectory);

        var validation = new ThemeValidationResult();
        var root = Path.GetFullPath(themeDirectory);
        if (!Directory.Exists(root))
        {
            validation.AddError("package.directory.missing", "Theme directory does not exist.", root);
            return new ThemeLoadResult(null, validation);
        }

        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            validation.AddError("manifest.missing", $"{ManifestFileName} is required.", manifestPath);
            return new ThemeLoadResult(null, validation);
        }

        if (new FileInfo(manifestPath).Length > MaximumManifestBytes)
        {
            validation.AddError("manifest.too-large", "Theme manifest exceeds 256 KiB.", manifestPath);
            return new ThemeLoadResult(null, validation);
        }

        ThemeManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<ThemeManifest>(stream, _jsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            validation.AddError("manifest.invalid-json", exception.Message, manifestPath);
            return new ThemeLoadResult(null, validation);
        }

        if (manifest is null)
        {
            validation.AddError("manifest.empty", "Theme manifest is empty.", manifestPath);
            return new ThemeLoadResult(null, validation);
        }

        manifest = NormalizeManifestSections(manifest, validation);
        ValidateManifest(manifest, validation);

        string? cssPath = null;
        if (!string.IsNullOrWhiteSpace(manifest.EntryPoints.Css))
        {
            cssPath = ResolveContainedFile(root, manifest.EntryPoints.Css, "entryPoints.css", ".css", validation);
            if (cssPath is not null)
            {
                await ValidateTextFileAsync(cssPath, MaximumCssBytes, "css.too-large", "Theme CSS exceeds 2 MiB.", validation, cancellationToken);
                await ValidateCssAsync(cssPath, validation, cancellationToken);
            }
        }
        string? scriptPath = null;
        if (!string.IsNullOrWhiteSpace(manifest.EntryPoints.Script))
        {
            scriptPath = ResolveContainedFile(root, manifest.EntryPoints.Script, "entryPoints.script", ".js", validation);
            if (scriptPath is not null)
            {
                await ValidateTextFileAsync(
                    scriptPath,
                    MaximumScriptBytes,
                    "script.too-large",
                    "Theme script exceeds 2 MiB.",
                    validation,
                    cancellationToken);
            }
        }
        else
        {
            validation.AddError("entry.script.missing", "Themes require entryPoints.script.");
        }

        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long totalAssetBytes = 0;
        foreach (var (name, relativePath) in manifest.Assets)
        {
            if (!AssetNameRegex().IsMatch(name))
            {
                validation.AddError(
                    "asset.name.invalid",
                    "Asset names must use letters, numbers, dots, underscores, or hyphens.",
                    name);
                continue;
            }

            var assetPath = ResolveContainedFile(root, relativePath, $"assets.{name}", null, validation);
            if (assetPath is null)
            {
                continue;
            }

            var extension = Path.GetExtension(assetPath);
            if (!OpenThemeAssetExtensions.Contains(extension))
            {
                validation.AddError(
                    "asset.extension.unsupported",
                    "This asset type is not supported by the local theme runtime.",
                    relativePath);
                continue;
            }

            var size = new FileInfo(assetPath).Length;
            if (size > MaximumAssetBytes)
            {
                validation.AddError("asset.too-large", "A single theme asset cannot exceed 25 MiB.", relativePath);
                continue;
            }

            totalAssetBytes += size;
            assets[name] = assetPath;
        }

        if (totalAssetBytes > MaximumPackageAssetBytes)
        {
            validation.AddError("assets.total-too-large", "Theme assets cannot exceed 100 MiB in total.");
        }

        var previewLightPath = ValidatePreview(root, manifest.Previews.Light, "previews.light", validation);
        var previewDarkPath = ValidatePreview(root, manifest.Previews.Dark, "previews.dark", validation);

        if (!validation.IsValid)
        {
            return new ThemeLoadResult(null, validation);
        }

        return new ThemeLoadResult(
            new ThemePackage(
                root,
                manifestPath,
                manifest,
                cssPath,
                scriptPath,
                assets,
                previewLightPath,
                previewDarkPath),
            validation);
    }

    private static void ValidateManifest(ThemeManifest manifest, ThemeValidationResult validation)
    {
        if (manifest.SchemaVersion != LatestSchemaVersion)
        {
            validation.AddError(
                "manifest.schema.unsupported",
                $"Schema version {manifest.SchemaVersion} is not supported. This app requires version {LatestSchemaVersion}.");
        }

        if (!ThemeIdRegex().IsMatch(manifest.Id))
        {
            validation.AddError(
                "manifest.id.invalid",
                "Theme id must contain 3-64 lowercase letters, numbers, dots, or hyphens and start with a letter or number.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            validation.AddError("manifest.name.missing", "Theme name is required.");
        }

        if (!System.Version.TryParse(manifest.Version, out _))
        {
            validation.AddError("manifest.version.invalid", "Theme version must be numeric, such as 1.0.0.");
        }

        if (manifest.EngineVersion > SupportedEngineVersion)
        {
            validation.AddError(
                "manifest.engine.unsupported",
                $"Theme requires engine version {manifest.EngineVersion}, but this app supports {SupportedEngineVersion}.");
        }

        if (!manifest.Capabilities.Light && !manifest.Capabilities.Dark)
        {
            validation.AddError("manifest.capabilities.empty", "A theme must support light mode, dark mode, or both.");
        }

        if (!string.Equals(manifest.Type, "advanced", StringComparison.OrdinalIgnoreCase))
        {
            validation.AddError("manifest.type.invalid", "Only advanced themes are supported.");
        }

        if (manifest.Template is not null && !manifest.UsesSharedTemplateV1)
        {
            validation.AddError(
                "manifest.template.unsupported",
                "Shared themes must declare template id 'flagship', version '1.0', and style 'shared'.");
        }
    }

    private static ThemeManifest NormalizeManifestSections(
        ThemeManifest manifest,
        ThemeValidationResult validation)
    {
        if (manifest.Capabilities is null)
        {
            validation.AddError("manifest.capabilities.missing", "Theme capabilities must be an object.");
        }
        if (manifest.EntryPoints is null)
        {
            validation.AddError("manifest.entry-points.missing", "Theme entryPoints must be an object.");
        }
        if (manifest.Assets is null)
        {
            validation.AddError("manifest.assets.missing", "Theme assets must be an object.");
        }
        if (manifest.Previews is null)
        {
            validation.AddWarning("manifest.previews.missing", "Theme previews should be an object.");
        }

        return manifest with
        {
            Id = manifest.Id ?? string.Empty,
            Name = manifest.Name ?? string.Empty,
            Version = manifest.Version ?? string.Empty,
            Author = manifest.Author ?? string.Empty,
            Description = manifest.Description ?? string.Empty,
            Type = manifest.Type ?? string.Empty,
            Capabilities = manifest.Capabilities ?? new ThemeCapabilities { Light = false, Dark = false },
            EntryPoints = manifest.EntryPoints ?? new ThemeEntryPoints(),
            Previews = manifest.Previews ?? new ThemePreviews(),
            Assets = manifest.Assets ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Config = manifest.Config ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
            Compatibility = manifest.Compatibility ?? new ThemeCompatibility(),
        };
    }

    private static string? ValidatePreview(
        string root,
        string? relativePath,
        string field,
        ThemeValidationResult validation)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var previewPath = ResolveContainedFile(root, relativePath, field, null, validation);
        if (previewPath is not null && !RasterExtensions.Contains(Path.GetExtension(previewPath)))
        {
            validation.AddError("preview.extension.unsupported", "Theme previews must be raster images.", relativePath);
            return null;
        }

        return previewPath;
    }

    private static async Task ValidateTextFileAsync(
        string path,
        long maximumBytes,
        string errorCode,
        string errorMessage,
        ThemeValidationResult validation,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > maximumBytes)
        {
            validation.AddError(errorCode, errorMessage, path);
            return;
        }

        _ = await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static async Task ValidateCssAsync(
        string cssPath,
        ThemeValidationResult validation,
        CancellationToken cancellationToken)
    {
        var css = await File.ReadAllTextAsync(cssPath, cancellationToken);
        var forbiddenPatterns = new (string Code, string Pattern, string Message)[]
        {
            ("css.import.forbidden", @"@import\s", "CSS @import is not allowed."),
            ("css.remote-url.forbidden", """url\s*\(\s*['"]?\s*(?:https?:|file:|data:text/html)""", "Remote, file, and HTML data URLs are not allowed."),
            ("css.javascript.forbidden", @"javascript\s*:", "JavaScript URLs are not allowed."),
            ("css.behavior.forbidden", @"(?:behavior|-moz-binding)\s*:", "Executable CSS bindings are not allowed."),
        };

        foreach (var (code, pattern, message) in forbiddenPatterns)
        {
            if (Regex.IsMatch(css, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                validation.AddError(code, message, cssPath);
            }
        }
    }

    private static string? ResolveContainedFile(
        string root,
        string? relativePath,
        string field,
        string? requiredExtension,
        ThemeValidationResult validation)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            validation.AddError("path.missing", $"{field} is required.");
            return null;
        }

        if (Path.IsPathRooted(relativePath))
        {
            validation.AddError("path.rooted", $"{field} must be relative to the theme directory.", relativePath);
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var relative = Path.GetRelativePath(root, candidate);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            validation.AddError("path.outside-package", $"{field} escapes the theme directory.", relativePath);
            return null;
        }

        if (!File.Exists(candidate))
        {
            validation.AddError("path.file.missing", $"{field} does not exist.", relativePath);
            return null;
        }

        if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
        {
            validation.AddError("path.reparse-point", $"{field} cannot be a symbolic link or reparse point.", relativePath);
            return null;
        }

        if (requiredExtension is not null &&
            !Path.GetExtension(candidate).Equals(requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            validation.AddError("path.extension.invalid", $"{field} must be a {requiredExtension} file.", relativePath);
            return null;
        }

        return candidate;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeIdRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AssetNameRegex();
}
