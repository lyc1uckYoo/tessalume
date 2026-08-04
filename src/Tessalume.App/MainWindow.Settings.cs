using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;
using Tessalume.Core.Updates;
using Microsoft.Win32;

namespace Tessalume.App;

public partial class MainWindow
{
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        UpdateStartupButton();
        UpdateUpdateControls();
        if (_codexDarkMode is { } dark)
        {
            _editingVisualDarkMode = dark;
        }
        ShowInfoPage(RightPane.Settings);
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
        _updatingStartupSetting = true;
        StartupCheckBox.IsChecked = enabled;
        _updatingStartupSetting = false;
    }

    private void UpdateQuickSwitchButton()
    {
        if (!_uiInitialized || QuickSwitchButton is null)
        {
            return;
        }

        var enabled = _quickSwitchWindow is { IsVisible: true };
        QuickSwitchButton.Tag = enabled ? "active" : "inactive";
        QuickSwitchButton.Content = enabled ? "浮窗已开启" : "打开主题浮窗";
        QuickSwitchButton.ToolTip = enabled ? "点击关闭主题浮窗" : "点击打开主题浮窗";
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingStartupSetting)
        {
            return;
        }

        try
        {
            var enabled = StartupCheckBox.IsChecked == true;
            StartupRegistration.SetEnabled(enabled);
            UpdateStartupButton();
            StatusText.Text = enabled ? "已启用开机自动启动" : "已关闭开机自动启动";
        }
        catch (Exception exception)
        {
            _updatingStartupSetting = true;
            StartupCheckBox.IsChecked = StartupRegistration.IsEnabled();
            _updatingStartupSetting = false;
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
        FavoriteThemeIds = _favoriteThemeIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
        ThemeVisualSettings = _themeVisualSettings.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Normalize(),
            StringComparer.OrdinalIgnoreCase),
    });

    private ThemeVisualSettings GetVisualSettings(string themeId)
    {
        if (_themeVisualSettings.TryGetValue(themeId, out var settings))
        {
            return settings.Normalize();
        }

        settings = new ThemeVisualSettings();
        _themeVisualSettings[themeId] = settings;
        return settings;
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

    private void VisualAdjustmentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVisualControls || sender is not Slider { Tag: string tag }) return;
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        var parts = tag.Split('.', 2);
        if (parts.Length != 2) return;

        var settings = GetVisualSettings(themeId);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        var adjustment = parts[0] switch
        {
            "hero" => mode.Hero,
            "sidebar" => mode.Sidebar,
            "chat" => mode.Chat,
            _ => null,
        };
        if (adjustment is null) return;

        adjustment = parts[1] switch
        {
            "brightness" => adjustment with { Brightness = e.NewValue },
            "contrast" => adjustment with { Contrast = e.NewValue },
            "saturation" => adjustment with { Saturation = e.NewValue },
            "opacity" => adjustment with { Opacity = e.NewValue },
            _ => adjustment,
        };
        mode = parts[0] switch
        {
            "hero" => mode with { Hero = adjustment },
            "sidebar" => mode with { Sidebar = adjustment },
            "chat" => mode with { Chat = adjustment },
            _ => mode,
        };
        _themeVisualSettings[themeId] = (_editingVisualDarkMode
            ? settings with { Dark = mode }
            : settings with { Light = mode }).Normalize();
        UpdateVisualAdjustmentLabels();
        ScheduleVisualSettingsUpdate();
    }

    private void ResetVisualRegion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string region }) return;
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        var settings = GetVisualSettings(themeId);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        mode = region switch
        {
            "hero" => mode with { Hero = new ThemeArtworkAdjustment() },
            "sidebar" => mode with { Sidebar = new ThemeArtworkAdjustment() },
            "chat" => mode with { Chat = new ThemeArtworkAdjustment() },
            _ => mode,
        };
        _themeVisualSettings[themeId] = _editingVisualDarkMode
            ? settings with { Dark = mode }
            : settings with { Light = mode };
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
    }

    private void ResetAllVisualSettings_Click(object sender, RoutedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        _themeVisualSettings[themeId] = new ThemeVisualSettings();
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
    }

    private void ScheduleVisualSettingsUpdate()
    {
        if (_visualSettingsDebounce is null) return;
        _visualSettingsDebounce.Stop();
        _visualSettingsDebounce.Start();
    }

    private async void VisualSettingsDebounce_Tick(object? sender, EventArgs e)
    {
        _visualSettingsDebounce?.Stop();
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        try
        {
            await SavePreferencesAsync();
            if (!string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"{theme.Name} 的图像参数已保存，应用主题后生效");
                return;
            }

            var state = await _stateStore.LoadAsync();
            var port = _activePort ?? state?.Port;
            if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
            {
                SetStatus("图像参数已保存；Codex 下次连接时自动生效");
                return;
            }

            await _runtime.ApplyVisualSettingsAsync(port.Value, themeId, GetVisualSettings(themeId));
            SetStatus($"已实时更新 {theme.Name} 的图像参数");
        }
        catch (Exception exception)
        {
            SetStatus($"图像参数已保留，但实时更新失败：{exception.Message}");
        }
    }

    private void UpdateVisualAdjustmentControls()
    {
        if (!_uiInitialized || VisualAdjustmentEditor is null) return;
        var theme = GetVisualAdjustmentTheme();
        var available = theme?.ThemeId is { Length: > 0 };
        VisualAdjustmentEditor.IsEnabled = available;
        var isApplied = available && string.Equals(
            theme!.ThemeId,
            _activeThemeId,
            StringComparison.OrdinalIgnoreCase);
        VisualThemeNameText.Text = available
            ? isApplied
                ? $"{theme!.Name} · 当前修改会立即显示在 Codex 中"
                : $"{theme!.Name} · 参数会保存并在应用主题时生效"
            : "请先在主题画廊中选择一个有效主题";
        VisualEditingModeText.Text = _codexDarkMode is null
            ? $"{(_editingVisualDarkMode ? "暗色" : "亮色")}参数 · 待检测"
            : _editingVisualDarkMode ? "暗色参数" : "亮色参数";
        VisualEditingModeBadge.Background = (Brush)Resources[_editingVisualDarkMode ? "AccentSoft" : "SkySoft"];
        VisualEditingModeBadge.BorderBrush = (Brush)Resources[_editingVisualDarkMode ? "Accent" : "Sky"];
        VisualEditingModeText.Foreground = (Brush)Resources[_editingVisualDarkMode ? "Accent" : "Sky"];
        UpdateSettingsVisualHeader();
        if (!available) return;

        var settings = GetVisualSettings(theme!.ThemeId!);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        _updatingVisualControls = true;
        try
        {
            SetAdjustmentControls(mode.Hero, HeroBrightnessSlider, HeroContrastSlider, HeroSaturationSlider, HeroOpacitySlider);
            SetAdjustmentControls(mode.Sidebar, SidebarBrightnessSlider, SidebarContrastSlider, SidebarSaturationSlider, SidebarOpacitySlider);
            SetAdjustmentControls(mode.Chat, ChatBrightnessSlider, ChatContrastSlider, ChatSaturationSlider, ChatOpacitySlider);
            UpdateVisualAdjustmentLabels();
        }
        finally
        {
            _updatingVisualControls = false;
        }
    }

    private static void SetAdjustmentControls(
        ThemeArtworkAdjustment adjustment,
        Slider brightness,
        Slider contrast,
        Slider saturation,
        Slider opacity)
    {
        brightness.Value = adjustment.Brightness;
        contrast.Value = adjustment.Contrast;
        saturation.Value = adjustment.Saturation;
        opacity.Value = adjustment.Opacity;
    }

    private void UpdateVisualAdjustmentLabels()
    {
        if (!_uiInitialized) return;
        HeroBrightnessValue.Text = $"{HeroBrightnessSlider.Value:0}%";
        HeroContrastValue.Text = $"{HeroContrastSlider.Value:0}%";
        HeroSaturationValue.Text = $"{HeroSaturationSlider.Value:0}%";
        HeroOpacityValue.Text = $"{HeroOpacitySlider.Value:0}%";
        SidebarBrightnessValue.Text = $"{SidebarBrightnessSlider.Value:0}%";
        SidebarContrastValue.Text = $"{SidebarContrastSlider.Value:0}%";
        SidebarSaturationValue.Text = $"{SidebarSaturationSlider.Value:0}%";
        SidebarOpacityValue.Text = $"{SidebarOpacitySlider.Value:0}%";
        ChatBrightnessValue.Text = $"{ChatBrightnessSlider.Value:0}%";
        ChatContrastValue.Text = $"{ChatContrastSlider.Value:0}%";
        ChatSaturationValue.Text = $"{ChatSaturationSlider.Value:0}%";
        ChatOpacityValue.Text = $"{ChatOpacitySlider.Value:0}%";
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

        SettingsCurrentThemeNameText.Text = activeTheme?.Name ?? "Codex 默认外观";
        SettingsThemeStateText.Text = activeTheme is not null
            ? "已应用 · 下方调节实时生效"
            : adjustmentTheme is not null
                ? $"默认外观 · 待应用 {adjustmentTheme.Name}"
                : "还没有可用主题";
        SettingsThemePositionText.Text = position >= 0
            ? $"{position + 1:00} / {candidates.Length:00}"
            : $"— / {candidates.Length:00}";
        SettingsLiveDot.Fill = (Brush)Resources[activeTheme is not null
            ? "Positive"
            : adjustmentTheme is not null ? "Amber" : "SubtleText"];
        SettingsPreviousThemeButton.IsEnabled = candidates.Length > 0;
        SettingsNextThemeButton.IsEnabled = candidates.Length > 0;

        SettingsModeMoonIcon.Visibility = _codexDarkMode is true ? Visibility.Visible : Visibility.Collapsed;
        SettingsModeSunIcon.Visibility = _codexDarkMode is false ? Visibility.Visible : Visibility.Collapsed;
        SettingsModeUnknownText.Visibility = _codexDarkMode is null ? Visibility.Visible : Visibility.Collapsed;
        if (_codexDarkMode is true)
        {
            SettingsColorModeText.Text = "Codex 当前暗色";
            SettingsColorModeHintText.Text = "点击切换到亮色";
            SettingsColorModeButton.Background = (Brush)Resources["AccentSoft"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["Accent"];
            SettingsColorModeButton.ToolTip = "Codex 当前为暗色，点击切换到亮色";
        }
        else if (_codexDarkMode is false)
        {
            SettingsColorModeText.Text = "Codex 当前亮色";
            SettingsColorModeHintText.Text = "点击切换到暗色";
            SettingsColorModeButton.Background = (Brush)Resources["SkySoft"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["Sky"];
            SettingsColorModeButton.ToolTip = "Codex 当前为亮色，点击切换到暗色";
        }
        else
        {
            SettingsColorModeText.Text = "检测显示模式";
            SettingsColorModeHintText.Text = "点击连接并切换";
            SettingsColorModeButton.Background = (Brush)Resources["SettingsControlSurface"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["SettingsControlBorder"];
            SettingsColorModeButton.ToolTip = "连接 Codex 后读取并切换亮暗模式";
        }
    }

}
