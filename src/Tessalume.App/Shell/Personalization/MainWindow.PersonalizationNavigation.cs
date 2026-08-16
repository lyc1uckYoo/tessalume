using System.Windows;
using System.Windows.Media;
using Tessalume.App.Models;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    internal Features.Personalization.DisplayPreferencesView DisplayPreferencesPage =>
        DisplayPreferencesInfoPanel.DisplayPreferencesPage;

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        OpenPersonalizationPage(Features.Navigation.AppRoute.ArtworkStudio);
    }

    private void DisplayPreferences_Click(object sender, RoutedEventArgs e)
    {
        OpenPersonalizationPage(Features.Navigation.AppRoute.DisplayPreferences);
    }

    private void OpenPersonalizationPage(Features.Navigation.AppRoute route)
    {
        UpdateStartupButton();
        UpdateUpdateControls();
        if (_codexDarkMode is { } dark)
        {
            _editingVisualDarkMode = dark;
        }
        NavigateTo(route);
        UpdateVisualAdjustmentControls();
        _ = RefreshCodexColorSchemeAsync();
    }

    private async void SettingsPreviousTheme_Click(object sender, RoutedEventArgs e) =>
        await ApplyRelativeSettingsThemeAsync(-1);

    private async void SettingsNextTheme_Click(object sender, RoutedEventArgs e) =>
        await ApplyRelativeSettingsThemeAsync(1);

    private async Task ApplyRelativeSettingsThemeAsync(int offset)
    {
        var candidates = GetQuickSwitchCandidates();
        if (candidates.Length == 0)
        {
            SetStatus("还没有可切换的有效主题");
            UpdateSettingsVisualHeader();
            return;
        }

        var currentId = _activeThemeId ?? GetVisualAdjustmentTheme()?.ThemeId;
        var currentIndex = Array.FindIndex(candidates, theme =>
            string.Equals(theme.ThemeId, currentId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = offset > 0 ? -1 : 0;
        }

        var nextIndex = (currentIndex + offset + candidates.Length) % candidates.Length;
        var nextTheme = candidates[nextIndex];
        SelectTheme(nextTheme);
        if (await ApplyThemeAsync(nextTheme))
        {
            UpdateVisualAdjustmentControls();
        }
    }

    private async void SettingsColorMode_Click(object sender, RoutedEventArgs e)
    {
        var dark = await ToggleCodexColorSchemeAsync();
        if (dark is null) return;

        _editingVisualDarkMode = dark.Value;
        UpdateVisualAdjustmentControls();
    }

    private ThemeVisualSettings GetVisualSettings(string themeId)
    {
        if (_themeVisualSettings.TryGetValue(themeId, out var settings))
        {
            return settings.Normalize();
        }

        return ResolveVisualSettings(themeId).Settings;
    }

    private ThemeCardModel? GetVisualAdjustmentTheme()
    {
        if (!string.IsNullOrWhiteSpace(_activeThemeId))
        {
            var active = _themes.FirstOrDefault(theme =>
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase));
            if (active is not null) return active;
        }

        return _selectedTheme;
    }

    private void UpdateSettingsVisualHeader()
    {
        if (!_uiInitialized || SettingsThemeControlBar is null) return;

        var candidates = GetQuickSwitchCandidates();
        var adjustmentTheme = GetVisualAdjustmentTheme();
        var activeTheme = string.IsNullOrWhiteSpace(_activeThemeId)
            ? null
            : _themes.FirstOrDefault(theme =>
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase));
        var positionTheme = activeTheme ?? adjustmentTheme;
        var position = positionTheme is null
            ? -1
            : Array.FindIndex(candidates, theme =>
                string.Equals(theme.ThemeId, positionTheme.ThemeId, StringComparison.OrdinalIgnoreCase));

        SettingsCurrentThemeNameText.Text = activeTheme?.Name
            ?? adjustmentTheme?.Name
            ?? "Codex 默认外观";
        SettingsThemeStateText.Text = activeTheme is not null
            ? "已应用到 Codex"
            : adjustmentTheme is not null
                ? "本地编辑 · 尚未应用到 Codex"
                : "还没有可用主题";
        SettingsThemePositionText.Text = position >= 0
            ? $"{position + 1:00} / {candidates.Length:00}"
            : $"— / {candidates.Length:00}";
        SettingsLiveDot.Fill = (Brush)Resources[activeTheme is not null
            ? "Positive"
            : adjustmentTheme is not null ? "Amber" : "SubtleText"];
        SettingsPreviousThemeButton.IsEnabled = candidates.Length > 0;
        SettingsNextThemeButton.IsEnabled = candidates.Length > 0;
        var canRestoreLastTheme = activeTheme is null && _themes.Any(theme =>
            theme.IsValid &&
            string.Equals(theme.ThemeId, _lastThemeId, StringComparison.OrdinalIgnoreCase));
        var restoreLastTheme = activeTheme is null;
        var restoreDescription = restoreLastTheme
            ? canRestoreLastTheme ? "恢复刚刚使用的主题" : "没有可恢复的上一主题"
            : "恢复 Codex 默认外观";
        SettingsRestoreThemeButton.IsEnabled = activeTheme is not null || canRestoreLastTheme;
        SettingsRestoreThemeButton.ToolTip = restoreDescription;
        System.Windows.Automation.AutomationProperties.SetName(
            SettingsRestoreThemeButton,
            restoreDescription);
        SettingsRestoreIconPath.Data = Geometry.Parse(restoreLastTheme
            ? "M 17,8 L 20.5,8 L 20.5,4.5 M 20,8 C 18,4 14,2.5 10,3.5 C 5.5,4.5 3,9 4,13.5 C 5,18 9.5,21 14,20 C 17,19.4 19.2,17.5 20.3,15"
            : "M 7,8 L 3.5,8 L 3.5,4.5 M 4,8 C 6,4 10,2.5 14,3.5 C 18.5,4.5 21,9 20,13.5 C 19,18 14.5,21 10,20 C 7,19.4 4.8,17.5 3.7,15");

        SettingsModeMoonIcon.Visibility = _codexDarkMode is true ? Visibility.Visible : Visibility.Collapsed;
        SettingsModeSunIcon.Visibility = _codexDarkMode is false ? Visibility.Visible : Visibility.Collapsed;
        SettingsModeUnknownText.Visibility = _codexDarkMode is null ? Visibility.Visible : Visibility.Collapsed;
        if (_codexDarkMode is true)
        {
            SettingsColorModeText.Text = "Codex 当前暗色";
            SettingsColorModeHintText.Text = "点击切换到亮色";
            SettingsColorModeButton.Background = (Brush)Resources["SettingsColorSurface"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["SettingsColorBorder"];
            SettingsColorModeButton.ToolTip = "Codex 当前为暗色，点击切换到亮色";
        }
        else if (_codexDarkMode is false)
        {
            SettingsColorModeText.Text = "Codex 当前亮色";
            SettingsColorModeHintText.Text = "点击切换到暗色";
            SettingsColorModeButton.Background = (Brush)Resources["SettingsColorSurface"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["SettingsColorBorder"];
            SettingsColorModeButton.ToolTip = "Codex 当前为亮色，点击切换到暗色";
        }
        else
        {
            SettingsColorModeText.Text = "检测显示模式";
            SettingsColorModeHintText.Text = "点击连接并切换";
            SettingsColorModeButton.Background = (Brush)Resources["SettingsColorSurface"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["SettingsColorBorder"];
            SettingsColorModeButton.ToolTip = "连接 Codex 后读取并切换亮暗模式";
        }

    }
}
