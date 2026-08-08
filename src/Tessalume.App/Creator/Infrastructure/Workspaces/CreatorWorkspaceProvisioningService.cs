using Tessalume.App.Infrastructure;

namespace Tessalume.App.Creator;

internal sealed class CreatorWorkspaceProvisioningService(string applicationRoot)
    : ICreatorWorkspaceProvisioningService
{
    private readonly CreatorWorkspaceProvisioner _provisioner = new(applicationRoot);

    public string CreateWorkspace(string parentDirectory) =>
        CreatorWorkspaceProvisioner.CreateWorkspace(parentDirectory);

    public string ResolveExistingWorkspace(string selectedDirectory) =>
        CreatorWorkspaceProvisioner.ResolveExistingWorkspace(selectedDirectory);

    public CreatorWorkspaceUpgradeResult UpgradeWorkspace(string workspaceDirectory) =>
        CreatorWorkspaceProvisioner.UpgradeWorkspace(workspaceDirectory);

    public string CopyManualTemplate(string destinationRoot) =>
        _provisioner.CopyManualTemplate(destinationRoot);
}
