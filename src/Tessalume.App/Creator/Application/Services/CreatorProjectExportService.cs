using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal sealed class CreatorProjectExportService(ThemeArchiveWriter archiveWriter)
    : ICreatorProjectExportService
{
    public Task<ThemeArchiveExportResult> ExportAsync(
        string projectDirectory,
        string archivePath,
        CancellationToken cancellationToken = default) =>
        archiveWriter.ExportAsync(projectDirectory, archivePath, cancellationToken);
}
