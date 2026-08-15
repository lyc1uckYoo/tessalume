using System.Windows;
using Tessalume.App.Features.About;
using Tessalume.App.Infrastructure;

namespace Tessalume.App;

public partial class MainWindow
{
    private void QuickSwitchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quickSwitchWindow is { IsVisible: true })
        {
            _quickSwitchWindow.Close();
            return;
        }

        OpenQuickSwitchWindow();
    }

    private void StartupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var enabled = !StartupRegistration.IsEnabled();
            StartupRegistration.SetEnabled(enabled);
            UpdateStartupButton();
            StatusText.Text = enabled ? "已启用开机自动启动" : "已关闭开机自动启动";
            ShowToast(enabled ? "已启用开机自动启动" : "已关闭开机自动启动");
        }
        catch (Exception exception)
        {
            UpdateStartupButton();
            ShowProductMessage("无法更新开机启动设置", exception.Message, ProductDialogKind.Error);
        }
    }

    private void UpdateStartupButton()
    {
        if (!_uiInitialized || StartupButton is null) return;
        var enabled = StartupRegistration.IsEnabled();
        StartupButton.Tag = enabled ? "active" : "inactive";
        StartupButton.Content = enabled ? "开机启动已开启" : "开启开机启动";
        StartupButton.ToolTip = enabled ? "点击关闭登录 Windows 后自动启动" : "点击开启登录 Windows 后自动启动";
        AboutPage.SetStartupEnabled(enabled);
    }

    private void UpdateQuickSwitchButton()
    {
        if (!_uiInitialized || QuickSwitchButton is null) return;

        var enabled = _quickSwitchWindow is { IsVisible: true };
        QuickSwitchButton.Tag = enabled ? "active" : "inactive";
        QuickSwitchButton.Content = enabled ? "浮窗已开启" : "打开主题浮窗";
        QuickSwitchButton.ToolTip = enabled ? "点击关闭主题浮窗" : "点击打开主题浮窗";
    }

    private void AboutPage_StartupSettingChanged(
        object? sender,
        AboutBooleanSettingChangedEventArgs e)
    {
        try
        {
            var enabled = e.Enabled;
            StartupRegistration.SetEnabled(enabled);
            UpdateStartupButton();
            StatusText.Text = enabled ? "已启用开机自动启动" : "已关闭开机自动启动";
        }
        catch (Exception exception)
        {
            AboutPage.SetStartupEnabled(StartupRegistration.IsEnabled());
            UpdateStartupButton();
            ShowProductMessage("无法更新开机启动设置", exception.Message, ProductDialogKind.Error);
        }
    }

    private Task SavePreferencesAsync() => _preferencesStore.SaveAsync(new UiPreferences
    {
        DarkMode = _darkMode,
        OnboardingCompleted = _onboardingCompleted,
        AutomaticUpdateChecks = _automaticUpdateChecks,
        LastUpdateCheckAt = _lastUpdateCheckAt,
        RecentCreatorWorkspaces = _creatorWorkspaces.Snapshot(),
        CreatorPromptDrafts = _creatorPromptDrafts.Snapshot(),
        FavoriteThemeIds = _favoriteThemeIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
        ThemeLibrarySort = ThemeLibraryState.NormalizeSort(_themeLibrarySort),
        RecentThemeUsage = ThemeLibraryState.NormalizeUsage(_themeUsage.Values),
        ThemeVisualOverrides = SnapshotVisualOverrides().ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Normalize(),
            StringComparer.OrdinalIgnoreCase),
        ArtworkPresets = _artworkPresets.Select(preset => preset.Normalize()).ToList(),
        ExperiencePresets = _experiencePresets.Select(preset => preset.Normalize()).ToList(),
    });
}
