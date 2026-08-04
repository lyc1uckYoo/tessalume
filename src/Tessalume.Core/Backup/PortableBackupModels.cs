namespace Tessalume.Core.Backup;

public sealed record PortableBackupOptions
{
    public bool IncludeImportedThemes { get; init; }
}

public sealed record PortableBackupThemeSummary(
    string DirectoryName,
    string? ThemeId,
    string DisplayName,
    string? Version,
    int FileCount,
    long TotalBytes);

public sealed record PortableBackupSummary(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    bool IncludesImportedThemes,
    int DataFileCount,
    long DataBytes,
    int TotalFileCount,
    long TotalBytes,
    IReadOnlyList<PortableBackupThemeSummary> ImportedThemes);

public sealed record PortableBackupResult(
    string ArchivePath,
    string Sha256,
    long CompressedBytes,
    PortableBackupSummary Summary);

public sealed record PortableRestoreResult(
    PortableBackupSummary Summary,
    string AutomaticSnapshotPath);
