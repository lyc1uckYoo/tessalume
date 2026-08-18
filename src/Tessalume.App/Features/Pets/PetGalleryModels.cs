using Tessalume.Core.Pets;

namespace Tessalume.App.Features.Pets;

internal enum PetGallerySourceKind
{
    Official,
    Development,
}

internal sealed record PetGalleryEntry
{
    public required string EntryKey { get; init; }

    public required string PetId { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string Version { get; init; }

    public required string Author { get; init; }

    public required string LicenseSummary { get; init; }

    public required string ProtocolSummary { get; init; }

    public required string RootDirectory { get; init; }

    public required string SourceBadge { get; init; }

    public required string HealthMessage { get; init; }

    public required DateTimeOffset LastUpdated { get; init; }

    public required PetGallerySourceKind SourceKind { get; init; }

    public required IReadOnlyList<PetPreviewFrame> PreviewFrames { get; init; }

    public string RecommendedThemeId { get; init; } = string.Empty;

    public string RecommendedThemeName { get; init; } = string.Empty;

    public bool IsValid { get; init; }

    public bool UsesLastGoodPreview { get; init; }

    public PetPackage? Package { get; init; }

    public PetDevelopmentProject? DevelopmentProject { get; init; }

    public bool IsDevelopment => SourceKind == PetGallerySourceKind.Development;

    public bool CanOpen => PreviewFrames.Count > 0 && (IsValid || UsesLastGoodPreview);

    public PetPreviewFrame? CoverPreview
    {
        get
        {
            for (var index = 0; index < PreviewFrames.Count; index++)
            {
                if (string.Equals(PreviewFrames[index].Key, "idle", StringComparison.Ordinal))
                {
                    return PreviewFrames[index];
                }
            }
            return PreviewFrames.Count == 0 ? null : PreviewFrames[0];
        }
    }
}

internal sealed record PetGallerySnapshot(
    IReadOnlyList<PetGalleryEntry> Entries,
    string DevelopmentProjectsRoot,
    DateTimeOffset RefreshedAt)
{
    public IReadOnlyList<PetGalleryEntry> DevelopmentEntries => Entries
        .Where(entry => entry.IsDevelopment)
        .ToArray();

    public IReadOnlyList<PetGalleryEntry> OfficialEntries => Entries
        .Where(entry => !entry.IsDevelopment)
        .ToArray();
}
