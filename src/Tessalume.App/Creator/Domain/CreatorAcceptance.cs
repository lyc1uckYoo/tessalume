namespace Tessalume.App.Creator;

internal enum CreatorAcceptanceCheckId
{
    LightMode,
    DarkMode,
    Composer,
    Messages,
    ResponsiveLayout,
}

internal enum CreatorAcceptanceState
{
    Passed,
    NeedsAttention,
    Failed,
}

internal enum CreatorIssueOrigin
{
    None,
    ThemeProject,
    RuntimeCompatibility,
}

internal sealed record CreatorAcceptanceCheck(
    CreatorAcceptanceCheckId Id,
    CreatorAcceptanceState State,
    CreatorIssueOrigin IssueOrigin,
    string Title,
    string Detail);

internal sealed record CreatorAcceptanceSnapshot(
    DateTimeOffset CompletedAt,
    IReadOnlyList<CreatorAcceptanceCheck> Checks)
{
    public bool HasRun => CompletedAt != default;

    public bool Passed => HasRun && Checks.All(check => check.State == CreatorAcceptanceState.Passed);

    public int ThemeIssueCount => Checks.Count(check =>
        check.State == CreatorAcceptanceState.Failed && check.IssueOrigin == CreatorIssueOrigin.ThemeProject);

    public int CompatibilityIssueCount => Checks.Count(check =>
        check.State == CreatorAcceptanceState.Failed && check.IssueOrigin == CreatorIssueOrigin.RuntimeCompatibility);

    public static CreatorAcceptanceSnapshot Pending { get; } = new(
        default,
        CreatorAcceptanceCatalog.CreatePendingChecks());
}

internal static class CreatorAcceptanceCatalog
{
    public static IReadOnlyList<CreatorAcceptanceCheck> CreatePendingChecks() =>
    [
        Pending(CreatorAcceptanceCheckId.LightMode, "亮色模式"),
        Pending(CreatorAcceptanceCheckId.DarkMode, "暗色模式"),
        Pending(CreatorAcceptanceCheckId.Composer, "输入框"),
        Pending(CreatorAcceptanceCheckId.Messages, "消息框"),
        Pending(CreatorAcceptanceCheckId.ResponsiveLayout, "响应式布局"),
    ];

    private static CreatorAcceptanceCheck Pending(CreatorAcceptanceCheckId id, string title) =>
        new(id, CreatorAcceptanceState.NeedsAttention, CreatorIssueOrigin.None, title, "等待批量验收。");
}
