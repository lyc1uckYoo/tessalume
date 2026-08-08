using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal interface ICreatorProjectInspectionService
{
    Task<CreatorWorkspaceScanResult> ScanWorkspaceAsync(
        string workspaceDirectory,
        CancellationToken cancellationToken = default);

    Task<ThemeProjectSnapshot> ScanProjectAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default);
}
