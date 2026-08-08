using System.Diagnostics;
using System.IO;
using Tessalume.Core.Updates;

namespace Tessalume.App.Infrastructure;

/// <summary>
/// Upgrades the minimal update result written by Tessalume 1.x before any
/// current-version settings migration can modify the legacy configuration.
/// </summary>
internal static class LegacyUpdateRecoveryAdapter
{
    public static async Task<PortableUpdateResult?> PrepareAsync(
        PortableLayout layout,
        PortableUpdateResult? result,
        CancellationToken cancellationToken = default)
    {
        if (result is null || !RequiresLegacySnapshot(layout, result)) return result;

        var snapshots = new UpdateDataSnapshotStore(layout.DataDirectory);
        UpdateDataSnapshotInfo? snapshot = null;
        try
        {
            var previousVersionLabel = ReadExecutableVersionLabel(result.BackupPath!);
            snapshot = await snapshots.CreateAsync(
                Guid.NewGuid().ToString("N"),
                previousVersionLabel,
                cancellationToken);
            var upgraded = result with
            {
                Operation = PortableUpdateOperation.Install,
                PreviousVersionLabel = previousVersionLabel,
                DataSnapshotId = snapshot.SnapshotId,
            };
            await PortableUpdateInstaller.WriteResultAsync(
                Path.Combine(layout.DataDirectory, "update-result.json"),
                upgraded,
                cancellationToken);
            LocalLog.Write(
                $"Legacy update result upgraded with data snapshot {snapshot.SnapshotId}.");
            return upgraded;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            if (snapshot is not null) snapshots.Delete(snapshot.SnapshotId);
            LocalLog.Write("Could not upgrade the legacy update recovery point.", exception);
            return result;
        }
    }

    private static bool RequiresLegacySnapshot(
        PortableLayout layout,
        PortableUpdateResult result)
    {
        if (!result.Success || result.RolledBack ||
            result.Operation != PortableUpdateOperation.Install ||
            !string.IsNullOrWhiteSpace(result.DataSnapshotId) ||
            string.IsNullOrWhiteSpace(result.BackupPath))
        {
            return false;
        }

        try
        {
            var destination = Path.GetFullPath(result.DestinationPath);
            var expectedDestination = Path.GetFullPath(Path.Combine(
                layout.RootDirectory,
                $"{BrandInfo.ProductName}.exe"));
            var backup = Path.GetFullPath(result.BackupPath);
            var expectedBackup = expectedDestination + ".previous";
            return string.Equals(destination, expectedDestination, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(backup, expectedBackup, StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(destination) &&
                   File.Exists(backup) &&
                   IsInside(result.SourcePath, Path.Combine(layout.DataDirectory, "updates", "downloads")) &&
                   IsInside(result.HelperPath, Path.Combine(layout.DataDirectory, "updates", "helpers"));
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static string ReadExecutableVersionLabel(string path)
    {
        try
        {
            var value = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (Version.TryParse(value, out var version))
            {
                return $"v{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or System.ComponentModel.Win32Exception)
        {
        }
        return "v更新前版本";
    }

    private static bool IsInside(string path, string directory)
    {
        var target = Path.GetFullPath(path);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
