using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private void UpdateVisualAdjustmentControls()
    {
        if (!_uiInitialized || ArtworkWorkbench is null) return;
        UpdateArtworkWorkbenchContext();
        var theme = GetVisualAdjustmentTheme();
        var available = theme?.ThemeId is { Length: > 0 };
        UpdateSettingsVisualHeader();
        DisplayPreferencesPage.Render(
            available
                ? GetVisualSettings(theme!.ThemeId!).Display
                : new ThemeDisplayPreferences(),
            enabled: available);
    }
}
