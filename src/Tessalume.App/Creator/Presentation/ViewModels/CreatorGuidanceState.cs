namespace Tessalume.App.Creator;

internal enum CreatorGuidanceAction
{
    None,
    EditPrompt,
    CreateWorkspace,
    RelocateWorkspace,
    CopyPrompt,
    ReviewIssues,
    RunAcceptance,
    ReviewAcceptance,
    ReviewWorkflow,
    OpenRelease,
}

internal sealed record CreatorGuidanceContext(
    bool IsBusy,
    bool PromptReady,
    bool PromptCopied,
    bool HasWorkspace,
    bool WorkspaceExists,
    bool HasProject,
    int ErrorCount,
    bool AcceptanceHasRun,
    bool AcceptancePassed,
    bool CanRelease,
    bool IsStartingNewTheme = false);

internal sealed record CreatorGuidanceState(
    string StepText,
    string Title,
    string Description,
    string PrimaryActionText,
    CreatorGuidanceAction Action,
    bool CanExecute)
{
    public static CreatorGuidanceState Start { get; } = new(
        "第 1 步",
        "先告诉 Tessalume 你准备创作谁",
        "只需要作品名称和角色名称；其余视觉要求可以稍后补充。",
        "填写创作需求",
        CreatorGuidanceAction.EditPrompt,
        true);
}

internal static class CreatorGuidancePlanner
{
    public static CreatorGuidanceState Resolve(CreatorGuidanceContext context)
    {
        if (context.IsBusy)
        {
            return new CreatorGuidanceState(
                "正在处理",
                "Tessalume 正在检查当前项目",
                "完成后会自动给出下一步，不需要重复点击或手动刷新。",
                "请稍候",
                CreatorGuidanceAction.None,
                false);
        }
        if (context.IsStartingNewTheme)
        {
            return context.PromptReady
                ? ReadyToCreateWorkspace()
                : CreatorGuidanceState.Start;
        }
        if (!context.PromptReady && (!context.HasWorkspace || !context.HasProject))
        {
            return CreatorGuidanceState.Start;
        }
        if (!context.HasWorkspace)
        {
            return ReadyToCreateWorkspace();
        }
        if (!context.WorkspaceExists)
        {
            return new CreatorGuidanceState(
                "需要处理",
                "工作区位置已经失效",
                "重新选择移动后的工作区；主题与素材不会被删除或覆盖。",
                "重新定位工作区",
                CreatorGuidanceAction.RelocateWorkspace,
                true);
        }
        if (!context.HasProject)
        {
            if (context.PromptCopied)
            {
                return new CreatorGuidanceState(
                    "第 2 步",
                    "提示词已复制，现在去 Codex 发送",
                    "在 Codex 中打开当前工作区并粘贴；主题写入稳定后会自动发现和体检。",
                    "再次复制提示词",
                    CreatorGuidanceAction.CopyPrompt,
                    true);
            }
            return new CreatorGuidanceState(
                "第 2 步",
                "把角色需求交给 Codex",
                "复制创作提示词，在 Codex 中打开当前工作区并发送；生成的主题会被自动发现。",
                "复制给 Codex",
                CreatorGuidanceAction.CopyPrompt,
                true);
        }
        if (context.ErrorCount > 0)
        {
            return new CreatorGuidanceState(
                "第 3 步",
                $"先修复 {context.ErrorCount} 项阻断问题",
                "Tessalume 已按文件定位问题，并会生成只针对当前项目的 Codex 修复提示词。",
                "复制修复提示词",
                CreatorGuidanceAction.ReviewIssues,
                true);
        }
        if (!context.AcceptanceHasRun)
        {
            return new CreatorGuidanceState(
                "第 4 步",
                "项目结构已通过，开始真实页面验收",
                "将自动检查亮色、暗色、输入框、消息框和响应式布局，结束后恢复原显示模式。",
                "开始自动验收",
                CreatorGuidanceAction.RunAcceptance,
                true);
        }
        if (!context.AcceptancePassed)
        {
            return new CreatorGuidanceState(
                "需要复验",
                "运行验收发现了待处理项",
                "查看每项结果，先分清主题问题和页面兼容问题，再按提示修复。",
                "查看验收结果",
                CreatorGuidanceAction.ReviewAcceptance,
                true);
        }
        if (context.CanRelease)
        {
            return new CreatorGuidanceState(
                "第 5 步",
                "全部检查通过，可以导出主题",
                "导出前清单已通过；分享包会同时计算 SHA-256，方便校验。",
                "前往导出",
                CreatorGuidanceAction.OpenRelease,
                true);
        }
        return new CreatorGuidanceState(
            "继续完善",
            "还有发布条件尚未满足",
            "查看完整流程，Tessalume 会标出仍未完成的阶段和原因。",
            "查看完整流程",
            CreatorGuidanceAction.ReviewWorkflow,
            true);
    }

    private static CreatorGuidanceState ReadyToCreateWorkspace() => new(
        "第 1 步",
        "需求已准备，创建专属主题工作区",
        "选择保存位置后会生成模板、创作 Skill 和本地检查工具，并自动复制提示词。",
        "创建主题工作区",
        CreatorGuidanceAction.CreateWorkspace,
        true);
}
