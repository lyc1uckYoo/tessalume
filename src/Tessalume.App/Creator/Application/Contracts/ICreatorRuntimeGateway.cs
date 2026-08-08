namespace Tessalume.App.Creator;

internal interface ICreatorRuntimeGateway
{
    Task<CreatorRuntimeActionResult> ApplyProjectAsync(
        string projectDirectory,
        bool automatic,
        CancellationToken cancellationToken = default);

    Task<CreatorRuntimeStatus> ReadStatusAsync(CancellationToken cancellationToken = default);

    Task<CreatorRuntimeStatus> ToggleColorSchemeAsync(
        CancellationToken cancellationToken = default);

    Task<CreatorAcceptanceSnapshot> RunAcceptanceAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default);
}
