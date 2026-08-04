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
    Func<CancellationToken, Task<CreatorRuntimeStatus>> toggleColorSchemeAsync)
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

    public static CreatorRuntimeBridge Unavailable { get; } = new(
        (_, _, _) => Task.FromResult(new CreatorRuntimeActionResult(
            false,
            CreatorRuntimeStatus.Disconnected(),
            "当前未连接 Codex。")),
        _ => Task.FromResult(CreatorRuntimeStatus.Disconnected()),
        _ => Task.FromResult(CreatorRuntimeStatus.Disconnected()));
}
