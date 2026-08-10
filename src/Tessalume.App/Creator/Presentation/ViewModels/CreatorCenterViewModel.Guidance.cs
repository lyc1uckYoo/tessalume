namespace Tessalume.App.Creator;

internal sealed partial class CreatorCenterViewModel
{
    private bool _promptReady;
    private bool _promptCopied;
    private bool _isStartingNewTheme;

    public CreatorGuidanceState Guidance { get; private set; } = CreatorGuidanceState.Start;

    public void SetPromptState(bool ready, bool copied, bool isStartingNewTheme)
    {
        if (_promptReady == ready &&
            _promptCopied == copied &&
            _isStartingNewTheme == isStartingNewTheme) return;
        _promptReady = ready;
        _promptCopied = copied;
        _isStartingNewTheme = isStartingNewTheme;
        UpdateGuidance();
    }

    private void UpdateGuidance()
    {
        var next = CreatorGuidancePlanner.Resolve(new CreatorGuidanceContext(
            IsBusy || IsDevelopmentBusy,
            _promptReady,
            _promptCopied,
            HasSelectedWorkspace,
            WorkspaceExists,
            HasSelectedProject,
            SelectedProject?.ErrorCount ?? 0,
            _acceptance.HasRun,
            _acceptance.Passed,
            CanReleaseSelectedProject,
            _isStartingNewTheme));
        if (next == Guidance) return;
        Guidance = next;
        OnPropertyChanged(nameof(Guidance));
    }
}
