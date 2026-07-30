namespace CodexThemeStudio.Core.Themes;

public enum ThemeValidationSeverity
{
    Warning,
    Error,
}

public sealed record ThemeValidationIssue(
    string Code,
    string Message,
    ThemeValidationSeverity Severity,
    string? Path = null);

public sealed class ThemeValidationResult
{
    private readonly List<ThemeValidationIssue> _issues = [];

    public IReadOnlyList<ThemeValidationIssue> Issues => _issues;

    public bool IsValid => _issues.All(issue => issue.Severity != ThemeValidationSeverity.Error);

    public void AddError(string code, string message, string? path = null) =>
        _issues.Add(new ThemeValidationIssue(code, message, ThemeValidationSeverity.Error, path));

    public void AddWarning(string code, string message, string? path = null) =>
        _issues.Add(new ThemeValidationIssue(code, message, ThemeValidationSeverity.Warning, path));
}
