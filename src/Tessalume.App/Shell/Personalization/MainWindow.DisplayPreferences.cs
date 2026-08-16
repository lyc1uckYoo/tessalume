using Tessalume.App.Features.Personalization;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private void DisplayPreferencesPage_PreferencesChanged(
        object? sender,
        DisplayPreferencesChangedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var settings = GetVisualSettings(themeId);
        var replacement = (settings with { Display = e.Preferences }).Normalize();
        if (ThemeVisualSettingsSemanticComparer.Instance.Equals(replacement, settings)) return;
        SetResolvedVisualSettings(themeId, replacement);
        UpdateArtworkWorkbenchContext();
        MarkVisualSettingsDirtyAndSchedule();
        ArtworkWorkbench.SetApplyState(
            ArtworkApplyState.Pending,
            "等待写入本机配置");
    }
}
