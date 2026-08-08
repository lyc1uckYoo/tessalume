using System.Security.Cryptography;
using System.Text.Json;

namespace Tessalume.Core.Updates;

public sealed record UpdateRollbackInfo(
    int SchemaVersion,
    string CurrentVersionLabel,
    string PreviousVersionLabel,
    string BackupPath,
    string BackupSha256,
    string DataSnapshotId,
    string DataSnapshotManifestSha256,
    DateTimeOffset ConfirmedAt);

public sealed class UpdateRollbackStore
{
    public const int CurrentSchemaVersion = 2;
    public const string StateFileName = "rollback.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _expectedBackupPath;
    private readonly string _statePath;
    private readonly UpdateDataSnapshotStore _dataSnapshots;

    public UpdateRollbackStore(
        string applicationRoot,
        string dataDirectory,
        string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        if (!string.Equals(Path.GetFileName(executableName), executableName, StringComparison.Ordinal) ||
            !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The rollback executable name must be a simple .exe file name.", nameof(executableName));
        }

        var normalizedApplicationRoot = Path.GetFullPath(applicationRoot);
        _expectedBackupPath = Path.Combine(normalizedApplicationRoot, executableName + ".previous");
        _statePath = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "updates",
            StateFileName);
        _dataSnapshots = new UpdateDataSnapshotStore(dataDirectory);
    }

    public async Task<UpdateRollbackInfo?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath)) return null;
        try
        {
            await using var stream = File.OpenRead(_statePath);
            var info = await JsonSerializer.DeserializeAsync<UpdateRollbackInfo>(
                stream,
                JsonOptions,
                cancellationToken);
            if (info is null || info.SchemaVersion != CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(info.CurrentVersionLabel) ||
                string.IsNullOrWhiteSpace(info.PreviousVersionLabel) ||
                !string.Equals(
                    Path.GetFullPath(info.BackupPath),
                    _expectedBackupPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsValidSha256(info.BackupSha256) ||
                info.DataSnapshotId.Length != 32 ||
                !info.DataSnapshotId.All(Uri.IsHexDigit) ||
                !IsValidSha256(info.DataSnapshotManifestSha256) ||
                !File.Exists(_expectedBackupPath))
            {
                return null;
            }

            var actualHash = await ComputeSha256Async(_expectedBackupPath, cancellationToken);
            if (!string.Equals(actualHash, info.BackupSha256, StringComparison.OrdinalIgnoreCase) ||
                await _dataSnapshots.ValidateAsync(
                    info.DataSnapshotId,
                    info.DataSnapshotManifestSha256,
                    cancellationToken) is null)
            {
                return null;
            }
            return info with { BackupPath = _expectedBackupPath, BackupSha256 = actualHash };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<UpdateRollbackInfo> SaveAsync(
        string currentVersionLabel,
        string previousVersionLabel,
        string backupPath,
        string dataSnapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersionLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousVersionLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        backupPath = Path.GetFullPath(backupPath);
        if (!string.Equals(backupPath, _expectedBackupPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(backupPath))
        {
            throw new InvalidDataException("更新回滚备份不在允许的程序目录中。");
        }

        var dataSnapshot = await _dataSnapshots.ValidateAsync(dataSnapshotId, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("更新前数据快照已损坏，无法建立安全回滚点。");

        var info = new UpdateRollbackInfo(
            CurrentSchemaVersion,
            NormalizeVersionLabel(currentVersionLabel),
            NormalizeVersionLabel(previousVersionLabel),
            backupPath,
            await ComputeSha256Async(backupPath, cancellationToken),
            dataSnapshot.SnapshotId,
            dataSnapshot.ManifestSha256,
            DateTimeOffset.Now);
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var temporaryPath = _statePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(info, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, _statePath, overwrite: true);
            return info;
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(_statePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string NormalizeVersionLabel(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"v{normalized}";
    }

    private static bool IsValidSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
