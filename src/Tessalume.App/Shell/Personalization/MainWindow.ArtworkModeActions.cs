using System.Windows;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private void ResetAllVisualSettings_Click(object sender, RoutedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var settings = GetVisualSettings(themeId);
        var replacement = settings with
        {
            Light = new ThemeVisualModeSettings(),
            Dark = new ThemeVisualModeSettings(),
        };
        replacement = replacement.Normalize();
        if (settings.Normalize() == replacement) return;
        RecordVisualUndo(themeId, settings);
        _themeVisualSettings[themeId] = replacement;
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
        ShowToast("当前主题的全部图像参数已重置 · 可撤销");
    }

    private void CopyVisualMode_Click(object sender, RoutedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var settings = GetVisualSettings(themeId);
        var replacement = _editingVisualDarkMode
            ? settings with { Light = settings.Dark }
            : settings with { Dark = settings.Light };
        replacement = replacement.Normalize();
        if (replacement == settings.Normalize())
        {
            ShowToast("另一显示模式已经使用相同参数");
            return;
        }

        RecordVisualUndo(themeId, settings);
        _themeVisualSettings[themeId] = replacement;
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
        ShowToast(_editingVisualDarkMode ? "已复制到亮色参数" : "已复制到暗色参数");
    }
}
