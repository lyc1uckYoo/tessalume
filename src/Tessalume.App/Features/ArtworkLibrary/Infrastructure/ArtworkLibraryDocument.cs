using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.ArtworkLibrary.Infrastructure;

// Only portable relative paths belong in this document. Theme source paths are
// discovered afresh from the installed packages and never serialized here.
internal sealed record ArtworkLibraryDocument
{
    public int SchemaVersion { get; init; } = 1;
    public List<ArtworkLibraryStoredImage> Images { get; init; } = [];
    // Removed gallery entries keep their bytes for applied settings and undo.
    // These paths must not be rediscovered as legacy personal images on load.
    public List<string> HiddenImagePaths { get; init; } = [];
    public List<string> Favorites { get; init; } = [];
    public List<ArtworkLibraryComposition> Compositions { get; init; } = [];
    public Dictionary<string, string> PreparedImages { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ArtworkLibraryStoredImage> PreparedMetadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ArtworkLibrarySourceFingerprint> SourceFingerprints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record ArtworkLibrarySourceFingerprint(long Length, long LastWriteTicks, string Sha256);

internal sealed record ArtworkLibraryStoredImage
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string CharacterId { get; init; } = "personal";
    public string CharacterName { get; init; } = "我的图片";
    public string StoredPath { get; init; } = string.Empty;
    public List<ArtworkRegion> Regions { get; init; } = [];
    public List<ArtworkColorMode> Modes { get; init; } = [];
    public DateTimeOffset AddedAt { get; init; }
}

internal sealed record ArtworkLibraryComposition(
    string ItemId, string ThemeId, ArtworkRegion Region, ArtworkColorMode Mode,
    ThemeArtworkAdjustment Adjustment);
