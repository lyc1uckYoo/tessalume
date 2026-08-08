using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal sealed class CreatorProjectInspectionService(ThemeProjectScanner scanner)
    : ICreatorProjectInspectionService
{
    public Task<CreatorWorkspaceScanResult> ScanWorkspaceAsync(
        string workspaceDirectory,
        CancellationToken cancellationToken = default) =>
        scanner.ScanWorkspaceAsync(workspaceDirectory, cancellationToken);

    public Task<ThemeProjectSnapshot> ScanProjectAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default) =>
        scanner.ScanProjectAsync(projectDirectory, cancellationToken);
}
