using System.Text.Json;

namespace Tessalume.Core.Pets;

public sealed partial class PetInstaller
{
    public async Task<PetBackupRestoreResult> RestoreBackupAsync(
        string backupId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupId);
        if (!confirmed)
        {
            throw new InvalidOperationException("恢复宠物备份需要明确确认。");
        }
        if (!PetPathSafety.IsSimpleDirectoryName(backupId))
        {
            throw new ArgumentException("宠物备份 ID 无效。", nameof(backupId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var backupDirectory = Path.GetFullPath(Path.Combine(_options.BackupRoot, backupId));
            PetPathSafety.EnsureContained(_options.BackupRoot, backupDirectory);
            var backup = await TryLoadBackupAsync(
                backupDirectory,
                validateFiles: true,
                cancellationToken) ?? throw new InvalidDataException("宠物备份已损坏或不完整。");

            var stateResult = await _stateStore.LoadAsync(cancellationToken);
            if (!stateResult.IsValid)
            {
                throw new InvalidDataException(stateResult.Error ?? "宠物管理状态已损坏。");
            }
            stateResult.State.Pets.TryGetValue(backup.PetId, out var currentManaged);
            Directory.CreateDirectory(_options.PetsRoot);
            PetPathSafety.EnsureRegularDirectory(_options.PetsRoot, _options.PetsRoot);

            var restoreTargets = backup.Directories
                .Select(item => GetPetDirectory(item.OriginalDirectoryName))
                .ToArray();
            foreach (var target in restoreTargets)
            {
                await EnsureRestoreTargetIsSafeAsync(
                    target,
                    backup.PetId,
                    cancellationToken);
            }

            var probes = await ProbeInstalledPetsAsync(backup.PetId, cancellationToken);
            var displaced = probes
                .Where(probe => string.Equals(probe.Manifest?.Id, backup.PetId, StringComparison.OrdinalIgnoreCase))
                .Select(probe => probe.DirectoryPath)
                .Concat(currentManaged is null
                    ? []
                    : [GetPetDirectory(currentManaged.DirectoryName)])
                .Concat(restoreTargets)
                .Select(Path.GetFullPath)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PetBackupManifest? safetyBackup = null;
            var stages = new List<RestoreStage>();
            var moved = new List<MovedDirectory>();
            var promoted = new List<string>();
            try
            {
                foreach (var item in backup.Directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var staging = Path.Combine(
                        _options.PetsRoot,
                        $".tessalume-stage-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(staging);
                    foreach (var file in item.Files)
                    {
                        var source = PetPathSafety.ResolveContainedPath(backupDirectory, file.StoredPath);
                        var destination = PetPathSafety.ResolveContainedPath(staging, file.RelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(source, destination, overwrite: false);
                        if (new FileInfo(destination).Length != file.Size ||
                            !string.Equals(
                                await PetPackageLoader.ComputeSha256Async(destination, cancellationToken),
                                file.Sha256,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("宠物备份 staging 校验失败。");
                        }
                    }
                    stages.Add(new RestoreStage(staging, GetPetDirectory(item.OriginalDirectoryName)));
                }
                _observer?.OnPhase(PetTransactionPhase.Staged);

                foreach (var existing in displaced)
                {
                    var rollback = Path.Combine(
                        _options.PetsRoot,
                        $".tessalume-rollback-{Guid.NewGuid():N}");
                    Directory.Move(existing, rollback);
                    moved.Add(new MovedDirectory(existing, rollback));
                }
                _observer?.OnPhase(PetTransactionPhase.ExistingMoved);
                foreach (var item in moved)
                {
                    await EnsureRestoreTargetIsSafeAsync(
                        item.RollbackPath,
                        backup.PetId,
                        cancellationToken);
                }
                var safetyBackupSources = moved
                    .Select(item => new PetBackupSource(
                        Path.GetFileName(item.OriginalPath),
                        item.RollbackPath))
                    .ToArray();
                safetyBackup = await CreateBackupAsync(
                    backup.PetId,
                    $"before-restore-{backup.BackupId}",
                    safetyBackupSources,
                    currentManaged,
                    cancellationToken);
                _observer?.OnPhase(PetTransactionPhase.BackupCompleted);
                _observer?.OnPhase(PetTransactionPhase.BeforePromote);
                if (safetyBackup is not null)
                {
                    await EnsureBackupMatchesSourcesAsync(
                        safetyBackup,
                        safetyBackupSources,
                        cancellationToken);
                }
                foreach (var item in moved)
                {
                    await EnsureRestoreTargetIsSafeAsync(
                        item.RollbackPath,
                        backup.PetId,
                        cancellationToken);
                }
                foreach (var stage in stages)
                {
                    Directory.Move(stage.StagingPath, stage.TargetPath);
                    promoted.Add(stage.TargetPath);
                }
                _observer?.OnPhase(PetTransactionPhase.Promoted);

                var restoredManaged = backup.ManagedState is null
                    ? null
                    : backup.ManagedState with { SelectionAcknowledged = false };
                _observer?.OnPhase(PetTransactionPhase.BeforeStateSave);
                await _stateStore.MutateAsync(
                    state =>
                    {
                        var pets = state.Pets.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value,
                            StringComparer.OrdinalIgnoreCase);
                        if (restoredManaged is null) pets.Remove(backup.PetId);
                        else pets[backup.PetId] = restoredManaged;
                        return state with { Pets = pets };
                    },
                    cancellationToken);
            }
            catch (Exception operationException)
            {
                RethrowAfterDirectoryRollback(
                    operationException,
                    promoted,
                    stages.Select(stage => stage.StagingPath).ToArray(),
                    moved,
                    safetyBackup?.BackupId);
                throw new InvalidOperationException("Unreachable rollback continuation.");
            }

            foreach (var item in moved) TryDeleteDirectory(item.RollbackPath);
            return new PetBackupRestoreResult(
                true,
                backup.PetId,
                backup.BackupId,
                safetyBackup?.BackupId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureRestoreTargetIsSafeAsync(
        string target,
        string petId,
        CancellationToken cancellationToken)
    {
        if (File.Exists(target))
        {
            throw new InvalidDataException("备份恢复目标被文件占用，无法安全替换。");
        }
        if (!Directory.Exists(target))
        {
            return;
        }

        PetPathSafety.EnsureRegularDirectory(_options.PetsRoot, target);
        var manifestPath = Path.Combine(target, PetPackageContract.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return;
        }

        PetPathSafety.EnsureRegularFile(target, manifestPath);
        if (new FileInfo(manifestPath).Length is <= 0 or > MaximumInstalledManifestBytes)
        {
            return;
        }
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<PetManifest>(
                stream,
                InstalledJsonOptions,
                cancellationToken);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                return;
            }
            if (!string.Equals(manifest.Id, petId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"备份原目录现在属于其他宠物 ID（{manifest.Id}），已拒绝覆盖。");
            }
        }
        catch (JsonException exception)
        {
            _ = exception;
            return;
        }
    }

    private sealed record RestoreStage(string StagingPath, string TargetPath);
}
