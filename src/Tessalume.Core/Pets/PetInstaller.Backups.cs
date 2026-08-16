using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tessalume.Core.Pets;

public sealed partial class PetInstaller
{
    private const int BackupSchemaVersion = 1;
    private const int MaximumBackupFiles = 2048;
    private const long MaximumBackupBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions BackupJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<IReadOnlyList<PetBackupInfo>> GetBackupsAsync(
        string petId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_options.BackupRoot)) return [];
            PetPathSafety.EnsureRegularDirectory(_options.BackupRoot, _options.BackupRoot);
            var backups = new List<PetBackupInfo>();
            foreach (var directory in Directory.EnumerateDirectories(
                         _options.BackupRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFileName(directory).StartsWith(".partial-", StringComparison.OrdinalIgnoreCase)) continue;
                var loaded = await TryLoadBackupAsync(directory, validateFiles: false, cancellationToken);
                if (loaded is not null && string.Equals(loaded.PetId, petId, StringComparison.OrdinalIgnoreCase))
                {
                    backups.Add(new PetBackupInfo(
                        loaded.BackupId,
                        loaded.PetId,
                        loaded.CreatedAt,
                        loaded.Reason,
                        directory));
                }
            }
            return backups.OrderByDescending(backup => backup.CreatedAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PetBackupManifest?> CreateBackupAsync(
        string petId,
        string reason,
        IReadOnlyCollection<PetBackupSource> sourceDirectories,
        PetManagedInstallation? managedState,
        CancellationToken cancellationToken)
    {
        if (!PetPathSafety.IsValidPetId(petId))
        {
            throw new InvalidDataException("备份宠物 ID 格式无效。");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var existing = sourceDirectories
            .Where(source => Directory.Exists(source.SourcePath))
            .Select(source => source with { SourcePath = Path.GetFullPath(source.SourcePath) })
            .DistinctBy(source => source.OriginalDirectoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (existing.Length == 0) return null;

        Directory.CreateDirectory(_options.BackupRoot);
        PetPathSafety.EnsureRegularDirectory(_options.BackupRoot, _options.BackupRoot);
        var backupId = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{petId}-{Guid.NewGuid():N}";
        if (!PetPathSafety.IsSimpleDirectoryName(backupId))
        {
            throw new InvalidDataException("生成的宠物备份 ID 无效。");
        }
        var partial = Path.Combine(_options.BackupRoot, $".partial-{Guid.NewGuid():N}");
        var destination = Path.Combine(_options.BackupRoot, backupId);
        PetPathSafety.EnsureContained(_options.BackupRoot, partial);
        PetPathSafety.EnsureContained(_options.BackupRoot, destination);
        Directory.CreateDirectory(partial);
        try
        {
            var backedUpDirectories = new List<PetBackupDirectory>();
            var fileCount = 0;
            long totalBytes = 0;
            for (var index = 0; index < existing.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceEntry = existing[index];
                var source = sourceEntry.SourcePath;
                PetPathSafety.EnsureRegularDirectory(_options.PetsRoot, source);
                var originalName = sourceEntry.OriginalDirectoryName;
                if (!PetPathSafety.IsSimpleDirectoryName(originalName))
                {
                    throw new InvalidDataException("备份源宠物目录名无效。");
                }
                var storedRoot = $"directories/{index:D2}";
                var storedDirectory = PetPathSafety.ResolveContainedPath(partial, storedRoot);
                Directory.CreateDirectory(storedDirectory);
                var files = new List<PetBackupFile>();
                foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                {
                    PetPathSafety.EnsureRegularDirectory(source, directory);
                }
                foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PetPathSafety.EnsureRegularFile(source, sourceFile);
                    fileCount++;
                    var size = new FileInfo(sourceFile).Length;
                    totalBytes += size;
                    if (fileCount > MaximumBackupFiles || totalBytes > MaximumBackupBytes)
                    {
                        throw new InvalidDataException("宠物备份超过 2048 个文件或 512 MiB 安全限制。");
                    }
                    var relative = Path.GetRelativePath(source, sourceFile).Replace('\\', '/');
                    var storedPath = $"{storedRoot}/{relative}";
                    var target = PetPathSafety.ResolveContainedPath(partial, storedPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(sourceFile, target, overwrite: false);
                    var sourceHash = await PetPackageLoader.ComputeSha256Async(sourceFile, cancellationToken);
                    var copiedHash = await PetPackageLoader.ComputeSha256Async(target, cancellationToken);
                    if (!string.Equals(sourceHash, copiedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("宠物备份复制后的 SHA-256 不一致。");
                    }
                    files.Add(new PetBackupFile(relative, storedPath, size, sourceHash));
                }
                backedUpDirectories.Add(new PetBackupDirectory(originalName, storedRoot, files));
            }

            var manifest = new PetBackupManifest(
                BackupSchemaVersion,
                backupId,
                petId,
                DateTimeOffset.UtcNow,
                reason,
                managedState,
                backedUpDirectories);
            await File.WriteAllTextAsync(
                Path.Combine(partial, "backup.json"),
                JsonSerializer.Serialize(manifest, BackupJsonOptions),
                cancellationToken);
            Directory.Move(partial, destination);
            return manifest;
        }
        catch
        {
            TryDeleteDirectory(partial);
            throw;
        }
    }

    private async Task EnsureBackupMatchesSourcesAsync(
        PetBackupManifest backup,
        IReadOnlyCollection<PetBackupSource> sources,
        CancellationToken cancellationToken)
    {
        var backupDirectory = GetBackupPath(backup.BackupId) ??
                              throw new InvalidDataException("宠物备份路径无效。");
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backupDirectoryEntry = backup.Directories.SingleOrDefault(item =>
                string.Equals(
                    item.OriginalDirectoryName,
                    source.OriginalDirectoryName,
                    StringComparison.OrdinalIgnoreCase)) ??
                throw new InvalidDataException("宠物备份缺少源目录记录。");
            var actualFiles = await SnapshotDirectoryFilesAsync(source.SourcePath, cancellationToken);
            if (actualFiles.Count != backupDirectoryEntry.Files.Count)
            {
                throw new IOException("宠物目录在持久备份完成后发生变化，已中止替换。");
            }
            foreach (var expected in backupDirectoryEntry.Files)
            {
                if (!actualFiles.TryGetValue(expected.RelativePath, out var actual) ||
                    actual.Size != expected.Size ||
                    !string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(
                        $"宠物文件在持久备份完成后发生变化：{expected.RelativePath}");
                }
                var backedUpPath = PetPathSafety.ResolveContainedPath(backupDirectory, expected.StoredPath);
                PetPathSafety.EnsureRegularFile(backupDirectory, backedUpPath);
            }
        }
    }

    private static async Task EnsureBackupMatchesUninstallSnapshotAsync(
        PetBackupManifest backup,
        string originalDirectory,
        string rollbackDirectory,
        IReadOnlyCollection<MovedFile> movedFiles,
        CancellationToken cancellationToken)
    {
        var originalName = Path.GetFileName(originalDirectory);
        var backupDirectoryEntry = backup.Directories.Single(item =>
            string.Equals(item.OriginalDirectoryName, originalName, StringComparison.OrdinalIgnoreCase));
        var movedByRelativePath = movedFiles.ToDictionary(
            item => Path.GetRelativePath(originalDirectory, item.OriginalPath).Replace('\\', '/'),
            item => item.RollbackPath,
            StringComparer.OrdinalIgnoreCase);
        var currentFiles = Directory.Exists(originalDirectory)
            ? await SnapshotDirectoryFilesAsync(originalDirectory, cancellationToken)
            : new Dictionary<string, PetBackupFileSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var (relativePath, rollbackPath) in movedByRelativePath)
        {
            PetPathSafety.EnsureRegularFile(rollbackDirectory, rollbackPath);
            currentFiles[relativePath] = new PetBackupFileSnapshot(
                new FileInfo(rollbackPath).Length,
                await PetPackageLoader.ComputeSha256Async(rollbackPath, cancellationToken));
        }
        if (currentFiles.Count != backupDirectoryEntry.Files.Count)
        {
            throw new IOException("宠物目录在卸载备份完成后发生变化，已中止卸载。");
        }
        foreach (var expected in backupDirectoryEntry.Files)
        {
            if (!currentFiles.TryGetValue(expected.RelativePath, out var actual) ||
                actual.Size != expected.Size ||
                !string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"宠物文件在卸载备份完成后发生变化：{expected.RelativePath}");
            }
        }
    }

    private static async Task<Dictionary<string, PetBackupFileSnapshot>> SnapshotDirectoryFilesAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        PetPathSafety.EnsureRegularDirectory(directory, directory);
        foreach (var childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            PetPathSafety.EnsureRegularDirectory(directory, childDirectory);
        }
        var snapshot = new Dictionary<string, PetBackupFileSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PetPathSafety.EnsureRegularFile(directory, file);
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            snapshot.Add(relative, new PetBackupFileSnapshot(
                new FileInfo(file).Length,
                await PetPackageLoader.ComputeSha256Async(file, cancellationToken)));
        }
        return snapshot;
    }

    private async Task<PetBackupManifest?> TryLoadBackupAsync(
        string directory,
        bool validateFiles,
        CancellationToken cancellationToken)
    {
        try
        {
            PetPathSafety.EnsureRegularDirectory(_options.BackupRoot, directory);
            var manifestPath = Path.Combine(directory, "backup.json");
            PetPathSafety.EnsureRegularFile(directory, manifestPath);
            if (new FileInfo(manifestPath).Length is <= 0 or > 1024 * 1024) return null;
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<PetBackupManifest>(
                stream,
                BackupJsonOptions,
                cancellationToken);
            if (manifest is null || manifest.SchemaVersion != BackupSchemaVersion ||
                !string.Equals(manifest.BackupId, Path.GetFileName(directory), StringComparison.Ordinal) ||
                !PetPathSafety.IsSimpleDirectoryName(manifest.BackupId) ||
                !PetPathSafety.IsValidPetId(manifest.PetId) || string.IsNullOrWhiteSpace(manifest.Reason) ||
                manifest.CreatedAt == default || manifest.Directories is null ||
                manifest.Directories.Count == 0)
            {
                return null;
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var storedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in manifest.Directories)
            {
                if (item is null || item.Files is null ||
                    !PetPathSafety.IsSimpleDirectoryName(item.OriginalDirectoryName) ||
                    !PetPathSafety.IsSafeRelativePath(item.StoredRoot) ||
                    !names.Add(item.OriginalDirectoryName))
                {
                    return null;
                }
                foreach (var file in item.Files)
                {
                    if (file is null ||
                        !PetPathSafety.IsSafeRelativePath(file.RelativePath) ||
                        !PetPathSafety.IsSafeRelativePath(file.StoredPath) ||
                        !file.StoredPath.StartsWith(item.StoredRoot + "/", StringComparison.Ordinal) ||
                        !storedPaths.Add(file.StoredPath) || file.Size < 0 ||
                        file.Sha256 is null || file.Sha256.Length != 64 ||
                        !file.Sha256.All(Uri.IsHexDigit))
                    {
                        return null;
                    }
                    if (!validateFiles) continue;
                    var path = PetPathSafety.ResolveContainedPath(directory, file.StoredPath);
                    PetPathSafety.EnsureRegularFile(directory, path);
                    if (new FileInfo(path).Length != file.Size ||
                        !string.Equals(
                            await PetPackageLoader.ComputeSha256Async(path, cancellationToken),
                            file.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
            }
            if (manifest.ManagedState is not null)
            {
                if (!string.Equals(manifest.ManagedState.Id, manifest.PetId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                _ = PetManagementStateStore.NormalizeAndValidate(new PetManagementState
                {
                    Pets = new Dictionary<string, PetManagedInstallation>(StringComparer.OrdinalIgnoreCase)
                    {
                        [manifest.PetId] = manifest.ManagedState,
                    },
                });
            }
            return manifest;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record PetBackupManifest(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("backupId")] string BackupId,
        [property: JsonPropertyName("petId")] string PetId,
        [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("managedState")] PetManagedInstallation? ManagedState,
        [property: JsonPropertyName("directories")] IReadOnlyList<PetBackupDirectory> Directories);

    private sealed record PetBackupDirectory(
        [property: JsonPropertyName("originalDirectoryName")] string OriginalDirectoryName,
        [property: JsonPropertyName("storedRoot")] string StoredRoot,
        [property: JsonPropertyName("files")] IReadOnlyList<PetBackupFile> Files);

    private sealed record PetBackupFile(
        [property: JsonPropertyName("relativePath")] string RelativePath,
        [property: JsonPropertyName("storedPath")] string StoredPath,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("sha256")] string Sha256);

    private sealed record PetBackupSource(string OriginalDirectoryName, string SourcePath);

    private sealed record PetBackupFileSnapshot(long Size, string Sha256);
}
