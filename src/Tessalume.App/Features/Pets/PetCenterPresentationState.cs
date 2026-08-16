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
    string? FilePath);

internal sealed record PetCenterPresentationState
{
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
