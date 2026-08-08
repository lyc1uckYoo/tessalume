using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal sealed class CreatorWorkflowEvaluator : ICreatorWorkflowEvaluator
{
    private static readonly ThemeProjectHealthGroup[] ContractGroups =
    [
        ThemeProjectHealthGroup.Manifest,
        ThemeProjectHealthGroup.EntryPoints,
        ThemeProjectHealthGroup.Template,
        ThemeProjectHealthGroup.Css,
        ThemeProjectHealthGroup.Script,
        ThemeProjectHealthGroup.Resources,
    ];

    public CreatorWorkflowSnapshot Evaluate(ThemeProjectSnapshot project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var characterReady = !string.IsNullOrWhiteSpace(project.CharacterName);
        var assetsReady = project.AssetCount >= 11 && !HasError(project, ThemeProjectHealthGroup.Assets);
        var contractReady = !HasError(project, ContractGroups);
        var acceptanceReady = project.SupportsLight && project.SupportsDark &&
            !HasError(project, ThemeProjectHealthGroup.Previews);
        var releaseReady = project.Health.CanExport;

        var stages = new[]
        {
            new CreatorWorkflowStage(
                CreatorWorkflowStageId.CharacterResearch,
                characterReady ? CreatorWorkflowStageState.Completed : CreatorWorkflowStageState.NeedsAttention,
                "角色研究",
                characterReady ? $"已确认角色：{project.CharacterName}" : "补充角色名称与视觉方向。"),
            new CreatorWorkflowStage(
                CreatorWorkflowStageId.AssetProduction,
                ResolveState(assetsReady, HasWarning(project, ThemeProjectHealthGroup.Assets)),
                "素材生成",
                assetsReady ? $"标准素材已准备 · {project.AssetCount} 个" : $"当前识别到 {project.AssetCount} 个素材，模板需要至少 11 个。"),
            new CreatorWorkflowStage(
                CreatorWorkflowStageId.ContractValidation,
                ResolveState(contractReady, HasWarning(project, ContractGroups)),
                "契约校验",
                contractReady ? "清单、入口、脚本和 Template 1.0 契约已通过。" : "仍有结构或运行契约错误需要修复。"),
            new CreatorWorkflowStage(
                CreatorWorkflowStageId.VisualAcceptance,
                ResolveState(acceptanceReady, HasWarning(project, ThemeProjectHealthGroup.Previews)),
                "亮暗验收",
                acceptanceReady ? "亮色、暗色和预览覆盖均已确认。" : "需要同时完成亮色、暗色与预览验收。"),
            new CreatorWorkflowStage(
                CreatorWorkflowStageId.Release,
                releaseReady ? CreatorWorkflowStageState.Completed : CreatorWorkflowStageState.Blocked,
                "导出发布",
                releaseReady ? "项目满足本地导出条件。" : $"还有 {project.Health.ErrorCount} 项阻断错误。"),
        };

        var checklist = new[]
        {
            new CreatorReleaseChecklistItem("character", "角色信息完整", "清单包含明确的角色身份。", characterReady, true),
            new CreatorReleaseChecklistItem("assets", "11 个标准素材位", $"当前素材：{project.AssetCount} 个。", assetsReady, true),
            new CreatorReleaseChecklistItem("contract", "Template 1.0 契约", "清单、入口、脚本与资源引用通过检查。", contractReady, true),
            new CreatorReleaseChecklistItem("modes", "亮暗模式覆盖", "亮色、暗色和对应预览均可验收。", acceptanceReady, true),
            new CreatorReleaseChecklistItem("health", "无阻断错误", $"错误 {project.Health.ErrorCount} 项，建议 {project.Health.WarningCount} 项。", releaseReady, true),
        };
        return new CreatorWorkflowSnapshot(stages, checklist);
    }

    private static CreatorWorkflowStageState ResolveState(bool complete, bool hasWarning) =>
        complete
            ? hasWarning ? CreatorWorkflowStageState.NeedsAttention : CreatorWorkflowStageState.Completed
            : CreatorWorkflowStageState.Blocked;

    private static bool HasError(
        ThemeProjectSnapshot project,
        params ThemeProjectHealthGroup[] groups) =>
        project.Health.Checks.Any(check =>
            groups.Contains(check.Group) && check.Severity == ThemeProjectHealthSeverity.Error);

    private static bool HasWarning(
        ThemeProjectSnapshot project,
        params ThemeProjectHealthGroup[] groups) =>
        project.Health.Checks.Any(check =>
            groups.Contains(check.Group) && check.Severity == ThemeProjectHealthSeverity.Warning);
}
