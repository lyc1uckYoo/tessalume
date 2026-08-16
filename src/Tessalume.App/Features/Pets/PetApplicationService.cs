using System.IO;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Pets;

namespace Tessalume.App.Features.Pets;

internal sealed class PetApplicationService : IDisposable
{
    public const string BuiltInPetId = "flying-snowfluff";
    public const string RecommendedThemeId = "aemeath.star-voyage";
    public const string WakeCommand = "/pet";

    private readonly PortableLayout _layout;
    private readonly PetApplicationServiceOptions _options;
    private readonly PetPackageLoader _loader = new();
    private readonly PetInstaller _installer;
    private readonly object _lifetimeGate = new();
    private PetPackage? _lastPackage;
    private TaskCompletionSource? _operationsDrained;
    private int _activeOperations;
    private bool _installerDisposed;
    private bool _disposed;

    public PetApplicationService(
        PortableLayout layout,
        PetApplicationServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        _layout = layout;
        _options = options with
        {
            CodexPetsRoot = Path.GetFullPath(options.CodexPetsRoot),
            BackupRoot = Path.GetFullPath(options.BackupRoot),
            StatePath = Path.GetFullPath(options.StatePath),
        };
        _installer = new PetInstaller(new PetInstallerOptions(
            _options.CodexPetsRoot,
            _options.BackupRoot,
            _options.StatePath));
    }

    public string CodexPetsRoot => _options.CodexPetsRoot;

    public Task<PetCenterPresentationState> RefreshAsync(
        CancellationToken cancellationToken = default) =>
        RunInBackgroundAsync(
            () => RefreshCoreAsync(cancellationToken),
            cancellationToken);

    public Task<PetCenterPresentationState> InstallAsync(
        PetInstallIntent intent,
        CancellationToken cancellationToken = default) =>
        RunInBackgroundAsync(
            () => InstallCoreAsync(intent, cancellationToken),
            cancellationToken);

    public Task<PetCenterPresentationState> UninstallAsync(
        PetUninstallIntent intent,
        CancellationToken cancellationToken = default) =>
        RunInBackgroundAsync(
            () => UninstallCoreAsync(intent, cancellationToken),
            cancellationToken);

    public Task<PetCenterPresentationState> AcknowledgeCodexSelectionAsync(
        CancellationToken cancellationToken = default) =>
        RunInBackgroundAsync(
            () => AcknowledgeCodexSelectionCoreAsync(cancellationToken),
            cancellationToken);

    public Task<PetCenterPresentationState> RestoreLatestBackupAsync(
        CancellationToken cancellationToken = default) =>
        RunInBackgroundAsync(
            () => RestoreLatestBackupCoreAsync(cancellationToken),
            cancellationToken);

    public Task<PetCenterPresentationState> RecoverManagementStateAsync(
        CancellationToken cancellationToken = default) =>
        RunInBackgroundAsync(
            () => RecoverManagementStateCoreAsync(cancellationToken),
            cancellationToken);

    public Task<bool> NeedsInformationalDisclosureAsync(
        CancellationToken cancellationToken = default) =>
        RunInBackgroundAsync(
            () => NeedsInformationalDisclosureCoreAsync(cancellationToken),
            cancellationToken);

    public Task MarkInformationalDisclosureShownAsync(
        CancellationToken cancellationToken = default) =>
        RunInBackgroundAsync(
            () => _installer.MarkInformationalDisclosureShownAsync(cancellationToken),
            cancellationToken);

    /// <summary>
    /// Atomically claims the one-time theme companion suggestion. This reads only
    /// Tessalume's own schema file; it does not scan the user's Codex data.
    /// </summary>
    public Task<bool> TryClaimCompanionSuggestionAsync(
        CancellationToken cancellationToken = default) =>
        RunInBackgroundAsync(
            () => TryClaimCompanionSuggestionCoreAsync(cancellationToken),
            cancellationToken);

    public Task WaitForIdleAsync()
    {
        lock (_lifetimeGate)
        {
            return _activeOperations == 0
                ? Task.CompletedTask
                : _operationsDrained!.Task;
        }
    }

    private async Task<PetCenterPresentationState> RefreshCoreAsync(
        CancellationToken cancellationToken)
    {
        var package = await LoadBuiltInPackageAsync(cancellationToken);
        var snapshot = await _installer.InspectAsync(package, cancellationToken);
        return await BuildPresentationStateAsync(package, snapshot, cancellationToken);
    }

    private async Task<PetCenterPresentationState> InstallCoreAsync(
        PetInstallIntent intent,
        CancellationToken cancellationToken)
    {
        var package = await LoadBuiltInPackageAsync(cancellationToken);
        var result = await _installer.InstallAsync(package, intent, cancellationToken);
        return await BuildPresentationStateAsync(package, result.Snapshot, cancellationToken);
    }

    private async Task<PetCenterPresentationState> UninstallCoreAsync(
        PetUninstallIntent intent,
        CancellationToken cancellationToken)
    {
        var package = await LoadBuiltInPackageAsync(cancellationToken);
        var result = await _installer.UninstallAsync(package, intent, cancellationToken);
        return await BuildPresentationStateAsync(package, result.Snapshot, cancellationToken);
    }

    private async Task<PetCenterPresentationState> AcknowledgeCodexSelectionCoreAsync(
        CancellationToken cancellationToken)
    {
        var package = await LoadBuiltInPackageAsync(cancellationToken);
        var snapshot = await _installer.MarkCodexSelectionAcknowledgedAsync(
            package,
            cancellationToken);
        return await BuildPresentationStateAsync(package, snapshot, cancellationToken);
    }

    private async Task<PetCenterPresentationState> RestoreLatestBackupCoreAsync(
        CancellationToken cancellationToken)
    {
        var package = await LoadBuiltInPackageAsync(cancellationToken);
        var backups = await _installer.GetBackupsAsync(package.Manifest.Id, cancellationToken);
        var latest = backups.Count == 0
            ? throw new InvalidOperationException("没有可恢复的飞行雪绒备份。")
            : backups[0];
        await _installer.RestoreBackupAsync(latest.BackupId, confirmed: true, cancellationToken);
        var snapshot = await _installer.InspectAsync(package, cancellationToken);
        return await BuildPresentationStateAsync(package, snapshot, cancellationToken);
    }

    private async Task<PetCenterPresentationState> RecoverManagementStateCoreAsync(
        CancellationToken cancellationToken)
    {
        var package = await LoadBuiltInPackageAsync(cancellationToken);
        await _installer.RecoverManagementStateAsync(
            confirmed: true,
            cancellationToken);
        var snapshot = await _installer.InspectAsync(package, cancellationToken);
        return await BuildPresentationStateAsync(package, snapshot, cancellationToken);
    }

    private async Task<bool> NeedsInformationalDisclosureCoreAsync(
        CancellationToken cancellationToken)
    {
        var result = await _installer.LoadManagementStateAsync(cancellationToken);
        return !result.IsValid || !result.State.InformationalDisclosureShown;
    }

    private async Task<bool> TryClaimCompanionSuggestionCoreAsync(
        CancellationToken cancellationToken)
    {
        return await _installer.TryMarkCompanionSuggestionShownAsync(
            BuiltInPetId,
            cancellationToken);
    }

    public PetCenterPresentationState CreateLoadingState(
        PetCenterPresentationState? previous = null,
        string detail = "正在校验本地宠物文件与安装状态…") =>
        (previous ?? CreatePackageOnlyState(_lastPackage)) with
        {
            Status = PetCenterStatus.Loading,
            StatusTitle = "正在检查",
            StatusDetail = detail,
            PrimaryAction = PetCenterAction.Refresh,
            PrimaryActionText = "正在检查…",
            PrimaryActionEnabled = false,
            IsBusy = true,
        };

    public static PetCenterPresentationState CreateBusyState(
        PetCenterPresentationState previous,
        string title,
        string detail) =>
        previous with
        {
            Status = PetCenterStatus.Busy,
            StatusTitle = title,
            StatusDetail = detail,
            PrimaryActionEnabled = false,
            IsBusy = true,
        };

    public PetCenterPresentationState CreateErrorState(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return CreatePackageOnlyState(_lastPackage) with
        {
            Status = PetCenterStatus.Error,
            StatusTitle = "无法完成检查",
            StatusDetail = exception.Message,
            PrimaryAction = PetCenterAction.Refresh,
            PrimaryActionText = "重新检查",
            PrimaryActionEnabled = true,
        };
    }

    private async Task<PetPackage> LoadBuiltInPackageAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        BuiltInAssetInstaller.EnsurePetsInstalled(_layout);
        var packageRoot = Path.Combine(_layout.PetsDirectory, BuiltInPetId);
        var result = await _loader.LoadAsync(packageRoot, cancellationToken);
        if (result.Package is null || !result.Validation.IsValid)
        {
            var messages = result.Validation.Issues
                .Where(issue => issue.Severity == PetValidationSeverity.Error)
                .Select(issue => issue.Message)
                .Take(3)
                .ToArray();
            throw new InvalidDataException(messages.Length == 0
                ? "内置飞行雪绒宠物包不可用。"
                : $"内置飞行雪绒宠物包校验失败：{string.Join("；", messages)}");
        }

        _lastPackage = result.Package;
        return result.Package;
    }

    private async Task<PetCenterPresentationState> BuildPresentationStateAsync(
        PetPackage package,
        PetInstallationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var backups = await _installer.GetBackupsAsync(package.Manifest.Id, cancellationToken);
        var primary = GetPrimaryAction(snapshot);
        var latestBackup = backups.Count == 0 ? null : backups[0];
        return CreatePackageOnlyState(package) with
        {
            Status = MapStatus(snapshot.Status),
            StatusTitle = GetStatusTitle(snapshot),
            StatusDetail = snapshot.StateIsValid
                ? snapshot.Detail
                : $"Tessalume 自己的宠物管理状态无法读取；不会自动覆盖 Codex Pets。{snapshot.Detail}",
            PrimaryAction = primary.Action,
            PrimaryActionText = primary.Label,
            PrimaryActionEnabled = true,
            CanUninstall = snapshot.ManagedProductVersion is not null,
            CanAcknowledgeSelection =
                snapshot.Status == PetInstallationStatus.InstalledAwaitingCodexSelection,
            CanRestoreBackup = snapshot.StateIsValid && latestBackup is not null,
            LatestBackupLabel = latestBackup is null
                ? null
                : $"最近备份：{latestBackup.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}",
        };
    }

    private PetCenterPresentationState CreatePackageOnlyState(PetPackage? package)
    {
        if (package is null)
        {
            return new PetCenterPresentationState
            {
                InstallLocation = _options.CodexPetsRoot,
            };
        }

        var catalog = package.Catalog;
        var actionCount = Math.Max(0, catalog.Protocol.States.Count - 2);
        var directionalFrames = catalog.Protocol.States
            .Skip(actionCount)
            .Sum(state => state.Frames);
        return new PetCenterPresentationState
        {
            ProductVersion = catalog.ProductVersion,
            ProtocolSummary =
                $"图集协议 v{catalog.Protocol.SpriteVersionNumber} · " +
                $"{actionCount} 种动作 · {directionalFrames} 向转身 · " +
                $"{catalog.Protocol.UsedFrameCount} 有效格",
            Author = catalog.Author.Name,
            LicenseSummary = catalog.License.Name ?? catalog.License.Spdx ?? catalog.License.Kind,
            InstallLocation = _options.CodexPetsRoot,
            PreviewFrames = package.PreviewFiles
                .Select(preview => new PetPreviewFrame(
                    preview.Metadata.StateKey ?? Path.GetFileNameWithoutExtension(preview.FullPath),
                    preview.Metadata.Label ?? preview.Metadata.StateKey ?? "状态预览",
                    preview.FullPath))
                .ToArray(),
        };
    }

    private static (PetCenterAction Action, string Label) GetPrimaryAction(
        PetInstallationSnapshot snapshot) => !snapshot.StateIsValid
            ? (PetCenterAction.RecoverState, "归档并重建管理状态")
            : snapshot.Status switch
            {
                PetInstallationStatus.NotInstalled =>
                    (PetCenterAction.Install, "安装飞行雪绒"),
                PetInstallationStatus.Installed =>
                    (PetCenterAction.OpenCodex, "打开 Codex"),
                PetInstallationStatus.InstalledAwaitingCodexSelection =>
                    (PetCenterAction.OpenCodex, "打开 Codex 完成选择"),
                PetInstallationStatus.UpdateAvailable =>
                    (PetCenterAction.Update, "安全更新"),
                PetInstallationStatus.UnknownModification =>
                    (PetCenterAction.ReplaceModified, "处理修改并修复"),
                PetInstallationStatus.Damaged =>
                    (PetCenterAction.Repair, "修复安装"),
                PetInstallationStatus.DuplicateIdConflict =>
                    (PetCenterAction.ExplainConflict, "处理同 ID 冲突"),
                _ => (PetCenterAction.Refresh, "重新检查"),
            };

    private static PetCenterStatus MapStatus(PetInstallationStatus status) => status switch
    {
        PetInstallationStatus.NotInstalled => PetCenterStatus.NotInstalled,
        PetInstallationStatus.Installed => PetCenterStatus.Installed,
        PetInstallationStatus.InstalledAwaitingCodexSelection =>
            PetCenterStatus.AwaitingCodexSelection,
        PetInstallationStatus.UpdateAvailable => PetCenterStatus.UpdateAvailable,
        PetInstallationStatus.UnknownModification => PetCenterStatus.UnknownModification,
        PetInstallationStatus.Damaged => PetCenterStatus.Damaged,
        PetInstallationStatus.DuplicateIdConflict => PetCenterStatus.DuplicateIdConflict,
        _ => PetCenterStatus.Error,
    };

    private static string GetStatusTitle(PetInstallationSnapshot snapshot) =>
        !snapshot.StateIsValid
            ? "管理状态损坏"
            : snapshot.Status switch
            {
                PetInstallationStatus.NotInstalled => "未安装",
                PetInstallationStatus.Installed => "已安装",
                PetInstallationStatus.InstalledAwaitingCodexSelection => "等待 Codex 中选择",
                PetInstallationStatus.UpdateAvailable => "有更新",
                PetInstallationStatus.UnknownModification => "文件被修改",
                PetInstallationStatus.Damaged => "安装损坏",
                PetInstallationStatus.DuplicateIdConflict => "同 ID 冲突",
                _ => "状态未知",
            };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private Task<T> RunInBackgroundAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        BeginOperation();
        return RunTrackedOperationAsync(operation, cancellationToken);
    }

    private Task RunInBackgroundAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        BeginOperation();
        return RunTrackedOperationAsync(operation, cancellationToken);
    }

    private async Task<T> RunTrackedOperationAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await operation().ConfigureAwait(false);
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            CompleteOperation();
        }
    }

    private async Task RunTrackedOperationAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(
                async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await operation().ConfigureAwait(false);
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            CompleteOperation();
        }
    }

    private void BeginOperation()
    {
        lock (_lifetimeGate)
        {
            ThrowIfDisposed();
            if (_activeOperations == 0)
            {
                _operationsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            _activeOperations++;
        }
    }

    private void CompleteOperation()
    {
        TaskCompletionSource? drained = null;
        var disposeInstaller = false;
        lock (_lifetimeGate)
        {
            _activeOperations--;
            if (_activeOperations == 0)
            {
                drained = _operationsDrained;
                _operationsDrained = null;
                if (_disposed && !_installerDisposed)
                {
                    _installerDisposed = true;
                    disposeInstaller = true;
                }
            }
        }

        drained?.TrySetResult();
        if (disposeInstaller)
        {
            _installer.Dispose();
        }
    }

    public void Dispose()
    {
        var disposeInstaller = false;
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_activeOperations == 0 && !_installerDisposed)
            {
                _installerDisposed = true;
                disposeInstaller = true;
            }
        }

        if (disposeInstaller)
        {
            _installer.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
