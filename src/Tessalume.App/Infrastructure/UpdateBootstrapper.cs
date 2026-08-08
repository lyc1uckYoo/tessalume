using System.Diagnostics;
using System.IO;
using Tessalume.Core.Updates;

namespace Tessalume.App.Infrastructure;

/// <summary>
/// Validates update command boundaries, creates the isolated helper process,
/// and owns disposable update artifacts. Transaction execution lives in
/// <see cref="UpdateHelperRuntime"/>.
/// </summary>
internal static class UpdateBootstrapper
{
    private const string ApplyUpdateArgument = "--apply-update";
    private const string StartupHealthArgument = UpdateHelperRuntime.StartupHealthArgument;
    private const string ResultFileName = "update-result.json";
    private const string HealthDirectoryName = "health";

    public static bool TryParseHelperArguments(string[] args, out PortableUpdateRequest? request)
    {
        request = null;
        if (args.Length == 0 || !string.Equals(args[0], ApplyUpdateArgument, StringComparison.Ordinal))
        {
            return false;
        }
        if (args.Length != 15 ||
            !Enum.TryParse<PortableUpdateOperation>(args[1], ignoreCase: true, out var operation) ||
            !Enum.IsDefined(operation) ||
            !int.TryParse(args[2], out var parentProcessId) ||
            string.IsNullOrWhiteSpace(args[7]) ||
            !IsValidHealthToken(args[10]) ||
            !IsValidSnapshotArgument(args[11], args[12]) ||
            (operation == PortableUpdateOperation.Rollback
                ? !IsValidSnapshotArgument(args[13], args[14])
                : args[13] != "-" || args[14] != "-"))
        {
            throw new InvalidDataException("自动更新助手参数不完整。");
        }

        var source = Path.GetFullPath(args[3]);
        var destination = Path.GetFullPath(args[4]);
        var resultPath = Path.GetFullPath(args[8]);
        var helperPath = Path.GetFullPath(args[9]);
        var applicationRoot = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("自动更新目标路径无效。");
        var dataRoot = Path.Combine(applicationRoot, "data");
        var expectedResultPath = Path.Combine(dataRoot, ResultFileName);
        var expectedExecutableName = $"{BrandInfo.ProductName}.exe";
        var allowedSource = operation == PortableUpdateOperation.Install
            ? IsInside(source, Path.Combine(dataRoot, "updates", "downloads"))
            : string.Equals(source, destination + ".previous", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(Path.GetFileName(destination), expectedExecutableName, StringComparison.OrdinalIgnoreCase) ||
            !allowedSource ||
            !IsInside(helperPath, Path.Combine(dataRoot, "updates", "helpers")) ||
            !string.Equals(resultPath, expectedResultPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(helperPath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("自动更新助手拒绝了越界路径。");
        }

        request = new PortableUpdateRequest(
            parentProcessId,
            source,
            destination,
            args[5],
            args[6],
            resultPath,
            helperPath)
        {
            Operation = operation,
            PreviousVersionLabel = args[7],
            StartupHealthToken = args[10],
            DataSnapshotId = args[11],
            DataSnapshotManifestSha256 = args[12],
            RecoveryDataSnapshotId = args[13] == "-" ? string.Empty : args[13],
            RecoveryDataSnapshotManifestSha256 = args[14] == "-" ? string.Empty : args[14],
        };
        return true;
    }

    public static bool TryParseStartupHealthToken(string[] args, out string? token)
    {
        token = null;
        if (args.Length == 0 || !string.Equals(args[0], StartupHealthArgument, StringComparison.Ordinal))
        {
            return false;
        }
        if (args.Length != 2 || !IsValidHealthToken(args[1]))
        {
            throw new InvalidDataException("更新启动确认参数无效。");
        }
        token = args[1];
        return true;
    }

    public static Task<int> RunHelperAsync(PortableUpdateRequest request) =>
        UpdateHelperRuntime.RunAsync(request);

    public static async Task<int> StartHelperAsync(
        PortableLayout layout,
        string downloadedExecutable,
        ReleaseUpdate release,
        CancellationToken cancellationToken = default)
    {
        var currentExecutable = RequireCurrentExecutable();
        var resultPath = GetResultPath(layout);
        File.Delete(resultPath);
        var dataSnapshots = new UpdateDataSnapshotStore(layout.DataDirectory);
        var snapshot = await dataSnapshots.CreateAsync(
            Guid.NewGuid().ToString("N"),
            BrandInfo.VersionLabel,
            cancellationToken);
        string? helperPath = null;
        try
        {
            helperPath = CreateHelperCopy(layout, downloadedExecutable);
            return StartHelperProcess(
                layout,
                PortableUpdateOperation.Install,
                downloadedExecutable,
                currentExecutable,
                release.Sha256,
                release.VersionLabel,
                BrandInfo.VersionLabel,
                resultPath,
                helperPath,
                snapshot,
                recoverySnapshot: null);
        }
        catch
        {
            TryDeleteFile(helperPath);
            dataSnapshots.Delete(snapshot.SnapshotId);
            throw;
        }
    }

    public static async Task<int> StartRollbackHelperAsync(
        PortableLayout layout,
        UpdateRollbackInfo rollback,
        CancellationToken cancellationToken = default)
    {
        var currentExecutable = RequireCurrentExecutable();
        if (!string.Equals(rollback.CurrentVersionLabel, BrandInfo.VersionLabel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("上一版本恢复记录与当前软件版本不一致。");
        }
        var dataSnapshots = new UpdateDataSnapshotStore(layout.DataDirectory);
        var rollbackSnapshot = await dataSnapshots.ValidateAsync(
            rollback.DataSnapshotId,
            rollback.DataSnapshotManifestSha256,
            cancellationToken) ?? throw new InvalidDataException("上一版本的数据恢复点已损坏。");
        var resultPath = GetResultPath(layout);
        File.Delete(resultPath);
        UpdateDataSnapshotInfo? recoverySnapshot = null;
        string? helperPath = null;
        try
        {
            recoverySnapshot = await dataSnapshots.CreateAsync(
                Guid.NewGuid().ToString("N"),
                rollback.CurrentVersionLabel,
                cancellationToken);
            helperPath = CreateHelperCopy(layout, currentExecutable);
            return StartHelperProcess(
                layout,
                PortableUpdateOperation.Rollback,
                rollback.BackupPath,
                currentExecutable,
                rollback.BackupSha256,
                rollback.PreviousVersionLabel,
                rollback.CurrentVersionLabel,
                resultPath,
                helperPath,
                rollbackSnapshot,
                recoverySnapshot);
        }
        catch
        {
            TryDeleteFile(helperPath);
            if (recoverySnapshot is not null) dataSnapshots.Delete(recoverySnapshot.SnapshotId);
            throw;
        }
    }

    public static Task ConfirmStartupHealthyAsync(
        PortableLayout layout,
        string? token,
        CancellationToken cancellationToken = default) =>
        UpdateHelperRuntime.ConfirmStartupHealthyAsync(layout, token, cancellationToken);

    public static PortableUpdateResult? ReadResult(PortableLayout layout) =>
        PortableUpdateInstaller.ReadResult(GetResultPath(layout));

    public static void DismissResult(PortableLayout layout)
    {
        try { File.Delete(GetResultPath(layout)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LocalLog.Write("Could not remove the update result marker.", exception);
        }
    }

    public static async Task CleanupArtifactsAsync(PortableLayout layout, PortableUpdateResult result)
    {
        try
        {
            if (result.HelperProcessId > 0 && result.HelperProcessId != Environment.ProcessId)
            {
                try
                {
                    using var helper = Process.GetProcessById(result.HelperProcessId);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await helper.WaitForExitAsync(timeout.Token);
                }
                catch (Exception exception) when (exception is ArgumentException or OperationCanceledException)
                {
                }
            }
            await TryDeleteKnownUpdateFileAsync(layout, result.SourcePath);
            await TryDeleteKnownUpdateFileAsync(layout, result.HelperPath);
            CleanupOldPartials(layout);
        }
        catch (Exception exception)
        {
            LocalLog.Write("Update artifact cleanup failed.", exception);
        }
    }

    public static async Task CleanupStaleArtifactsAsync(PortableLayout layout)
    {
        try
        {
            foreach (var (directory, pattern) in new[]
                     {
                         (Path.Combine(layout.DataDirectory, "updates", "helpers"), "Tessalume.UpdateHelper.*.exe"),
                         (Path.Combine(layout.DataDirectory, "updates", "downloads"), "Tessalume-*.exe.download*"),
                     })
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                {
                    await TryDeleteKnownUpdateFileAsync(layout, file);
                }
            }
        }
        catch (Exception exception)
        {
            LocalLog.Write("Stale update artifact cleanup failed.", exception);
        }
    }

    private static string RequireCurrentExecutable()
    {
        var currentExecutable = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(currentExecutable) && File.Exists(currentExecutable)
            ? currentExecutable
            : throw new InvalidOperationException("无法确定当前 Tessalume.exe 的位置。");
    }

    private static string CreateHelperCopy(PortableLayout layout, string sourceExecutable)
    {
        var helpersDirectory = Path.Combine(layout.DataDirectory, "updates", "helpers");
        Directory.CreateDirectory(helpersDirectory);
        var helperPath = Path.Combine(
            helpersDirectory,
            $"Tessalume.UpdateHelper.{Guid.NewGuid():N}.exe");
        File.Copy(sourceExecutable, helperPath, overwrite: false);
        return helperPath;
    }

    private static string GetResultPath(PortableLayout layout) =>
        Path.Combine(layout.DataDirectory, ResultFileName);

    private static int StartHelperProcess(
        PortableLayout layout,
        PortableUpdateOperation operation,
        string sourcePath,
        string destinationPath,
        string expectedSha256,
        string versionLabel,
        string previousVersionLabel,
        string resultPath,
        string helperPath,
        UpdateDataSnapshotInfo dataSnapshot,
        UpdateDataSnapshotInfo? recoverySnapshot)
    {
        var healthToken = Guid.NewGuid().ToString("N");
        var startInfo = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = layout.RootDirectory,
        };
        foreach (var argument in new[]
                 {
                     ApplyUpdateArgument,
                     operation.ToString(),
                     Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     Path.GetFullPath(sourcePath),
                     Path.GetFullPath(destinationPath),
                     expectedSha256,
                     versionLabel,
                     previousVersionLabel,
                     resultPath,
                     helperPath,
                     healthToken,
                     dataSnapshot.SnapshotId,
                     dataSnapshot.ManifestSha256,
                     recoverySnapshot?.SnapshotId ?? "-",
                     recoverySnapshot?.ManifestSha256 ?? "-",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }
        try
        {
            return Process.Start(startInfo)?.Id ?? throw new InvalidOperationException("无法启动更新助手。");
        }
        catch
        {
            TryDeleteFile(helperPath);
            throw;
        }
    }

    private static bool IsValidHealthToken(string token) => UpdateHelperRuntime.IsValidToken(token);

    private static bool IsValidSnapshotArgument(string snapshotId, string manifestSha256) =>
        IsValidHealthToken(snapshotId) && manifestSha256.Length == 64 && manifestSha256.All(Uri.IsHexDigit);

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsInside(string path, string directory)
    {
        var target = Path.GetFullPath(path);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task TryDeleteKnownUpdateFileAsync(PortableLayout layout, string path)
    {
        var target = Path.GetFullPath(path);
        var updatesRoot = Path.GetFullPath(Path.Combine(layout.DataDirectory, "updates")) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase)) return;
        for (var attempt = 1; attempt <= 40 && File.Exists(target); attempt++)
        {
            try
            {
                File.Delete(target);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 40)
                {
                    LocalLog.Write($"Could not clean update artifact: {target}", exception);
                    return;
                }
                await Task.Delay(250);
            }
        }
    }

    private static void CleanupOldPartials(PortableLayout layout)
    {
        var updatesRoot = Path.Combine(layout.DataDirectory, "updates");
        if (!Directory.Exists(updatesRoot)) return;
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var file in Directory.EnumerateFiles(updatesRoot, "*", options))
        {
            var isDisposable = file.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
                               file.Contains(
                                   $"{Path.DirectorySeparatorChar}{HealthDirectoryName}{Path.DirectorySeparatorChar}",
                                   StringComparison.OrdinalIgnoreCase);
            if (isDisposable && File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
        }
    }
}
