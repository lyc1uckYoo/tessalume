using System.Diagnostics;
using System.IO;
using Tessalume.Core.Updates;

namespace Tessalume.App.Infrastructure;

internal static class UpdateBootstrapper
{
    private const string ApplyUpdateArgument = "--apply-update";
    private const string ResultFileName = "update-result.json";

    public static bool TryParseHelperArguments(string[] args, out PortableUpdateRequest? request)
    {
        request = null;
        if (args.Length == 0 || !string.Equals(args[0], ApplyUpdateArgument, StringComparison.Ordinal))
        {
            return false;
        }
        if (args.Length != 8 || !int.TryParse(args[1], out var parentProcessId))
        {
            throw new InvalidDataException("自动更新助手参数不完整。");
        }

        var source = Path.GetFullPath(args[2]);
        var destination = Path.GetFullPath(args[3]);
        var resultPath = Path.GetFullPath(args[6]);
        var helperPath = Path.GetFullPath(args[7]);
        var applicationRoot = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("自动更新目标路径无效。");
        var dataRoot = Path.Combine(applicationRoot, "data");
        var expectedResultPath = Path.Combine(dataRoot, ResultFileName);
        var expectedExecutableName = $"{BrandInfo.ProductName}.exe";
        if (!string.Equals(Path.GetFileName(destination), expectedExecutableName, StringComparison.OrdinalIgnoreCase) ||
            !IsInside(source, Path.Combine(dataRoot, "updates", "downloads")) ||
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
            args[4],
            args[5],
            resultPath,
            helperPath);
        return true;
    }

    public static async Task<int> RunHelperAsync(PortableUpdateRequest request)
    {
        var result = await PortableUpdateInstaller.ApplyAndWriteResultAsync(request);
        var restartPath = File.Exists(result.DestinationPath)
            ? result.DestinationPath
            : result.BackupPath is { } backup && File.Exists(backup)
                ? backup
                : null;
        if (restartPath is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(restartPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(restartPath)!,
                });
            }
            catch
            {
                return 2;
            }
        }

        return result.Success ? 0 : 1;
    }

    public static int StartHelper(
        PortableLayout layout,
        string downloadedExecutable,
        ReleaseUpdate release)
    {
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
        {
            throw new InvalidOperationException("无法确定当前 Tessalume.exe 的位置。");
        }

        var helpersDirectory = Path.Combine(layout.DataDirectory, "updates", "helpers");
        Directory.CreateDirectory(helpersDirectory);
        var helperPath = Path.Combine(
            helpersDirectory,
            $"Tessalume.UpdateHelper.{Guid.NewGuid():N}.exe");
        File.Copy(currentExecutable, helperPath, overwrite: false);

        var resultPath = GetResultPath(layout);
        File.Delete(resultPath);
        var startInfo = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = layout.RootDirectory,
        };
        startInfo.ArgumentList.Add(ApplyUpdateArgument);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(Path.GetFullPath(downloadedExecutable));
        startInfo.ArgumentList.Add(Path.GetFullPath(currentExecutable));
        startInfo.ArgumentList.Add(release.Sha256);
        startInfo.ArgumentList.Add(release.VersionLabel);
        startInfo.ArgumentList.Add(resultPath);
        startInfo.ArgumentList.Add(helperPath);

        try
        {
            return Process.Start(startInfo)?.Id ?? throw new InvalidOperationException("无法启动更新助手。");
        }
        catch
        {
            File.Delete(helperPath);
            throw;
        }
    }

    public static PortableUpdateResult? ReadResult(PortableLayout layout) =>
        PortableUpdateInstaller.ReadResult(GetResultPath(layout));

    public static void DismissResult(PortableLayout layout)
    {
        try
        {
            File.Delete(GetResultPath(layout));
        }
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
            if (result.Success && result.BackupPath is { } backup)
            {
                await TryDeleteKnownUpdateFileAsync(layout, backup, allowApplicationRoot: true);
            }
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
            await TryDeleteKnownUpdateFileAsync(
                layout,
                Path.Combine(layout.RootDirectory, $"{BrandInfo.ProductName}.exe.previous"),
                allowApplicationRoot: true);
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

    private static string GetResultPath(PortableLayout layout) =>
        Path.Combine(layout.DataDirectory, ResultFileName);

    private static bool IsInside(string path, string directory)
    {
        var target = Path.GetFullPath(path);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task TryDeleteKnownUpdateFileAsync(
        PortableLayout layout,
        string path,
        bool allowApplicationRoot = false)
    {
        var target = Path.GetFullPath(path);
        var updatesRoot = Path.GetFullPath(Path.Combine(layout.DataDirectory, "updates")) + Path.DirectorySeparatorChar;
        var applicationRoot = Path.GetFullPath(layout.RootDirectory).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var insideUpdates = target.StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase);
        var allowedRootBackup = allowApplicationRoot &&
            target.StartsWith(applicationRoot, StringComparison.OrdinalIgnoreCase) &&
            target.EndsWith(".previous", StringComparison.OrdinalIgnoreCase);
        if (!insideUpdates && !allowedRootBackup) return;
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
        foreach (var file in Directory.EnumerateFiles(updatesRoot, "*.partial", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
