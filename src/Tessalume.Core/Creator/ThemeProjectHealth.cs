namespace Tessalume.Core.Creator;

public enum ThemeProjectHealthSeverity
{
    Passed,
    Warning,
    Error,
}

public enum ThemeProjectHealthGroup
{
    Workspace,
    Manifest,
    EntryPoints,
    Assets,
    Artwork,
    Previews,
    Template,
    Css,
    Script,
    Resources,
}

public enum ThemeProjectState
{
    Ready,
    NeedsAttention,
    Invalid,
}

public sealed record ThemeProjectHealthCheck(
    ThemeProjectHealthGroup Group,
    string Code,
    string Title,
    string Message,
    ThemeProjectHealthSeverity Severity,
    string? FilePath = null,
    string? SuggestedAction = null);

public sealed class ThemeProjectHealthReport(IEnumerable<ThemeProjectHealthCheck> checks)
{
    private readonly IReadOnlyList<ThemeProjectHealthCheck> _checks = checks.ToArray();

    public IReadOnlyList<ThemeProjectHealthCheck> Checks => _checks;

    public int ErrorCount => _checks.Count(check => check.Severity == ThemeProjectHealthSeverity.Error);

    public int WarningCount => _checks.Count(check => check.Severity == ThemeProjectHealthSeverity.Warning);

    public int PassedCount => _checks.Count(check => check.Severity == ThemeProjectHealthSeverity.Passed);

    public bool CanExport => ErrorCount == 0;

    public ThemeProjectState State => ErrorCount > 0
        ? ThemeProjectState.Invalid
        : WarningCount > 0
            ? ThemeProjectState.NeedsAttention
            : ThemeProjectState.Ready;
}

public sealed record ThemeProjectSnapshot(
    string DirectoryPath,
    string DirectoryName,
    string? ThemeId,
    string DisplayName,
    string? CharacterName,
    string? Version,
    string? Author,
    bool SupportsLight,
    bool SupportsDark,
    int AssetCount,
    DateTimeOffset LastModifiedAt,
    ThemeProjectHealthReport Health)
{
    public IReadOnlyList<string> WatchedFiles { get; init; } = [];
}

public sealed record CreatorWorkspaceScanResult(
    string WorkspaceDirectory,
    string ThemesDirectory,
    IReadOnlyList<ThemeProjectSnapshot> Projects,
    ThemeProjectHealthReport Health)
{
    public bool Exists => Directory.Exists(WorkspaceDirectory);

    public CreatorWorkspaceContractInfo Contract { get; init; } = new(
        CreatorWorkspaceContractState.Missing,
        null,
        null,
        "尚未读取工作区版本。");
}
