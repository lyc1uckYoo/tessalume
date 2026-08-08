using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal interface ICreatorProjectExportService
{
    Task<ThemeArchiveExportResult> ExportAsync(
        string projectDirectory,
        string archivePath,
        CancellationToken cancellationToken = default);
}
