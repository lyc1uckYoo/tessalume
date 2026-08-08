namespace Tessalume.App.Creator;

internal sealed record CreatorWorkflowStageViewModel
{
    public CreatorWorkflowStageViewModel(int number, CreatorWorkflowStage stage)
    {
        Number = number.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        Title = stage.Title;
        Description = stage.Description;
        StatusTone = stage.State switch
        {
            CreatorWorkflowStageState.Completed => "ready",
            CreatorWorkflowStageState.NeedsAttention => "warning",
            _ => "error",
        };
        StatusText = stage.State switch
        {
            CreatorWorkflowStageState.Completed => "已完成",
            CreatorWorkflowStageState.NeedsAttention => "有建议",
            _ => "待处理",
        };
    }

    public string Number { get; }

    public string Title { get; }

    public string Description { get; }

    public string StatusTone { get; }

    public string StatusText { get; }
}

internal sealed record CreatorReleaseChecklistItemViewModel
{
    public CreatorReleaseChecklistItemViewModel(CreatorReleaseChecklistItem item)
    {
        Code = item.Code;
        Title = item.Title;
        Description = item.Description;
        Passed = item.Passed;
        Blocking = item.Blocking;
    }

    public string Code { get; }

    public string Title { get; }

    public string Description { get; }

    public bool Passed { get; }

    public bool Blocking { get; }

    public string StatusTone => Passed ? "ready" : Blocking ? "error" : "warning";

    public string StatusText => Passed ? "通过" : Blocking ? "阻断" : "建议";

    public string IconText => Passed ? "✓" : Blocking ? "!" : "•";
}
