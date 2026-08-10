using System.Windows;

namespace Tessalume.App.Creator;

public partial class CreatorCenterView
{
    private void GuidancePrimary_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        switch (_viewModel.Guidance.Action)
        {
            case CreatorGuidanceAction.EditPrompt:
                ExpandPromptEditor();
                PromptView.PromptWorkNameBox.Focus();
                break;
            case CreatorGuidanceAction.CreateWorkspace:
                CreateWorkspace_Click(sender, e);
                break;
            case CreatorGuidanceAction.RelocateWorkspace:
                RelocateWorkspace_Click(sender, e);
                break;
            case CreatorGuidanceAction.CopyPrompt:
                TryCopyPrompt();
                break;
            case CreatorGuidanceAction.ReviewIssues:
                NavigateTo(CreatorCenterRoute.Inspection);
                CopyRepairPrompt_Click(sender, e);
                break;
            case CreatorGuidanceAction.RunAcceptance:
                NavigateTo(CreatorCenterRoute.Acceptance);
                RunAcceptance_Click(sender, e);
                break;
            case CreatorGuidanceAction.ReviewAcceptance:
                NavigateTo(CreatorCenterRoute.Acceptance);
                break;
            case CreatorGuidanceAction.OpenRelease:
                NavigateTo(CreatorCenterRoute.Release);
                break;
            case CreatorGuidanceAction.ReviewWorkflow:
                NavigateTo(CreatorCenterRoute.Workflow);
                break;
        }
    }

    private void RenderGuidance()
    {
        if (_viewModel is null) return;
        var guidance = _viewModel.Guidance;
        GuidancePrimaryButton.IsEnabled = guidance.CanExecute;
        GuidancePrimaryButton.Visibility = GuidanceActionBelongsToCurrentPage(guidance.Action)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private bool GuidanceActionBelongsToCurrentPage(CreatorGuidanceAction action) => action switch
    {
        CreatorGuidanceAction.EditPrompt or
        CreatorGuidanceAction.CreateWorkspace or
        CreatorGuidanceAction.RelocateWorkspace or
        CreatorGuidanceAction.CopyPrompt => _currentRoute == CreatorCenterRoute.Workspace,
        CreatorGuidanceAction.ReviewIssues => _currentRoute == CreatorCenterRoute.Inspection,
        CreatorGuidanceAction.RunAcceptance or CreatorGuidanceAction.ReviewAcceptance =>
            _currentRoute == CreatorCenterRoute.Acceptance,
        CreatorGuidanceAction.ReviewWorkflow => _currentRoute == CreatorCenterRoute.Workflow,
        CreatorGuidanceAction.OpenRelease => _currentRoute == CreatorCenterRoute.Release,
        _ => false,
    };
}
