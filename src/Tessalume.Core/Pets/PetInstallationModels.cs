namespace Tessalume.Core.Pets;

public enum PetInstallationStatus
{
    NotInstalled,
    Installed,
    InstalledAwaitingCodexSelection,
    UpdateAvailable,
    UnknownModification,
    Damaged,
    DuplicateIdConflict,
}

public enum PetInstallIntent
{
    Install,
    UpdateConfirmed,
    RepairConfirmed,
    ReplaceConfirmed,
}

public enum PetUninstallIntent
{
    Safe,
    RemoveModifiedManagedFilesConfirmed,
}

public sealed record PetInstallerOptions
{
    public PetInstallerOptions(string petsRoot, string backupRoot, string statePath)
    {
        PetsRoot = PetPathSafety.NormalizeDirectory(petsRoot);
        BackupRoot = PetPathSafety.NormalizeDirectory(backupRoot);
        StatePath = Path.GetFullPath(statePath);
        if (string.Equals(PetsRoot, Path.GetPathRoot(PetsRoot), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(BackupRoot, Path.GetPathRoot(BackupRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("宠物目录和备份目录不能是文件系统根目录。");
        }
        if (PetPathSafety.IsContainedOrEqual(PetsRoot, BackupRoot) ||
            PetPathSafety.IsContainedOrEqual(BackupRoot, PetsRoot))
        {
            throw new ArgumentException("宠物目录与备份目录不能相同或互相包含。");
        }
        if (PetPathSafety.IsContainedOrEqual(PetsRoot, StatePath) ||
            PetPathSafety.IsContainedOrEqual(BackupRoot, StatePath))
        {
            throw new ArgumentException("宠物管理状态不能保存在宠物目录或备份目录内。");
        }
    }

    public string PetsRoot { get; }

    public string BackupRoot { get; }

    public string StatePath { get; }
}

public sealed record PetInstallationSnapshot(
    PetInstallationStatus Status,
    string PetId,
    string? ManagedProductVersion,
    IReadOnlyList<string> InstalledDirectories,
    string Detail,
    bool StateIsValid)
{
    public bool IsInstalled => Status is
        PetInstallationStatus.Installed or
        PetInstallationStatus.InstalledAwaitingCodexSelection or
        PetInstallationStatus.UpdateAvailable;
}

public sealed record PetInstallResult(
    bool Changed,
    PetInstallationSnapshot Snapshot,
    string? BackupId);

public sealed record PetUninstallResult(
    bool Changed,
    PetInstallationSnapshot Snapshot,
    string? BackupId);

public sealed record PetBackupInfo(
    string BackupId,
    string PetId,
    DateTimeOffset CreatedAt,
    string Reason,
    string DirectoryPath);

public sealed record PetBackupRestoreResult(
    bool Changed,
    string PetId,
    string BackupId,
    string? SafetyBackupId);

public sealed record PetStateRecoveryResult(
    bool Changed,
    string? ArchivedStatePath,
    PetManagementState State);

internal enum PetTransactionPhase
{
    Staged,
    BackupCompleted,
    ExistingMoved,
    BeforePromote,
    Promoted,
    BeforeStateSave,
    BeforeRollbackDeletePromoted,
    BeforeRollbackRestoreOriginal,
}

internal interface IPetTransactionObserver
{
    void OnPhase(PetTransactionPhase phase);
}

internal sealed class PetTransactionRollbackException(
    string message,
    IReadOnlyList<string> recoveryPaths,
    Exception innerException) : IOException(message, innerException)
{
    public IReadOnlyList<string> RecoveryPaths { get; } = recoveryPaths;
}
