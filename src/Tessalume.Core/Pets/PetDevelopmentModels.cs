using System.Text.Json.Serialization;

namespace Tessalume.Core.Pets;

public static class PetDevelopmentProjectContract
{
    public const int SchemaVersion = 1;
    public const string ManifestFileName = "pet-project.json";

    public static IReadOnlyList<string> RequiredPreviewActionKeys { get; } =
    [
        "idle",
        "move-right",
        "move-left",
        "wave-touch",
        "jump",
        "blocked",
        "needs-input",
        "running",
        "ready",
        "gaze-clockwise",
        "showcase",
    ];
}

public sealed record PetDevelopmentProjectManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = PetDevelopmentProjectContract.SchemaVersion;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("projectVersion")]
    public string ProjectVersion { get; init; } = string.Empty;

    [JsonPropertyName("petManifestPath")]
    public string PetManifestPath { get; init; } = PetPackageContract.ManifestFileName;

    [JsonPropertyName("previewOutputDirectory")]
    public string PreviewOutputDirectory { get; init; } = string.Empty;

    [JsonPropertyName("protocol")]
    public PetProtocolMetadata Protocol { get; init; } = new();

    [JsonPropertyName("author")]
    public PetAuthorMetadata Author { get; init; } = new();

    [JsonPropertyName("license")]
    public PetLicenseMetadata License { get; init; } = new();

    [JsonPropertyName("rights")]
    public PetRightsMetadata Rights { get; init; } = new();

    [JsonPropertyName("previews")]
    public IReadOnlyList<PetPreviewMetadata> Previews { get; init; } = [];

    [JsonPropertyName("recommendedThemeIds")]
    public IReadOnlyList<string> RecommendedThemeIds { get; init; } = [];
}

public sealed record PetDevelopmentProject(
    string RootDirectory,
    string ProjectManifestPath,
    string PreviewOutputDirectory,
    PetDevelopmentProjectManifest Manifest,
    PetManifest? PetManifest,
    IReadOnlyDictionary<string, string> ResolvedPreviews,
    IReadOnlyDictionary<string, PetGifInfo> PreviewInfos,
    DateTimeOffset LastUpdated)
{
    public IEnumerable<(PetPreviewMetadata Metadata, string FullPath, PetGifInfo GifInfo)> PreviewFiles =>
        Manifest.Previews
            .Where(preview =>
                ResolvedPreviews.ContainsKey(preview.Path) &&
                PreviewInfos.ContainsKey(preview.Path))
            .Select(preview =>
                (preview, ResolvedPreviews[preview.Path], PreviewInfos[preview.Path]));
}

public sealed record PetDevelopmentLoadResult(
    PetDevelopmentProject? Project,
    PetValidationResult Validation);
