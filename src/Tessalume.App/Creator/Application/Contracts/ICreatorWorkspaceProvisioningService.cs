using Tessalume.App.Infrastructure;

namespace Tessalume.App.Creator;

internal interface ICreatorWorkspaceProvisioningService
{
    string CreateWorkspace(string parentDirectory);

    string ResolveExistingWorkspace(string selectedDirectory);

    CreatorWorkspaceUpgradeResult UpgradeWorkspace(string workspaceDirectory);

    string CopyManualTemplate(string destinationRoot);
}
