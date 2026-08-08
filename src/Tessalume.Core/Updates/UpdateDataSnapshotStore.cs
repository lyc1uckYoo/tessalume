using System.Security.Cryptography;
using System.Text.Json;

namespace Tessalume.Core.Updates;

public sealed record UpdateDataSnapshotInfo(
    string SnapshotId,
    string VersionLabel,
    string DirectoryPath,
    string ManifestSha256,
    DateTimeOffset CreatedAt);

/// <summary>
/// Owns the small set of versioned data files that must move together with a
/// portable executable rollback. Theme assets, personal images, logs, and
/// other user content deliberately remain untouched.
/// </summary>
public sealed class UpdateDataSnapshotStore
{
    public const int CurrentSchemaVersion = 1;
    private const string ManifestFileName = "snapshot.json";
    private static readonly string[] ManagedRelativePaths = ["ui-settings.json", "state.json"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _dataDirectory;
    private readonly string _snapshotsDirectory;
    private readonly string _recoveryDirectory;

    public UpdateDataSnapshotStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = NormalizeDirectory(dataDirectory);
        _snapshotsDirectory = NormalizeDirectory(Path.Combine(_dataDirectory, "updates", "data-snapshots"));
        _recoveryDirectory = NormalizeDirectory(Path.Combine(_dataDirectory, "backups", "version-rollback"));
    }

    public async Task<UpdateDataSnapshotInfo> CreateAsync(
        string snapshotId,
        string versionLabel,
        CancellationToken cancellationToken = default)
    {
        ValidateSnapshotId(snapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionLabel);
        var destination = GetSnapshotDirectory(snapshotId);
        var temporary = destination + ".partial";
        TryDeleteDirectory(temporary);
        if (Directory.Exists(destination))
        {
            throw new IOException("更新数据快照标识已存在。");
        }

        Directory.CreateDirectory(temporary);
        try
        {
            var files = new List<UpdateDataSnapshotFile>();
            foreach (var relativePath in ManagedRelativePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = GetManagedDataPath(relativePath);
                if (!File.Exists(source))
                {
                    files.Add(new UpdateDataSnapshotFile(relativePath, false, 0, string.Empty));
                    continue;
                }
                EnsureRegularFile(source);
                var target = Path.Combine(temporary, relativePath);
                File.Copy(source, target, overwrite: false);
                files.Add(new UpdateDataSnapshotFile(
                    relativePath,
                    true,
                    new FileInfo(target).Length,
                    await ComputeSha256Async(target, cancellationToken)));
            }

            var manifest = new UpdateDataSnapshotManifest(
                CurrentSchemaVersion,
                snapshotId,
                NormalizeVersionLabel(versionLabel),
                DateTimeOffset.Now,
                files);
            var manifestPath = Path.Combine(temporary, ManifestFileName);
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken);
            Directory.Move(temporary, destination);
            return new UpdateDataSnapshotInfo(
                snapshotId,
                manifest.VersionLabel,
                destination,
                await ComputeSha256Async(Path.Combine(destination, ManifestFileName), cancellationToken),
                manifest.CreatedAt);
        }
        catch
        {
            TryDeleteDirectory(temporary);
            throw;
        }
    }

    public async Task<UpdateDataSnapshotInfo?> ValidateAsync(
        string snapshotId,
        string? expectedManifestSha256 = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateSnapshotId(snapshotId);
            if (expectedManifestSha256 is not null && !IsValidSha256(expectedManifestSha256)) return null;
            var directory = GetSnapshotDirectory(snapshotId);
            EnsureRegularDirectory(directory);
            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath)) return null;
            EnsureRegularFile(manifestPath);
            var manifestSha256 = await ComputeSha256Async(manifestPath, cancellationToken);
            if (expectedManifestSha256 is not null &&
                !string.Equals(manifestSha256, expectedManifestSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateDataSnapshotManifest>(
                stream,
                JsonOptions,
                cancellationToken);
            if (manifest is null || manifest.SchemaVersion != CurrentSchemaVersion ||
                !string.Equals(manifest.SnapshotId, snapshotId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.VersionLabel) ||
                manifest.Files.Count != ManagedRelativePaths.Length)
            {
                return null;
            }

            foreach (var relativePath in ManagedRelativePaths)
            {
                var entry = manifest.Files.SingleOrDefault(file =>
                    string.Equals(file.RelativePath, relativePath, StringComparison.Ordinal));
                if (entry is null) return null;
                var path = Path.Combine(directory, relativePath);
                if (!entry.Exists)
                {
                    if (File.Exists(path) || entry.Size != 0 || entry.Sha256.Length != 0) return null;
                    continue;
                }
                if (!File.Exists(path) || entry.Size < 0 || !IsValidSha256(entry.Sha256)) return null;
                EnsureRegularFile(path);
                if (new FileInfo(path).Length != entry.Size ||
                    !string.Equals(
                        await ComputeSha256Async(path, cancellationToken),
                        entry.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return new UpdateDataSnapshotInfo(
                snapshotId,
                manifest.VersionLabel,
                directory,
                manifestSha256,
                manifest.CreatedAt);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task RestoreAsync(
        string snapshotId,
        string? expectedManifestSha256 = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ValidateAsync(snapshotId, expectedManifestSha256, cancellationToken)
            ?? throw new InvalidDataException("更新数据快照已损坏或不完整，拒绝跨版本恢复。");
        var manifest = JsonSerializer.Deserialize<UpdateDataSnapshotManifest>(
            await File.ReadAllTextAsync(Path.Combine(snapshot.DirectoryPath, ManifestFileName), cancellationToken),
            JsonOptions) ?? throw new InvalidDataException("更新数据快照清单无效。");
        var transaction = NormalizeDirectory(Path.Combine(
            _snapshotsDirectory,
            $"restore-{Guid.NewGuid():N}.partial"));
        Directory.CreateDirectory(transaction);
        var applied = new List<string>();
        try
        {
            foreach (var relativePath in ManagedRelativePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = GetManagedDataPath(relativePath);
                var rollback = Path.Combine(transaction, relativePath);
                if (File.Exists(target))
                {
                    EnsureRegularFile(target);
                    File.Copy(target, rollback, overwrite: false);
                }
                applied.Add(relativePath);

                var entry = manifest.Files.Single(file => file.RelativePath == relativePath);
                if (!entry.Exists)
                {
                    File.Delete(target);
                    continue;
                }
                var staged = target + ".version-restore.tmp";
                File.Copy(Path.Combine(snapshot.DirectoryPath, relativePath), staged, overwrite: true);
                File.Move(staged, target, overwrite: true);
            }
        }
        catch
        {
            foreach (var relativePath in applied.AsEnumerable().Reverse())
            {
                var target = GetManagedDataPath(relativePath);
                var rollback = Path.Combine(transaction, relativePath);
                if (File.Exists(rollback)) File.Copy(rollback, target, overwrite: true);
                else File.Delete(target);
                TryDeleteFile(target + ".version-restore.tmp");
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(transaction);
        }
    }

    public async Task<string> PreserveRecoveryCopyAsync(
        string snapshotId,
        string label,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ValidateAsync(snapshotId, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("无法保留损坏的版本回滚数据快照。");
        var safeLabel = string.Concat(label.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        safeLabel = safeLabel.Length <= 64 ? safeLabel : safeLabel[..64];
        var destination = Path.Combine(
            _recoveryDirectory,
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeLabel}-{snapshotId[..8]}");
        Directory.CreateDirectory(_recoveryDirectory);
        CopyDirectory(snapshot.DirectoryPath, destination);
        return destination;
    }

    public void Delete(string snapshotId)
    {
        ValidateSnapshotId(snapshotId);
        TryDeleteDirectory(GetSnapshotDirectory(snapshotId));
    }

    private string GetSnapshotDirectory(string snapshotId)
    {
        var path = Path.GetFullPath(Path.Combine(_snapshotsDirectory, snapshotId));
        EnsureContained(_snapshotsDirectory, path);
        return path;
    }

    private string GetManagedDataPath(string relativePath)
    {
        if (!ManagedRelativePaths.Contains(relativePath, StringComparer.Ordinal))
        {
            throw new InvalidDataException("更新数据快照包含未授权路径。");
        }
        var path = Path.GetFullPath(Path.Combine(_dataDirectory, relativePath));
        EnsureContained(_dataDirectory, path);
        return path;
    }

    private static void ValidateSnapshotId(string value)
    {
        if (value.Length != 32 || !value.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("更新数据快照标识无效。", nameof(value));
        }
    }

    private static void EnsureRegularFile(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("更新数据快照拒绝符号链接或重解析点文件。");
        }
    }

    private static void EnsureRegularDirectory(string path)
    {
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("更新数据快照目录无效或包含重解析点。");
        }
    }

    private static void EnsureContained(string root, string path)
    {
        var normalizedRoot = NormalizeDirectory(root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新数据快照路径超出便携数据目录。");
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizeVersionLabel(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"v{normalized}";
    }

    private static bool IsValidSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
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

    private sealed record UpdateDataSnapshotFile(
        string RelativePath,
        bool Exists,
        long Size,
        string Sha256);

    private sealed record UpdateDataSnapshotManifest(
        int SchemaVersion,
        string SnapshotId,
        string VersionLabel,
        DateTimeOffset CreatedAt,
        IReadOnlyList<UpdateDataSnapshotFile> Files);
}
