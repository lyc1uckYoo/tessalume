namespace Tessalume.App.Features.Pets;

internal enum PetCenterStatus
{
    Loading,
    NotInstalled,
    Installed,
    AwaitingCodexSelection,
    UpdateAvailable,
    UnknownModification,
    Damaged,
    DuplicateIdConflict,
    Busy,
    Error,
}

internal enum PetCenterAction
{
    Refresh,
    Install,
    OpenCodex,
    Update,
    Repair,
    RecoverState,
    ReplaceModified,
    ExplainConflict,
}

internal sealed record PetPreviewFrame(
    string Key,
    string Label,
    string? FilePath,
    string Kind = "action",
    int ExpectedFrameCount = 2,
    int SourceWidth = 1,
    int SourceHeight = 1,
    int RepresentativeFrame = 0,
    string Revision = "",
    string? RuntimeSpritesheetPath = null,
    string RuntimeSpritesheetRevision = "");

internal sealed record PetCenterPresentationState
{
    public string PetId { get; init; } = PetApplicationService.BuiltInPetId;

    public string DisplayName { get; init; } = "飞行雪绒";

    public string Description { get; init; } = "爱弥斯的电子幽灵伙伴，陪你工作与互动。";

    public string SourceBadge { get; init; } = "正式宠物";

    public string RecommendedThemeId { get; init; } = PetApplicationService.RecommendedThemeId;

    public string RecommendedThemeName { get; init; } = "爱弥斯 · 星海远航";

    public bool HasRecommendedTheme { get; init; } = true;

    public PetCenterStatus Status { get; init; } = PetCenterStatus.Loading;

    public string StatusTitle { get; init; } = "正在检查";

    public string StatusDetail { get; init; } = "正在校验本地宠物文件…";

    public string ProductVersion { get; init; } = "—";

    public string ProtocolSummary { get; init; } = "—";

    public string Author { get; init; } = "Tessalume";

    public string LicenseSummary { get; init; } = "随内置包提供";

    public string InstallLocation { get; init; } = "当前用户 .codex\\pets";

    public PetCenterAction PrimaryAction { get; init; } = PetCenterAction.Refresh;

    public string PrimaryActionText { get; init; } = "重新检查";

    public bool PrimaryActionEnabled { get; init; } = true;

    public bool CanUninstall { get; init; }

    public bool CanAcknowledgeSelection { get; init; }

    public bool CanRestoreBackup { get; init; }

    public string? LatestBackupLabel { get; init; }

    public bool IsBusy { get; init; }

    public IReadOnlyList<PetPreviewFrame> PreviewFrames { get; init; } = [];
}
