using System.Diagnostics;
using System.IO;
using Tessalume.Core.Updates;

namespace Tessalume.App.Infrastructure;

/// <summary>
/// Executes the post-shutdown update transaction. Argument validation and
/// helper process creation remain in <see cref="UpdateBootstrapper"/>.
/// </summary>
internal static class UpdateHelperRuntime
{
    internal const string StartupHealthArgument = "--update-health";
    private const string HealthDirectoryName = "health";

    public static async Task<int> RunAsync(PortableUpdateRequest request)
    {
        var result = await PortableUpdateInstaller.ApplyAndWriteResultAsync(request);
        if (!result.Success)
        {
            var restartPath = File.Exists(result.DestinationPath)
                ? result.DestinationPath
                : result.BackupPath is { } backup && File.Exists(backup)
                    ? backup
                    : result.DestinationPath;
            return TryStartApplication(restartPath) ? 1 : 2;
        }
        return request.Operation == PortableUpdateOperation.Rollback
            ? await CompleteManualRollbackAsync(request, result)
            : await ConfirmInstalledUpdateAsync(request, result);
    }

    public static async Task ConfirmStartupHealthyAsync(
        PortableLayout layout,
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        if (!IsValidToken(token)) throw new InvalidDataException("更新启动确认令牌无效。");
        var path = GetHealthPath(layout.DataDirectory, token);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        var marker = new StartupHealthMarker(
            token,
            Environment.ProcessId,
            BrandInfo.VersionLabel,
            DateTimeOffset.Now);
        await File.WriteAllTextAsync(
            temporaryPath,
            System.Text.Json.JsonSerializer.Serialize(marker),
            cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static async Task<int> ConfirmInstalledUpdateAsync(
        PortableUpdateRequest request,
        PortableUpdateResult result)
    {
        var application = StartApplication(result.DestinationPath, request.StartupHealthToken);
        if (application is null)
        {
            return await RestoreAfterFailedStartupAsync(request, result, null, "新版本无法启动");
        }
        using (application)
        {
            var dataDirectory = Path.Combine(Path.GetDirectoryName(result.DestinationPath)!, "data");
            var healthPath = GetHealthPath(dataDirectory, request.StartupHealthToken);
            var healthy = await WaitForStartupHealthAsync(
                application,
                healthPath,
                request.StartupHealthToken,
                result.VersionLabel,
                TimeSpan.FromSeconds(60));
            if (!healthy)
            {
                return await RestoreAfterFailedStartupAsync(
                    request,
                    result,
                    application,
                    "新版本没有在规定时间内完成启动健康确认");
            }
            TryDelete(healthPath);
            if (result.BackupPath is { } backup)
            {
                try
                {
                    var store = new UpdateRollbackStore(
                        Path.GetDirectoryName(result.DestinationPath)!,
                        dataDirectory,
                        Path.GetFileName(result.DestinationPath));
                    await store.SaveAsync(
                        result.VersionLabel,
                        result.PreviousVersionLabel,
                        backup,
                        request.DataSnapshotId);
                }
                catch
                {
                    // The healthy executable backup remains usable even when
                    // optional recovery metadata cannot be persisted.
                }
            }
        }
        return 0;
    }

    private static async Task<int> CompleteManualRollbackAsync(
        PortableUpdateRequest request,
        PortableUpdateResult result)
    {
        var dataDirectory = Path.Combine(Path.GetDirectoryName(result.DestinationPath)!, "data");
        var dataSnapshots = new UpdateDataSnapshotStore(dataDirectory);
        try
        {
            await dataSnapshots.RestoreAsync(
                request.DataSnapshotId,
                request.DataSnapshotManifestSha256);
        }
        catch
        {
            return await RestoreAfterFailedManualRollbackAsync(
                request,
                result,
                null,
                "上一版本配置无法安全恢复");
        }
        var application = StartApplication(result.DestinationPath, healthToken: null);
        if (application is null)
        {
            return await RestoreAfterFailedManualRollbackAsync(
                request,
                result,
                null,
                "上一版本无法重新启动");
        }
        using (application)
        {
            if (!await WaitForProcessStabilityAsync(application, TimeSpan.FromSeconds(8)))
            {
                return await RestoreAfterFailedManualRollbackAsync(
                    request,
                    result,
                    application,
                    "上一版本启动后异常退出");
            }
        }
        try
        {
            _ = await dataSnapshots.PreserveRecoveryCopyAsync(
                request.RecoveryDataSnapshotId,
                request.PreviousVersionLabel);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Preserving forward-compatible settings after rollback failed.", exception);
        }
        if (result.BackupPath is { } rollbackCurrent) TryDelete(rollbackCurrent);
        new UpdateRollbackStore(
            Path.GetDirectoryName(result.DestinationPath)!,
            dataDirectory,
            Path.GetFileName(result.DestinationPath)).Clear();
        dataSnapshots.Delete(request.DataSnapshotId);
        dataSnapshots.Delete(request.RecoveryDataSnapshotId);
        return 0;
    }

    private static async Task<int> RestoreAfterFailedManualRollbackAsync(
        PortableUpdateRequest request,
        PortableUpdateResult result,
        Process? previousApplication,
        string reason)
    {
        if (previousApplication is not null) await StopProcessAsync(previousApplication);
        if (result.BackupPath is not { } currentBackup || !File.Exists(currentBackup)) return 2;
        var dataDirectory = Path.Combine(Path.GetDirectoryName(result.DestinationPath)!, "data");
        var dataSnapshots = new UpdateDataSnapshotStore(dataDirectory);
        try
        {
            await PortableUpdateInstaller.RestoreBackupAsync(result.DestinationPath, currentBackup);
            await dataSnapshots.RestoreAsync(
                request.RecoveryDataSnapshotId,
                request.RecoveryDataSnapshotManifestSha256);
            new UpdateRollbackStore(
                Path.GetDirectoryName(result.DestinationPath)!,
                dataDirectory,
                Path.GetFileName(result.DestinationPath)).Clear();
            dataSnapshots.Delete(request.DataSnapshotId);
            dataSnapshots.Delete(request.RecoveryDataSnapshotId);
            var restored = result with
            {
                Success = false,
                RolledBack = true,
                VersionLabel = result.PreviousVersionLabel,
                PreviousVersionLabel = result.VersionLabel,
                Message = $"{reason}，已恢复 {result.PreviousVersionLabel} 和对应用户配置。",
                BackupPath = null,
                CompletedAt = DateTimeOffset.Now,
            };
            await PortableUpdateInstaller.WriteResultAsync(request.ResultPath, restored);
            return TryStartApplication(result.DestinationPath) ? 1 : 2;
        }
        catch
        {
            return 2;
        }
    }

    private static async Task<int> RestoreAfterFailedStartupAsync(
        PortableUpdateRequest request,
        PortableUpdateResult result,
        Process? application,
        string reason)
    {
        if (application is not null) await StopProcessAsync(application);
        if (result.BackupPath is not { } backup || !File.Exists(backup)) return 2;
        try
        {
            var dataDirectory = Path.Combine(Path.GetDirectoryName(result.DestinationPath)!, "data");
            var dataSnapshots = new UpdateDataSnapshotStore(dataDirectory);
            await dataSnapshots.RestoreAsync(
                request.DataSnapshotId,
                request.DataSnapshotManifestSha256);
            await PortableUpdateInstaller.RestoreBackupAsync(result.DestinationPath, backup);
            new UpdateRollbackStore(
                Path.GetDirectoryName(result.DestinationPath)!,
                dataDirectory,
                Path.GetFileName(result.DestinationPath)).Clear();
            var rolledBack = result with
            {
                Success = false,
                RolledBack = true,
                Message = $"{reason}，已自动恢复 {result.PreviousVersionLabel}。",
                BackupPath = null,
                CompletedAt = DateTimeOffset.Now,
            };
            await PortableUpdateInstaller.WriteResultAsync(request.ResultPath, rolledBack);
            dataSnapshots.Delete(request.DataSnapshotId);
            return TryStartApplication(result.DestinationPath) ? 1 : 2;
        }
        catch
        {
            return 2;
        }
    }

    private static Process? StartApplication(string path, string? healthToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path)!,
            };
            if (!string.IsNullOrWhiteSpace(healthToken))
            {
                startInfo.ArgumentList.Add(StartupHealthArgument);
                startInfo.ArgumentList.Add(healthToken);
            }
            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryStartApplication(string path)
    {
        using var process = StartApplication(path, healthToken: null);
        return process is not null;
    }

    private static async Task<bool> WaitForStartupHealthAsync(
        Process application,
        string healthPath,
        string expectedToken,
        string expectedVersionLabel,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (application.HasExited) return false;
            try
            {
                if (File.Exists(healthPath))
                {
                    var marker = System.Text.Json.JsonSerializer.Deserialize<StartupHealthMarker>(
                        await File.ReadAllTextAsync(healthPath));
                    if (marker?.Token == expectedToken && marker.ProcessId == application.Id &&
                        string.Equals(marker.VersionLabel, expectedVersionLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
            }
            await Task.Delay(250);
        }
        return false;
    }

    private static async Task<bool> WaitForProcessStabilityAsync(Process application, TimeSpan duration)
    {
        var deadline = DateTimeOffset.UtcNow + duration;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try { if (application.HasExited) return false; }
            catch (InvalidOperationException) { return false; }
            await Task.Delay(250);
        }
        return !application.HasExited;
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (process.HasExited) return;
            _ = process.CloseMainWindow();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static string GetHealthPath(string dataDirectory, string token) =>
        Path.Combine(dataDirectory, "updates", HealthDirectoryName, $"{token}.json");

    internal static bool IsValidToken(string token) => token.Length == 32 && token.All(Uri.IsHexDigit);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private sealed record StartupHealthMarker(
        string Token,
        int ProcessId,
        string VersionLabel,
        DateTimeOffset ConfirmedAt);
}
