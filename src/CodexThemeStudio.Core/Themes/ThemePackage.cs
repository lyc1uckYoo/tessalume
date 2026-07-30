namespace CodexThemeStudio.Core.Themes;

public sealed record ThemePackage(
    string RootDirectory,
    string ManifestPath,
    ThemeManifest Manifest,
    string? CssPath,
    string? ScriptPath,
    IReadOnlyDictionary<string, string> AssetPaths,
    string? PreviewLightPath,
    string? PreviewDarkPath)
{
    public bool IsAdvanced => ScriptPath is not null;
}

public sealed record ThemeLoadResult(ThemePackage? Package, ThemeValidationResult Validation);

public sealed record ThemeCatalogItem(
    string Directory,
    ThemePackage? Package,
    ThemeValidationResult Validation);
