using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.ArtworkLibrary.Presentation;

internal class ArtworkLibraryTargetEventArgs(string themeId, ArtworkRegion region, ArtworkColorMode mode) : EventArgs
{
    public string ThemeId { get; } = themeId;
    public ArtworkRegion Region { get; } = region;
    public ArtworkColorMode Mode { get; } = mode;
}

internal sealed class ArtworkLibraryApplyEventArgs(
    string themeId, ArtworkRegion region, ArtworkColorMode mode,
    ArtworkLibraryItem item, ThemeArtworkAdjustment adjustment) : ArtworkLibraryTargetEventArgs(themeId, region, mode)
{
    public ArtworkLibraryItem Item { get; } = item;
    public ThemeArtworkAdjustment Adjustment { get; } = adjustment;
}

internal sealed class ArtworkLibraryImportEventArgs(
    string characterId, string characterName, ArtworkRegion? region, ArtworkColorMode? mode) : EventArgs
{
    public string CharacterId { get; } = characterId;
    public string CharacterName { get; } = characterName;
    public ArtworkRegion? Region { get; } = region;
    public ArtworkColorMode? Mode { get; } = mode;
}

internal sealed class ArtworkLibraryDeleteEventArgs(ArtworkLibraryItem item) : EventArgs
{
    public ArtworkLibraryItem Item { get; } = item;
}
