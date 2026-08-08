using System.Security.Cryptography;
using System.Text.Json;

namespace Tessalume.Core.Backup;

public sealed partial class PortableBackupService
{
    private static PortableBackupSummary ToSummary(BackupManifest manifest)
    {
        var dataFiles = manifest.Files.Where(file => file.Path.StartsWith("data/", StringComparison.OrdinalIgnoreCase)).ToArray();
        return new PortableBackupSummary(
            manifest.SchemaVersion,
            manifest.CreatedAt,
            manifest.IncludesImportedThemes,
            dataFiles.Length,
            dataFiles.Sum(file => file.Size),
            manifest.Files.Count,
            manifest.Files.Sum(file => file.Size),
            manifest.Themes.Select(theme => new PortableBackupThemeSummary(
                theme.DirectoryName,
                theme.ThemeId,
                theme.DisplayName,
                theme.Version,
                theme.FileCount,
                theme.TotalBytes)).ToArray());
    }

    private static async Task<ThemeMetadata> ReadThemeMetadataAsync(
        string themeDirectory,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(themeDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new ThemeMetadata(null, Path.GetFileName(themeDirectory), null);
        }
        try
        {
            await using var stream = OpenSourceRead(manifestPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
            var name = root.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
            var version = root.TryGetProperty("version", out var versionProperty) ? versionProperty.GetString() : null;
            return new ThemeMetadata(
                string.IsNullOrWhiteSpace(id) ? null : id,
                string.IsNullOrWhiteSpace(name) ? Path.GetFileName(themeDirectory) : name,
                string.IsNullOrWhiteSpace(version) ? null : version);
        }
        catch (JsonException)
        {
            return new ThemeMetadata(null, Path.GetFileName(themeDirectory), null);
        }
    }

    private static FileStream OpenSourceRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read | FileShare.Delete,
        81920,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = OpenSourceRead(path);
        return await ComputeHashAsync(stream, cancellationToken);
    }

    private static async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed record SourceFile(
        string SourcePath,
        string ArchivePath,
        long Size,
        string? ThemeDirectoryName);

    private sealed record ThemeMetadata(string? ThemeId, string DisplayName, string? Version);

    private sealed record BackupFileManifest(string Path, long Size, string Sha256);

    private sealed record BackupThemeManifest(
        string DirectoryName,
        string? ThemeId,
        string DisplayName,
        string? Version,
        int FileCount,
        long TotalBytes);

    private sealed record BackupManifest(
        int SchemaVersion,
        DateTimeOffset CreatedAt,
        bool IncludesImportedThemes,
        IReadOnlyList<BackupFileManifest> Files,
        IReadOnlyList<BackupThemeManifest> Themes);

    private sealed record ArchiveInspection(BackupManifest Manifest);

    private sealed class RestoreTarget(
        string stagedPath,
        string targetPath,
        string rollbackPath,
        bool isDirectory)
    {
        public string StagedPath { get; } = stagedPath;
        public string TargetPath { get; } = targetPath;
        public string RollbackPath { get; } = rollbackPath;
        public bool IsDirectory { get; } = isDirectory;
        public bool HadOriginal { get; set; }
        public bool Installed { get; set; }
    }
}
