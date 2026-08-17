namespace Tessalume.App.Creator;

internal enum CreatorWorkflowStageId
{
    CharacterResearch,
    AssetProduction,
    ArtworkRecommendations,
    ContractValidation,
    VisualAcceptance,
    Release,
}

internal enum CreatorWorkflowStageState
{
    Completed,
    NeedsAttention,
    Blocked,
}

internal sealed record CreatorWorkflowStage(
    CreatorWorkflowStageId Id,
    CreatorWorkflowStageState State,
    string Title,
    string Description);

internal sealed record CreatorReleaseChecklistItem(
    string Code,
    string Title,
    string Description,
    bool Passed,
    bool Blocking);

internal sealed record CreatorWorkflowSnapshot(
    IReadOnlyList<CreatorWorkflowStage> Stages,
    IReadOnlyList<CreatorReleaseChecklistItem> ReleaseChecklist)
{
    public int CompletedStageCount => Stages.Count(stage =>
        stage.State == CreatorWorkflowStageState.Completed);

    public bool CanRelease => ReleaseChecklist.All(item => !item.Blocking || item.Passed);
}
