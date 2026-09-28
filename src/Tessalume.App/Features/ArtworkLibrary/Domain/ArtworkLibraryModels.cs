using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.ArtworkLibrary.Domain;

internal sealed record ArtworkLibraryCharacter(string Id, string Name, int ImageCount);

internal sealed record ArtworkLibraryUsage(string ThemeId, string ThemeName, ArtworkRegion Region, ArtworkColorMode Mode);

internal sealed record ArtworkLibraryRecommendation(
    ArtworkRegion Region, ArtworkColorMode Mode, ThemeArtworkAdjustment Adjustment);

internal sealed record ArtworkLibraryItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string CharacterId { get; init; } = "personal";
    public string CharacterName { get; init; } = "我的图片";
    public string AbsolutePath { get; init; } = string.Empty;
    public string? StoredPath { get; init; }
    public string? SourceThemeId { get; init; }
    public string? SourceThemeName { get; init; }
    public bool IsBuiltIn { get; init; }
    public bool IsFavorite { get; init; }
    public bool CanDelete => !IsBuiltIn && Id.StartsWith("personal:", StringComparison.Ordinal);
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public DateTimeOffset AddedAt { get; init; }
    public IReadOnlyList<ArtworkRegion> Regions { get; init; } = [];
    public IReadOnlyList<ArtworkColorMode> Modes { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<ArtworkLibraryUsage> Usages { get; init; } = [];
    public IReadOnlyList<ArtworkLibraryRecommendation> Recommendations { get; init; } = [];

    public bool Matches(string? query) => string.IsNullOrWhiteSpace(query) ||
        new[] { Name, CharacterName, SourceThemeName ?? string.Empty }.Concat(Tags)
            .Any(text => text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
}

internal sealed record ArtworkLibrarySnapshot(
    IReadOnlyList<ArtworkLibraryItem> Items,
    IReadOnlyList<ArtworkLibraryCharacter> Characters,
    IReadOnlyList<string> Diagnostics);
