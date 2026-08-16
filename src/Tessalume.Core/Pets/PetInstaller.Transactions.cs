using System.Text.Json;
using System.Runtime.ExceptionServices;

namespace Tessalume.Core.Pets;

public sealed partial class PetInstaller
{
    public async Task<PetInstallResult> InstallAsync(
        PetPackage package,
        PetInstallIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var reloaded = await _loader.LoadAsync(package.RootDirectory, cancellationToken);
            var currentPackage = reloaded.Package ?? throw new InvalidDataException(
                "内置宠物包在安装前校验失败：" +
                string.Join("；", reloaded.Validation.Issues.Select(issue => issue.Message)));
            if (!string.Equals(currentPackage.Manifest.Id, package.Manifest.Id, StringComparison.Ordinal))
            {
                throw new InvalidDataException("安装期间宠物包 ID 发生变化，已拒绝安装。");
            }

            var before = await InspectCoreAsync(currentPackage, cancellationToken);
            if (before.Status is PetInstallationStatus.Installed or
                PetInstallationStatus.InstalledAwaitingCodexSelection)
            {
                return new PetInstallResult(false, before, null);
            }
            EnsureInstallIntentMatches(before.Status, intent);
            if (!before.StateIsValid)
            {
                throw new InvalidDataException("宠物管理状态已损坏，不能安全执行覆盖操作。");
            }

            Directory.CreateDirectory(_options.PetsRoot);
            PetPathSafety.EnsureRegularDirectory(_options.PetsRoot, _options.PetsRoot);
            var stateResult = await _stateStore.LoadAsync(cancellationToken);
            if (!stateResult.IsValid)
            {
                throw new InvalidDataException(stateResult.Error ?? "宠物管理状态已损坏。");
            }
            stateResult.State.Pets.TryGetValue(currentPackage.Manifest.Id, out var previousManaged);

            var target = GetPetDirectory(currentPackage.Manifest.Id);
            if (File.Exists(target))
            {
                throw new InvalidDataException("宠物目标路径被文件占用，无法安全备份和替换。");
            }
            var displaced = before.InstalledDirectories
                .Append(target)
                .Select(Path.GetFullPath)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var staging = Path.Combine(_options.PetsRoot, $".tessalume-stage-{Guid.NewGuid():N}");
            var moved = new List<MovedDirectory>();
            var promoted = new List<string>();
            string? backupId = null;
            try
            {
                await StagePackageAsync(currentPackage, staging, cancellationToken);
                _observer?.OnPhase(PetTransactionPhase.Staged);

                foreach (var existing in displaced)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PetPathSafety.EnsureRegularDirectory(_options.PetsRoot, existing);
                    var rollback = Path.Combine(
                        _options.PetsRoot,
                        $".tessalume-rollback-{Guid.NewGuid():N}");
                    Directory.Move(existing, rollback);
                    moved.Add(new MovedDirectory(existing, rollback));
                }
                _observer?.OnPhase(PetTransactionPhase.ExistingMoved);
                await EnsureMovedInstallIntentStillMatchesAsync(
                    intent,
                    previousManaged,
                    moved,
                    cancellationToken);
                var backupSources = moved
                    .Select(item => new PetBackupSource(
                        Path.GetFileName(item.OriginalPath),
                        item.RollbackPath))
                    .ToArray();
                var backup = await CreateBackupAsync(
                    currentPackage.Manifest.Id,
                    intent.ToString(),
                    backupSources,
                    previousManaged,
                    cancellationToken);
                backupId = backup?.BackupId;
                _observer?.OnPhase(PetTransactionPhase.BackupCompleted);
                _observer?.OnPhase(PetTransactionPhase.BeforePromote);
                if (backup is not null)
                {
                    await EnsureBackupMatchesSourcesAsync(backup, backupSources, cancellationToken);
                }
                await EnsureMovedInstallIntentStillMatchesAsync(
                    intent,
                    previousManaged,
                    moved,
                    cancellationToken);
                Directory.Move(staging, target);
                promoted.Add(target);
                _observer?.OnPhase(PetTransactionPhase.Promoted);

                var now = DateTimeOffset.UtcNow;
                var managedFiles = currentPackage.InstallFiles
                    .Select(file => new PetManagedFile
                    {
                        RelativePath = file.Path,
                        Sha256 = file.Sha256.ToUpperInvariant(),
                        Size = file.Size,
                    })
                    .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var managed = new PetManagedInstallation
                {
                    Id = currentPackage.Manifest.Id,
                    ProductVersion = currentPackage.Catalog.ProductVersion,
                    DirectoryName = currentPackage.Manifest.Id,
                    Files = managedFiles,
                    InstalledAt = previousManaged?.InstalledAt ?? now,
                    UpdatedAt = now,
                    SelectionAcknowledged = false,
                };
                _observer?.OnPhase(PetTransactionPhase.BeforeStateSave);
                await _stateStore.MutateAsync(
                    state =>
                    {
                        var pets = state.Pets.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value,
                            StringComparer.OrdinalIgnoreCase);
                        pets[currentPackage.Manifest.Id] = managed;
                        return state with { Pets = pets };
                    },
                    cancellationToken);
            }
            catch (Exception operationException)
            {
                RethrowAfterDirectoryRollback(
                    operationException,
                    promoted,
                    [staging],
                    moved,
                    backupId);
                throw new InvalidOperationException("Unreachable rollback continuation.");
            }

            foreach (var item in moved)
            {
                TryDeleteDirectory(item.RollbackPath);
            }
            var after = await InspectCoreAsync(currentPackage, cancellationToken);
            return new PetInstallResult(true, after, backupId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PetUninstallResult> UninstallAsync(
        PetPackage package,
        PetUninstallIntent intent = PetUninstallIntent.Safe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stateResult = await _stateStore.LoadAsync(cancellationToken);
            if (!stateResult.IsValid)
            {
                throw new InvalidDataException(stateResult.Error ?? "宠物管理状态已损坏。");
            }
            if (!stateResult.State.Pets.TryGetValue(package.Manifest.Id, out var managed))
            {
                return new PetUninstallResult(
                    false,
                    await InspectCoreAsync(package, cancellationToken),
                    null);
            }

            var directory = GetPetDirectory(managed.DirectoryName);
            if (!Directory.Exists(directory))
            {
                await RemoveManagedStateAsync(package.Manifest.Id, cancellationToken);
                return new PetUninstallResult(
                    true,
                    await InspectCoreAsync(package, cancellationToken),
                    null);
            }
            if (await HasModifiedManagedFilesAsync(directory, managed, cancellationToken) &&
                intent != PetUninstallIntent.RemoveModifiedManagedFilesConfirmed)
            {
                throw new InvalidOperationException("受管宠物文件已被修改，需要明确确认后才能卸载。");
            }

            var backup = await CreateBackupAsync(
                package.Manifest.Id,
                "uninstall",
                [new PetBackupSource(Path.GetFileName(directory), directory)],
                managed,
                cancellationToken);
            _observer?.OnPhase(PetTransactionPhase.BackupCompleted);
            var rollback = Path.Combine(
                _options.PetsRoot,
                $".tessalume-uninstall-rollback-{Guid.NewGuid():N}");
            var movedFiles = new List<MovedFile>();
            try
            {
                Directory.CreateDirectory(rollback);
                foreach (var file in managed.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = PetPathSafety.ResolveContainedPath(directory, file.RelativePath);
                    if (!File.Exists(source)) continue;
                    PetPathSafety.EnsureRegularFile(directory, source);
                    var rollbackPath = PetPathSafety.ResolveContainedPath(rollback, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(rollbackPath)!);
                    File.Move(source, rollbackPath, overwrite: false);
                    movedFiles.Add(new MovedFile(source, rollbackPath));
                }
                _observer?.OnPhase(PetTransactionPhase.ExistingMoved);
                if (intent == PetUninstallIntent.Safe)
                {
                    await EnsureSafeUninstallIntentStillMatchesAsync(
                        directory,
                        rollback,
                        movedFiles,
                        managed,
                        cancellationToken);
                }
                _observer?.OnPhase(PetTransactionPhase.BeforeStateSave);
                if (backup is not null)
                {
                    await EnsureBackupMatchesUninstallSnapshotAsync(
                        backup,
                        directory,
                        rollback,
                        movedFiles,
                        cancellationToken);
                }
                if (intent == PetUninstallIntent.Safe)
                {
                    await EnsureSafeUninstallIntentStillMatchesAsync(
                        directory,
                        rollback,
                        movedFiles,
                        managed,
                        cancellationToken);
                }
                await RemoveManagedStateAsync(package.Manifest.Id, cancellationToken);
            }
            catch (Exception operationException)
            {
                var rollbackFailures = RestoreMovedFiles(movedFiles);
                if (rollbackFailures.Count > 0)
                {
                    throw CreateRollbackException(
                        "卸载失败，且部分受管文件无法放回原位；原文件仍保留在恢复目录或持久备份中。",
                        operationException,
                        rollbackFailures,
                        [rollback, GetBackupPath(backup?.BackupId)]);
                }
                TryDeleteDirectory(rollback);
                ExceptionDispatchInfo.Capture(operationException).Throw();
                throw new InvalidOperationException("Unreachable rollback continuation.");
            }
            TryDeleteDirectory(rollback);
            PruneEmptyManagedDirectories(directory, movedFiles.Select(item => item.OriginalPath));
            return new PetUninstallResult(
                true,
                await InspectCoreAsync(package, cancellationToken),
                backup?.BackupId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void EnsureInstallIntentMatches(
        PetInstallationStatus status,
        PetInstallIntent intent)
    {
        var allowed = status switch
        {
            PetInstallationStatus.NotInstalled => intent == PetInstallIntent.Install,
            PetInstallationStatus.UpdateAvailable => intent == PetInstallIntent.UpdateConfirmed,
            PetInstallationStatus.Damaged => intent == PetInstallIntent.RepairConfirmed,
            PetInstallationStatus.UnknownModification or PetInstallationStatus.DuplicateIdConflict =>
                intent == PetInstallIntent.ReplaceConfirmed,
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidOperationException($"状态 {status} 需要对应的明确安装意图，已拒绝操作。");
        }
    }

    private static async Task StagePackageAsync(
        PetPackage package,
        string staging,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(staging);
        foreach (var file in package.InstallFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = package.ResolvedFiles[file.Path];
            var destination = PetPathSafety.ResolveContainedPath(staging, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
            if (new FileInfo(destination).Length != file.Size ||
                !string.Equals(
                    await PetPackageLoader.ComputeSha256Async(destination, cancellationToken),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("宠物 staging 文件复制校验失败。");
            }
        }

        var stagedFiles = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(staging, path).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expected = package.InstallFiles
            .Select(file => file.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!stagedFiles.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("宠物 staging 包含未声明文件或缺少运行文件。");
        }

        var manifestPath = Path.Combine(staging, PetPackageContract.ManifestFileName);
        var stagedManifest = JsonSerializer.Deserialize<PetManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken),
            InstalledJsonOptions) ?? throw new InvalidDataException("staging pet.json 无效。");
        if (!string.Equals(stagedManifest.Id, package.Manifest.Id, StringComparison.Ordinal) ||
            !string.Equals(stagedManifest.SpritesheetPath, package.Manifest.SpritesheetPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("staging pet.json 与已验证宠物包不一致。");
        }
        var sheetPath = PetPathSafety.ResolveContainedPath(staging, stagedManifest.SpritesheetPath);
        var webp = await PetWebPReader.ReadAsync(sheetPath, cancellationToken);
        if (webp.Width != package.Catalog.Protocol.AtlasWidth ||
            webp.Height != package.Catalog.Protocol.AtlasHeight || !webp.HasAlpha)
        {
            throw new InvalidDataException("staging WebP 图集协议校验失败。");
        }
    }

    private static async Task<bool> HasModifiedManagedFilesAsync(
        string directory,
        PetManagedInstallation managed,
        CancellationToken cancellationToken)
    {
        foreach (var file in managed.Files)
        {
            var path = PetPathSafety.ResolveContainedPath(directory, file.RelativePath);
            if (!File.Exists(path)) continue;
            PetPathSafety.EnsureRegularFile(directory, path);
            if (new FileInfo(path).Length != file.Size ||
                !string.Equals(
                    await PetPackageLoader.ComputeSha256Async(path, cancellationToken),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private async Task EnsureMovedInstallIntentStillMatchesAsync(
        PetInstallIntent intent,
        PetManagedInstallation? previousManaged,
        IReadOnlyCollection<MovedDirectory> moved,
        CancellationToken cancellationToken)
    {
        if (intent is PetInstallIntent.Install or PetInstallIntent.ReplaceConfirmed)
        {
            return;
        }
        if (previousManaged is null)
        {
            throw new InvalidOperationException(
                "安装状态在操作期间发生变化，需要重新检查后再确认。");
        }

        var managedOriginalPath = GetPetDirectory(previousManaged.DirectoryName);
        var managedMove = moved.SingleOrDefault(item => string.Equals(
            item.OriginalPath,
            managedOriginalPath,
            StringComparison.OrdinalIgnoreCase));
        if (managedMove is null)
        {
            if (intent == PetInstallIntent.RepairConfirmed)
            {
                return;
            }
            throw new InvalidOperationException(
                "受管宠物目录在更新前消失，需要重新检查后再确认。");
        }

        var snapshot = await SnapshotDirectoryFilesAsync(
            managedMove.RollbackPath,
            cancellationToken);
        if (intent == PetInstallIntent.UpdateConfirmed)
        {
            EnsureSnapshotExactlyMatchesManaged(snapshot, previousManaged, "更新");
            return;
        }

        await EnsureSnapshotIsStillRepairOnlyAsync(
            managedMove.RollbackPath,
            snapshot,
            previousManaged,
            cancellationToken);
    }

    private static void EnsureSnapshotExactlyMatchesManaged(
        IReadOnlyDictionary<string, PetBackupFileSnapshot> snapshot,
        PetManagedInstallation managed,
        string operationName)
    {
        if (snapshot.Count != managed.Files.Count)
        {
            throw new InvalidOperationException(
                $"宠物目录在{operationName}确认后出现缺失或未知文件，需要重新检查并选择正确操作。");
        }
        foreach (var expected in managed.Files)
        {
            if (!snapshot.TryGetValue(expected.RelativePath, out var actual) ||
                actual.Size != expected.Size ||
                !string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"受管文件在{operationName}确认后发生变化：{expected.RelativePath}。请重新检查并明确处理修改。");
            }
        }
    }

    private static async Task EnsureSnapshotIsStillRepairOnlyAsync(
        string directory,
        IReadOnlyDictionary<string, PetBackupFileSnapshot> snapshot,
        PetManagedInstallation managed,
        CancellationToken cancellationToken)
    {
        var managedByPath = managed.Files.ToDictionary(
            file => file.RelativePath,
            file => file,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (relativePath, actual) in snapshot)
        {
            if (!managedByPath.TryGetValue(relativePath, out var expected))
            {
                throw new InvalidOperationException(
                    $"修复确认后发现未知文件：{relativePath}。请重新检查并明确处理修改。");
            }
            if (actual.Size == expected.Size &&
                string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!string.Equals(
                    relativePath,
                    PetPackageContract.ManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"修复确认后发现受管文件被修改：{relativePath}。请重新检查并明确处理修改。");
            }

            var manifestPath = PetPathSafety.ResolveContainedPath(directory, relativePath);
            try
            {
                await using var stream = File.OpenRead(manifestPath);
                var manifest = await JsonSerializer.DeserializeAsync<PetManifest>(
                    stream,
                    InstalledJsonOptions,
                    cancellationToken);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    continue;
                }
            }
            catch (JsonException)
            {
                continue;
            }

            throw new InvalidOperationException(
                "修复确认后 pet.json 已变为可识别的不同内容，需要重新检查并明确处理修改。");
        }
    }

    private static async Task EnsureSafeUninstallIntentStillMatchesAsync(
        string originalDirectory,
        string rollbackDirectory,
        IReadOnlyCollection<MovedFile> movedFiles,
        PetManagedInstallation managed,
        CancellationToken cancellationToken)
    {
        var managedByPath = managed.Files.ToDictionary(
            file => file.RelativePath,
            file => file,
            StringComparer.OrdinalIgnoreCase);
        foreach (var moved in movedFiles)
        {
            var relativePath = Path.GetRelativePath(originalDirectory, moved.OriginalPath)
                .Replace('\\', '/');
            if (!managedByPath.TryGetValue(relativePath, out var expected))
            {
                throw new InvalidDataException("卸载移动了未登记的宠物文件，已中止操作。");
            }
            PetPathSafety.EnsureRegularFile(rollbackDirectory, moved.RollbackPath);
            var info = new FileInfo(moved.RollbackPath);
            var hash = await PetPackageLoader.ComputeSha256Async(
                moved.RollbackPath,
                cancellationToken);
            if (info.Length != expected.Size ||
                !string.Equals(hash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"受管文件在安全卸载确认后发生变化：{relativePath}。请重新检查并确认卸载修改文件。");
            }
        }
    }

    private Task<PetManagementState> RemoveManagedStateAsync(
        string petId,
        CancellationToken cancellationToken) =>
        _stateStore.MutateAsync(
            state =>
            {
                var pets = state.Pets.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
                pets.Remove(petId);
                return state with { Pets = pets };
            },
            cancellationToken);

    private void RethrowAfterDirectoryRollback(
        Exception operationException,
        IReadOnlyList<string> promotedTargets,
        IReadOnlyList<string> stagingPaths,
        IReadOnlyList<MovedDirectory> moved,
        string? backupId)
    {
        var rollbackFailures = new List<Exception>();
        foreach (var target in promotedTargets.Reverse())
        {
            if (!Directory.Exists(target)) continue;
            TryRollbackStep(
                PetTransactionPhase.BeforeRollbackDeletePromoted,
                () => DeleteDirectoryStrict(target),
                rollbackFailures);
        }
        foreach (var staging in stagingPaths)
        {
            if (!Directory.Exists(staging)) continue;
            try
            {
                DeleteDirectoryStrict(staging);
            }
            catch (Exception exception)
            {
                rollbackFailures.Add(exception);
            }
        }
        foreach (var item in moved.Reverse())
        {
            if (Directory.Exists(item.OriginalPath))
            {
                if (Directory.Exists(item.RollbackPath))
                {
                    rollbackFailures.Add(new IOException(
                        $"回滚时原目录与恢复目录同时存在：{item.OriginalPath}"));
                }
                continue;
            }
            if (!Directory.Exists(item.RollbackPath))
            {
                rollbackFailures.Add(new DirectoryNotFoundException(
                    $"回滚恢复目录已经丢失：{item.RollbackPath}"));
                continue;
            }
            TryRollbackStep(
                PetTransactionPhase.BeforeRollbackRestoreOriginal,
                () => Directory.Move(item.RollbackPath, item.OriginalPath),
                rollbackFailures);
        }

        if (rollbackFailures.Count > 0)
        {
            throw CreateRollbackException(
                "宠物操作失败，且无法完整恢复原目录；旧目录与持久备份均未被静默删除。",
                operationException,
                rollbackFailures,
                moved.Select(item => item.RollbackPath).Append(GetBackupPath(backupId)));
        }
        ExceptionDispatchInfo.Capture(operationException).Throw();
    }

    private List<Exception> RestoreMovedFiles(IReadOnlyList<MovedFile> movedFiles)
    {
        var rollbackFailures = new List<Exception>();
        foreach (var item in movedFiles.Reverse())
        {
            if (File.Exists(item.OriginalPath))
            {
                rollbackFailures.Add(new IOException($"受管文件原路径已被重新占用：{item.OriginalPath}"));
                continue;
            }
            if (!File.Exists(item.RollbackPath))
            {
                rollbackFailures.Add(new FileNotFoundException("卸载回滚文件已经丢失。", item.RollbackPath));
                continue;
            }
            TryRollbackStep(
                PetTransactionPhase.BeforeRollbackRestoreOriginal,
                () =>
                {
                    var parent = Path.GetDirectoryName(item.OriginalPath)!;
                    Directory.CreateDirectory(parent);
                    PetPathSafety.EnsureNoReparsePoints(_options.PetsRoot, parent);
                    File.Move(item.RollbackPath, item.OriginalPath, overwrite: false);
                },
                rollbackFailures);
        }
        return rollbackFailures;
    }

    private void TryRollbackStep(
        PetTransactionPhase phase,
        Action action,
        List<Exception> failures)
    {
        try
        {
            _observer?.OnPhase(phase);
            action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static PetTransactionRollbackException CreateRollbackException(
        string message,
        Exception operationException,
        IEnumerable<Exception> rollbackFailures,
        IEnumerable<string?> recoveryPaths)
    {
        var existingRecoveryPaths = recoveryPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Where(path => Directory.Exists(path) || File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var failures = new[] { operationException }.Concat(rollbackFailures).ToArray();
        return new PetTransactionRollbackException(
            message + (existingRecoveryPaths.Length == 0
                ? string.Empty
                : $" 恢复材料：{string.Join("；", existingRecoveryPaths)}"),
            existingRecoveryPaths,
            new AggregateException(failures));
    }

    private string? GetBackupPath(string? backupId) =>
        string.IsNullOrWhiteSpace(backupId)
            ? null
            : Path.Combine(_options.BackupRoot, backupId);

    private static void PruneEmptyManagedDirectories(
        string petDirectory,
        IEnumerable<string> managedFilePaths)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        candidates.Add(petDirectory);
        foreach (var filePath in managedFilePaths)
        {
            var current = Path.GetDirectoryName(filePath);
            while (current is not null &&
                   PetPathSafety.IsContainedOrEqual(petDirectory, current))
            {
                candidates.Add(current);
                if (string.Equals(current, petDirectory, StringComparison.OrdinalIgnoreCase)) break;
                current = Path.GetDirectoryName(current);
            }
        }
        foreach (var directory in candidates.OrderByDescending(path => path.Length))
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, recursive: false);
            }
        }
    }

    private void DeleteDirectoryStrict(string path)
    {
        PetPathSafety.EnsureContained(_options.PetsRoot, path);
        var isReparsePoint = (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        Directory.Delete(path, recursive: !isReparsePoint);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            var isReparsePoint = (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            Directory.Delete(path, recursive: !isReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private sealed record MovedDirectory(string OriginalPath, string RollbackPath);

    private sealed record MovedFile(string OriginalPath, string RollbackPath);
}
