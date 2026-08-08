using Tessalume.Core.Backup;

namespace Tessalume.App.Features.About;

internal sealed class AboutDataService
{
    private readonly PortableBackupService _backupService;

    public AboutDataService(
        string rootDirectory,
        string dataDirectory,
        string themesDirectory,
        IReadOnlySet<string> builtInThemeIds)
    {
        _backupService = new PortableBackupService(
            rootDirectory,
            dataDirectory,
            themesDirectory,
            builtInThemeIds);
    }

    public Task<PortableBackupResult> CreateBackupAsync(
        string archivePath,
        bool includeImportedThemes,
        CancellationToken cancellationToken) =>
        _backupService.CreateAsync(
            archivePath,
            new PortableBackupOptions
            {
                IncludeImportedThemes = includeImportedThemes,
            },
            cancellationToken);

    public static Task<PortableBackupSummary> InspectBackupAsync(
        string archivePath,
        CancellationToken cancellationToken) =>
        PortableBackupService.InspectAsync(archivePath, cancellationToken);

    public Task<PortableRestoreResult> RestoreBackupAsync(
        string archivePath,
        CancellationToken cancellationToken) =>
        _backupService.RestoreAsync(archivePath, cancellationToken);
}
