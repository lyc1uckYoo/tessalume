namespace Tessalume.App.Creator;

internal sealed record CreatorAcceptanceCheckViewModel(CreatorAcceptanceCheck Check)
{
    public string Number => ((int)Check.Id + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    public string Title => Check.Title;

    public string Detail => Check.Detail;

    public string StatusTone => Check.State switch
    {
        CreatorAcceptanceState.Passed => "ready",
        CreatorAcceptanceState.NeedsAttention => "warning",
        _ => "error",
    };

    public string StatusText => Check.State switch
    {
        CreatorAcceptanceState.Passed => "通过",
        CreatorAcceptanceState.NeedsAttention => "待观察",
        _ => "未通过",
    };

    public string OriginText => Check.IssueOrigin switch
    {
        CreatorIssueOrigin.ThemeProject => "主题项目",
        CreatorIssueOrigin.RuntimeCompatibility => "运行时兼容",
        _ => "验收结果",
    };
}
