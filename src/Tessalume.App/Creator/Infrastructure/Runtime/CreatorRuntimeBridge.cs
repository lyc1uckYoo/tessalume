namespace Tessalume.App.Creator;

internal sealed record CreatorRuntimeStatus(
    bool IsConnected,
    int? Port,
    bool? IsDarkMode,
    string? Detail = null)
{
    public static CreatorRuntimeStatus Disconnected(string? detail = null) =>
        new(false, null, null, detail);
}

internal sealed record CreatorRuntimeActionResult(
    bool Succeeded,
    CreatorRuntimeStatus Status,
    string Message);

internal sealed class CreatorRuntimeBridge(
    Func<string, bool, CancellationToken, Task<CreatorRuntimeActionResult>> applyProjectAsync,
    Func<CancellationToken, Task<CreatorRuntimeStatus>> readStatusAsync,
    Func<CancellationToken, Task<CreatorRuntimeStatus>> toggleColorSchemeAsync,
    Func<string, CancellationToken, Task<CreatorAcceptanceSnapshot>> runAcceptanceAsync)
    : ICreatorRuntimeGateway
{
    public Task<CreatorRuntimeActionResult> ApplyProjectAsync(
        string projectDirectory,
        bool automatic,
        CancellationToken cancellationToken = default) =>
        applyProjectAsync(projectDirectory, automatic, cancellationToken);

    public Task<CreatorRuntimeStatus> ReadStatusAsync(CancellationToken cancellationToken = default) =>
        readStatusAsync(cancellationToken);

    public Task<CreatorRuntimeStatus> ToggleColorSchemeAsync(
        CancellationToken cancellationToken = default) =>
        toggleColorSchemeAsync(cancellationToken);

    public Task<CreatorAcceptanceSnapshot> RunAcceptanceAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default) =>
        runAcceptanceAsync(projectDirectory, cancellationToken);

    public static CreatorRuntimeBridge Unavailable { get; } = new(
        (_, _, _) => Task.FromResult(new CreatorRuntimeActionResult(
            false,
            CreatorRuntimeStatus.Disconnected(),
            "当前未连接 Codex。")),
        _ => Task.FromResult(CreatorRuntimeStatus.Disconnected()),
        _ => Task.FromResult(CreatorRuntimeStatus.Disconnected()),
        (_, _) => Task.FromResult(new CreatorAcceptanceSnapshot(
            DateTimeOffset.Now,
            CreatorAcceptanceCatalog.CreatePendingChecks()
                .Select(check => check with
                {
                    State = CreatorAcceptanceState.Failed,
                    IssueOrigin = CreatorIssueOrigin.RuntimeCompatibility,
                    Detail = "Codex 尚未连接，无法执行运行验收。",
                })
                .ToArray())));
}
