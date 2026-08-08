using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal interface ICreatorAcceptanceService
{
    Task<CreatorAcceptanceSnapshot> RunAsync(
        ThemeProjectSnapshot project,
        CancellationToken cancellationToken = default);
}
