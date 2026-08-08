using Tessalume.Core.Compatibility;
using Tessalume.Core.Updates;

namespace Tessalume.App.Features.About;

public sealed record AboutOverview(
    string RootDirectory,
    string DataDirectory,
    int ThemeCount,
    int ValidThemeCount,
    int FavoriteThemeCount);

public sealed record AboutRollbackState(
    string Status,
    string ToolTip,
    bool IsAvailable,
    bool IsBusy);

public sealed record AboutUpdateState(
    bool AutomaticChecksEnabled,
    bool IsChecking,
    string Status,
    AboutRollbackState Rollback);

public sealed record AboutUpdateCheckResult(
    ReleaseUpdate? ApplicationUpdate,
    CompatibilityPackInstallResult? CompatibilityUpdate,
    Exception? CompatibilityError);

public sealed class AboutBooleanSettingChangedEventArgs(bool enabled) : EventArgs
{
    public bool Enabled { get; } = enabled;
}
