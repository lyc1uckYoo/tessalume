using System.Collections.ObjectModel;

namespace Tessalume.App.Creator;

internal sealed partial class CreatorCenterViewModel
{
    private CreatorAcceptanceSnapshot _acceptance = CreatorAcceptanceSnapshot.Pending;

    public ObservableCollection<CreatorAcceptanceCheckViewModel> AcceptanceChecks { get; } = [];

    public bool AcceptanceHasRun => _acceptance.HasRun;

    public int StaticThemeIssueCount => SelectedProject is null
        ? 0
        : SelectedProject.ErrorCount + SelectedProject.WarningCount;

    public int RuntimeCompatibilityIssueCount => _acceptance.CompatibilityIssueCount;

    public string AcceptanceSummaryText => !_acceptance.HasRun
        ? "尚未运行，不会更改当前主题项目。"
        : _acceptance.Passed
            ? $"批量验收完成 · {_acceptance.Checks.Count(check => check.State == CreatorAcceptanceState.Passed)} 项通过"
            : $"发现 {_acceptance.ThemeIssueCount} 项主题问题、{_acceptance.CompatibilityIssueCount} 项兼容问题、" +
              $"{_acceptance.Checks.Count(check => check.State == CreatorAcceptanceState.NeedsAttention)} 项待复验";

    public string AcceptanceCompletedText => _acceptance.HasRun
        ? $"最近验收 {_acceptance.CompletedAt:HH:mm:ss}"
        : "等待验收";

    public async Task RunAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var project = SelectedProject
            ?? throw new InvalidOperationException("请先选择要验收的主题项目。");
        var operation = BeginDevelopmentOperation(cancellationToken);
        IsDevelopmentBusy = true;
        try
        {
            _acceptance = await _acceptanceService.RunAsync(project.Snapshot, operation.Token);
            RenderAcceptance();
            UpdateCreatorWorkflow(project);
        }
        finally
        {
            if (CompleteDevelopmentOperation(operation)) IsDevelopmentBusy = false;
        }
    }

    private void ResetAcceptance(ThemeProjectItemViewModel? project)
    {
        _acceptance = CreatorAcceptanceSnapshot.Pending;
        RenderAcceptance();
        OnPropertyChanged(nameof(StaticThemeIssueCount));
        if (project is null) OnPropertyChanged(nameof(AcceptanceSummaryText));
    }

    private void RenderAcceptance()
    {
        AcceptanceChecks.Clear();
        foreach (var check in _acceptance.Checks)
        {
            AcceptanceChecks.Add(new CreatorAcceptanceCheckViewModel(check));
        }
        OnPropertyChanged(nameof(AcceptanceHasRun));
        OnPropertyChanged(nameof(RuntimeCompatibilityIssueCount));
        OnPropertyChanged(nameof(AcceptanceSummaryText));
        OnPropertyChanged(nameof(AcceptanceCompletedText));
        UpdateGuidance();
    }
}
