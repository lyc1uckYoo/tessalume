namespace Tessalume.Core.Pets;

public enum PetValidationSeverity
{
    Warning,
    Error,
}

public sealed record PetValidationIssue(
    string Code,
    string Message,
    PetValidationSeverity Severity,
    string? Path = null);

public sealed class PetValidationResult
{
    private readonly List<PetValidationIssue> _issues = [];

    public IReadOnlyList<PetValidationIssue> Issues => _issues;

    public bool IsValid => _issues.All(issue => issue.Severity != PetValidationSeverity.Error);

    public void AddError(string code, string message, string? path = null) =>
        _issues.Add(new PetValidationIssue(code, message, PetValidationSeverity.Error, path));

    public void AddWarning(string code, string message, string? path = null) =>
        _issues.Add(new PetValidationIssue(code, message, PetValidationSeverity.Warning, path));
}
