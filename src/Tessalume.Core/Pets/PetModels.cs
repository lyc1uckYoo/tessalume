using System.Text.Json.Serialization;

namespace Tessalume.Core.Pets;

public static class PetPackageContract
{
    public const int CatalogSchemaVersion = 1;
    public const string CatalogFileName = "catalog.json";
    public const string ManifestFileName = "pet.json";
    public const string ManifestRole = "codex-manifest";
    public const string SpritesheetRole = "codex-spritesheet";
    public const string PreviewRole = "preview";

    public const int SpriteVersionNumber = 2;
    public const int AtlasWidth = 1536;
    public const int AtlasHeight = 2288;
    public const int Columns = 8;
    public const int Rows = 11;
    public const int CellWidth = 192;
    public const int CellHeight = 208;
    public const int UsedFrameCount = 74;

    public static IReadOnlyList<PetProtocolState> RequiredStates { get; } =
    [
        new("idle", 0, 7),
        new("move-right", 1, 8),
        new("move-left", 2, 8),
        new("wave-touch", 3, 4),
        new("jump", 4, 5),
        new("blocked", 5, 8),
        new("needs-input", 6, 6),
        new("running", 7, 6),
        new("ready", 8, 6),
        new("gaze-upper", 9, 8),
        new("gaze-lower", 10, 8),
    ];
}

public sealed record PetManifest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("spriteVersionNumber")]
    public int SpriteVersionNumber { get; init; }

    [JsonPropertyName("spritesheetPath")]
    public string SpritesheetPath { get; init; } = string.Empty;
}

public sealed record PetCatalog
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = PetPackageContract.CatalogSchemaVersion;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("productVersion")]
    public string ProductVersion { get; init; } = string.Empty;

    [JsonPropertyName("protocol")]
    public PetProtocolMetadata Protocol { get; init; } = new();

    [JsonPropertyName("author")]
    public PetAuthorMetadata Author { get; init; } = new();

    [JsonPropertyName("license")]
    public PetLicenseMetadata License { get; init; } = new();

    [JsonPropertyName("rights")]
    public PetRightsMetadata Rights { get; init; } = new();

    [JsonPropertyName("files")]
    public IReadOnlyList<PetCatalogFile> Files { get; init; } = [];

    [JsonPropertyName("previews")]
    public IReadOnlyList<PetPreviewMetadata> Previews { get; init; } = [];

    [JsonPropertyName("recommendedThemeIds")]
    public IReadOnlyList<string> RecommendedThemeIds { get; init; } = [];
}

public sealed record PetProtocolMetadata
{
    [JsonPropertyName("spriteVersionNumber")]
    public int SpriteVersionNumber { get; init; }

    [JsonPropertyName("atlasWidth")]
    public int AtlasWidth { get; init; }

    [JsonPropertyName("atlasHeight")]
    public int AtlasHeight { get; init; }

    [JsonPropertyName("columns")]
    public int Columns { get; init; }

    [JsonPropertyName("rows")]
    public int Rows { get; init; }

    [JsonPropertyName("cellWidth")]
    public int CellWidth { get; init; }

    [JsonPropertyName("cellHeight")]
    public int CellHeight { get; init; }

    [JsonPropertyName("usedFrameCount")]
    public int UsedFrameCount { get; init; }

    [JsonPropertyName("states")]
    public IReadOnlyList<PetProtocolState> States { get; init; } = [];
}

public sealed record PetProtocolState(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("row")] int Row,
    [property: JsonPropertyName("frames")] int Frames);

public sealed record PetAuthorMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

public sealed record PetLicenseMetadata
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("spdx")]
    public string? Spdx { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

public sealed record PetRightsMetadata
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("notice")]
    public string Notice { get; init; } = string.Empty;
}

public sealed record PetCatalogFile
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;
}

public sealed record PetPreviewMetadata
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("stateKey")]
    public string? StateKey { get; init; }
}

public sealed record PetWebPInfo(int Width, int Height, bool HasAlpha, string Encoding);

public sealed record PetPackage(
    string RootDirectory,
    string CatalogPath,
    string ManifestPath,
    PetCatalog Catalog,
    PetManifest Manifest,
    IReadOnlyDictionary<string, string> ResolvedFiles,
    PetWebPInfo SpritesheetInfo)
{
    public IEnumerable<PetCatalogFile> InstallFiles =>
        Catalog.Files.Where(file =>
            string.Equals(file.Role, PetPackageContract.ManifestRole, StringComparison.Ordinal) ||
            string.Equals(file.Role, PetPackageContract.SpritesheetRole, StringComparison.Ordinal));

    public IEnumerable<(PetPreviewMetadata Metadata, string FullPath)> PreviewFiles =>
        Catalog.Previews.Select(preview => (preview, ResolvedFiles[preview.Path]));
}

public sealed record PetLoadResult(PetPackage? Package, PetValidationResult Validation);

public sealed record PetPackageCandidate(
    string DirectoryPath,
    PetPackage? Package,
    PetValidationResult Validation);
