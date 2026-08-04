namespace Tessalume.Core.Runtime;

public enum ThemeRuntimeFailureStage
{
    None,
    CodexNotFound,
    PortUnavailable,
    PageTargetsMissing,
    ResourcePreflightFailed,
    RuntimeInjectionFailed,
    ThemeScriptFailed,
}

public sealed class ThemeRuntimeException(
    ThemeRuntimeFailureStage stage,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public ThemeRuntimeFailureStage Stage { get; } = stage;
}
