using System.Collections.ObjectModel;

namespace Tessalume.App.Creator;

internal sealed partial class CreatorCenterViewModel
{
    public ObservableCollection<CreatorWorkflowStageViewModel> WorkflowStages { get; } = [];

    public ObservableCollection<CreatorReleaseChecklistItemViewModel> ReleaseChecklist { get; } = [];

    public string WorkflowProgressText { get; private set; } = "选择项目后生成创作流程";

    public string ReleaseReadinessText { get; private set; } = "尚未选择可验收的主题项目";

    public bool CanReleaseSelectedProject { get; private set; }

    private void UpdateCreatorWorkflow(ThemeProjectItemViewModel? project)
    {
        WorkflowStages.Clear();
        ReleaseChecklist.Clear();
        if (project is null)
        {
            WorkflowProgressText = "选择项目后生成创作流程";
            ReleaseReadinessText = "尚未选择可验收的主题项目";
            CanReleaseSelectedProject = false;
        }
        else
        {
            var snapshot = _workflowEvaluator.Evaluate(project.Snapshot);
            var stages = snapshot.Stages
                .Select(ApplyAcceptanceToStage)
                .ToArray();
            for (var index = 0; index < stages.Length; index++)
            {
                WorkflowStages.Add(new CreatorWorkflowStageViewModel(index + 1, stages[index]));
            }
            foreach (var item in snapshot.ReleaseChecklist)
            {
                ReleaseChecklist.Add(new CreatorReleaseChecklistItemViewModel(item));
            }
            ReleaseChecklist.Add(new CreatorReleaseChecklistItemViewModel(new CreatorReleaseChecklistItem(
                "runtime-acceptance",
                "运行验收已完成",
                "批量检查亮色、暗色、输入框、消息框和响应式布局。",
                _acceptance.Passed,
                Blocking: true)));
            var completedStages = stages.Count(stage => stage.State == CreatorWorkflowStageState.Completed);
            WorkflowProgressText = $"{completedStages} / {stages.Length} 个阶段已完成";
            CanReleaseSelectedProject = snapshot.CanRelease && _acceptance.Passed;
            ReleaseReadinessText = CanReleaseSelectedProject
                ? "发布清单已全部通过，可以导出分享 ZIP"
                : $"还有 {ReleaseChecklist.Count(item => item.Blocking && !item.Passed)} 项阻断条件";
        }

        OnPropertyChanged(nameof(WorkflowProgressText));
        OnPropertyChanged(nameof(ReleaseReadinessText));
        OnPropertyChanged(nameof(CanReleaseSelectedProject));
        UpdateGuidance();
    }

    private CreatorWorkflowStage ApplyAcceptanceToStage(CreatorWorkflowStage stage)
    {
        if (stage.Id == CreatorWorkflowStageId.Release && !_acceptance.Passed)
        {
            return stage with
            {
                State = CreatorWorkflowStageState.Blocked,
                Description = "先完成运行验收，再进入最终导出清单。",
            };
        }
        if (stage.Id != CreatorWorkflowStageId.VisualAcceptance) return stage;
        if (!_acceptance.HasRun)
        {
            return stage with
            {
                State = CreatorWorkflowStageState.Blocked,
                Description = "在“运行验收”页批量检查亮暗模式、输入框、消息框和响应式布局。",
            };
        }
        return stage with
        {
            State = _acceptance.Passed
                ? CreatorWorkflowStageState.Completed
                : CreatorWorkflowStageState.Blocked,
            Description = AcceptanceSummaryText,
        };
    }
}
