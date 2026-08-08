using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tessalume.Core.Updates;

public enum PortableUpdateOperation
{
    Install,
    Rollback,
}

public sealed record PortableUpdateRequest(
    int ParentProcessId,
    string SourcePath,
    string DestinationPath,
    string ExpectedSha256,
    string VersionLabel,
    string ResultPath,
    string HelperPath)
{
    public PortableUpdateOperation Operation { get; init; }
    public string PreviousVersionLabel { get; init; } = string.Empty;
    public string StartupHealthToken { get; init; } = string.Empty;
    public string DataSnapshotId { get; init; } = string.Empty;
    public string DataSnapshotManifestSha256 { get; init; } = string.Empty;
    public string RecoveryDataSnapshotId { get; init; } = string.Empty;
    public string RecoveryDataSnapshotManifestSha256 { get; init; } = string.Empty;
}

public sealed record PortableUpdateResult(
    bool Success,
    string VersionLabel,
    string Message,
    string SourcePath,
    string DestinationPath,
    string? BackupPath,
    string HelperPath,
    int HelperProcessId,
    DateTimeOffset CompletedAt)
{
    public PortableUpdateOperation Operation { get; init; }
    public string PreviousVersionLabel { get; init; } = string.Empty;
    public bool RolledBack { get; init; }
    public string DataSnapshotId { get; init; } = string.Empty;
}

public static class PortableUpdateInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<PortableUpdateResult> ApplyAndWriteResultAsync(
        PortableUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PortableUpdateResult result;
        try
        {
            result = await ApplyAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var possibleBackup = Path.GetFullPath(request.DestinationPath) +
                (request.Operation == PortableUpdateOperation.Rollback
                    ? ".rollback-current"
                    : ".previous");
            result = CreateResult(
                request,
                success: false,
                exception.Message,
                File.Exists(possibleBackup) ? possibleBackup : null);
        }

        try
        {
            await WriteResultAsync(request.ResultPath, result, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The replacement result takes priority over the secondary status
            // marker. A later normal launch also removes stale update artifacts.
        }

        return result;
    }

    public static async Task<PortableUpdateResult> ApplyAsync(
        PortableUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = Path.GetFullPath(request.SourcePath);
        var destination = Path.GetFullPath(request.DestinationPath);
        var helper = Path.GetFullPath(request.HelperPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新文件必须位于当前程序所在磁盘，且不能与正在使用的 EXE 相同。");
        }

        if (!File.Exists(source) || !File.Exists(destination))
        {
            throw new FileNotFoundException("更新文件或当前程序文件不存在。");
        }

        if (request.ExpectedSha256.Length != 64 || !request.ExpectedSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("更新请求中的 SHA-256 校验值无效。");
        }

        await WaitForParentExitAsync(request.ParentProcessId, cancellationToken);
        var actualHash = await ComputeSha256Async(source, cancellationToken);
        if (!string.Equals(actualHash, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("安装前的 SHA-256 复核失败，已保留当前版本。");
        }

        var backup = request.Operation == PortableUpdateOperation.Rollback
            ? destination + ".rollback-current"
            : destination + ".previous";
        File.Delete(backup);
        if (request.Operation == PortableUpdateOperation.Rollback)
        {
            if (!string.Equals(source, destination + ".previous", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("恢复请求没有使用已验证的上一版本备份。");
            }
        }
        await ReplaceWithRetryAsync(source, destination, backup, cancellationToken);
        return new PortableUpdateResult(
            true,
            request.VersionLabel,
            $"已成功更新到 {request.VersionLabel}。",
            source,
            destination,
            backup,
            helper,
            Environment.ProcessId,
            DateTimeOffset.Now)
        {
            Operation = request.Operation,
            PreviousVersionLabel = request.PreviousVersionLabel,
            DataSnapshotId = request.DataSnapshotId,
        };
    }

    public static async Task WriteResultAsync(
        string resultPath,
        PortableUpdateResult result,
        CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(resultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(result, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
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

    public static PortableUpdateResult? ReadResult(string resultPath)
    {
        var path = Path.GetFullPath(resultPath);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<PortableUpdateResult>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static async Task RestoreBackupAsync(
        string destinationPath,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        var destination = Path.GetFullPath(destinationPath);
        var backup = Path.GetFullPath(backupPath);
        var expectedPrevious = destination + ".previous";
        var expectedRollbackCurrent = destination + ".rollback-current";
        if (!string.Equals(backup, expectedPrevious, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backup, expectedRollbackCurrent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("恢复文件不在允许的程序回滚位置。");
        }
        if (!File.Exists(destination) || !File.Exists(backup))
        {
            throw new FileNotFoundException("当前程序或恢复文件不存在。");
        }

        var failedVersion = destination + ".failed-update";
        File.Delete(failedVersion);
        await ReplaceWithRetryAsync(backup, destination, failedVersion, cancellationToken);
        try
        {
            File.Delete(failedVersion);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task ReplaceWithRetryAsync(
        string source,
        string destination,
        string backup,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Replace(source, destination, backup, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastException = exception;
                if (attempt < 20)
                {
                    await Task.Delay(250, cancellationToken);
                }
            }
            catch (PlatformNotSupportedException exception)
            {
                lastException = exception;
                break;
            }
        }

        try
        {
            File.Move(destination, backup, overwrite: true);
            try
            {
                File.Move(source, destination, overwrite: false);
                return;
            }
            catch
            {
                if (!File.Exists(destination) && File.Exists(backup))
                {
                    File.Move(backup, destination, overwrite: true);
                }
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("无法替换正在使用的 Tessalume.exe，当前版本已保留。", lastException ?? exception);
        }
    }

    private static async Task WaitForParentExitAsync(int processId, CancellationToken cancellationToken)
    {
        if (processId <= 0) return;
        if (processId == Environment.ProcessId)
        {
            throw new InvalidOperationException("更新助手不能等待自身退出。");
        }

        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("等待旧版本退出超时，更新尚未安装。");
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static PortableUpdateResult CreateResult(
        PortableUpdateRequest request,
        bool success,
        string message,
        string? backupPath) =>
        new(
            success,
            request.VersionLabel,
            message,
            Path.GetFullPath(request.SourcePath),
            Path.GetFullPath(request.DestinationPath),
            backupPath,
            Path.GetFullPath(request.HelperPath),
            Environment.ProcessId,
            DateTimeOffset.Now)
        {
            Operation = request.Operation,
            PreviousVersionLabel = request.PreviousVersionLabel,
            DataSnapshotId = request.DataSnapshotId,
        };
}
