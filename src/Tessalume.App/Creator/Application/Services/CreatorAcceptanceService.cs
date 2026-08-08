using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal sealed class CreatorAcceptanceService(ICreatorRuntimeGateway runtimeGateway)
    : ICreatorAcceptanceService
{
    public Task<CreatorAcceptanceSnapshot> RunAsync(
        ThemeProjectSnapshot project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.Health.CanExport)
        {
            var detail = $"项目体检仍有 {project.Health.ErrorCount} 项阻断错误，运行验收已暂停。";
            return Task.FromResult(new CreatorAcceptanceSnapshot(
                DateTimeOffset.Now,
                CreatorAcceptanceCatalog.CreatePendingChecks()
                    .Select(check => check with
                    {
                        State = CreatorAcceptanceState.Failed,
                        IssueOrigin = CreatorIssueOrigin.ThemeProject,
                        Detail = detail,
                    })
                    .ToArray()));
        }

        return runtimeGateway.RunAcceptanceAsync(project.DirectoryPath, cancellationToken);
    }
}
