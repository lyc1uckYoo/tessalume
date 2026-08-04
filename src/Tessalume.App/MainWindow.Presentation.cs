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
    private void ApplyStudioTheme(bool dark)
    {
        SetGradientBrush(
            "WindowBackground",
            dark ? "#0D1017" : "#F6F8FC",
            dark ? "#111620" : "#F0F3F8");
        SetGradientBrush(
            "SidebarBackground",
            dark ? "#12161F" : "#FCFDFE",
            dark ? "#171C26" : "#F7F9FC");
        SetGradientBrush(
            "PrimaryGradient",
            dark ? "#7480F4" : "#5968EA",
            dark ? "#A16BE7" : "#8A5FDF");
        SetGradientBrush(
            "PrimaryActionGradient",
            dark ? "#736BFA" : "#615FE8",
            dark ? "#A45AD9" : "#8C58D5");
        SetGradientBrush(
            "AdvancedPreview",
            dark ? "#17212C" : "#E8EDF3",
            dark ? "#33475C" : "#BECBD8");
        SetGradientBrush(
            "SettingsControlBar",
            dark ? "#211827" : "#F0F2F8",
            dark ? "#35243E" : "#E7EAF4");
        SetGradientBrush(
            "SettingsCurrentThemeGradient",
            dark ? "#3A2B45" : "#FFFFFF",
            dark ? "#2A233A" : "#E9E7F8");
        SetBrush("Surface", dark ? "#1C222C" : "#FFFFFF");
        SetBrush("SurfaceAlt", dark ? "#252C37" : "#F2F4F8");
        SetBrush("SurfaceElevated", dark ? "#202732" : "#FFFFFF");
        SetBrush("HoverSurface", dark ? "#2C3441" : "#E9ECF3");
        SetBrush("InfoSurface", dark ? "#292D46" : "#F0EFFF");
        SetBrush("InfoBorder", dark ? "#464C71" : "#DAD8F7");
        SetBrush("PrimaryText", dark ? "#EFF2F8" : "#171927");
        SetBrush("MutedText", dark ? "#ADB6C6" : "#62697A");
        SetBrush("SubtleText", dark ? "#858FA1" : "#9299AA");
        SetBrush("Border", dark ? "#353E4E" : "#DDE2EC");
        SetBrush("Accent", dark ? "#978BFF" : "#675CF0");
        SetBrush("AccentSoft", dark ? "#332F58" : "#EFEDFF");
        SetBrush("ActiveNav", dark ? "#302D50" : "#EFEEFF");
        SetBrush("Positive", dark ? "#55D6A6" : "#24B987");
        SetBrush("Danger", dark ? "#FF829E" : "#D94C70");
        SetBrush("DangerSoft", dark ? "#38232B" : "#FFF0F4");
        SetBrush("Sky", dark ? "#8EAAFF" : "#4D7FE8");
        SetBrush("SkySoft", dark ? "#293752" : "#EEF4FF");
        SetBrush("Amber", dark ? "#F1B85B" : "#D88A24");
        SetBrush("AmberSoft", dark ? "#3A3020" : "#FFF5E7");
        SetBrush("Rose", dark ? "#F58CB6" : "#D9598C");
        SetBrush("RoseSoft", dark ? "#412737" : "#FFF0F7");
        SetBrush("Teal", dark ? "#55D4D1" : "#159A9C");
        SetBrush("TealSoft", dark ? "#203B3D" : "#EAF9F7");
        SetBrush("SettingsBarBorder", dark ? "#61815B8C" : "#BAC4D8");
        SetBrush("SettingsBarPrimaryText", dark ? "#FFF7FA" : "#25293B");
        SetBrush("SettingsBarMutedText", dark ? "#B8DFD4E5" : "#697087");
        SetBrush("SettingsControlSurface", dark ? "#18FFFFFF" : "#F9FBFE");
        SetBrush("SettingsControlBorder", dark ? "#32FFFFFF" : "#C7CDDE");
        SetBrush("SettingsControlHover", dark ? "#29FFFFFF" : "#FFFFFF");
        SetBrush("SettingsTrack", dark ? "#46505D" : "#D9DDE8");
        SetBrush("DropOverlayBackground", dark ? "#F51A202B" : "#F5F7FFF5");
        Resources["SettingsBarShadow"] = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = (Color)ColorConverter.ConvertFromString(dark ? "#120B18" : "#59637A"),
            BlurRadius = dark ? 28 : 24,
            ShadowDepth = dark ? 8 : 7,
            Opacity = dark ? 0.4 : 0.18,
        };
        if (SettingsThemeControlBar is not null)
        {
            SettingsThemeControlBar.Effect = (System.Windows.Media.Effects.Effect)Resources["SettingsBarShadow"];
        }
        foreach (var theme in _themes)
        {
            theme.SetDarkMode(dark);
        }
        _quickSwitchWindow?.SetShellTheme(dark);
        SetEngineState(_engineStateText);
        NativeTitleBar.Apply(this, dark);
        UpdateCategoryButtons();
        UpdateModeButtons();
        UpdateStartupButton();
        UpdateQuickSwitchButton();
        UpdateCodexModeButton();
        UpdateVisualAdjustmentGroup();
        if (_uiInitialized && AllThemesFilterButton is not null)
        {
            UpdateThemeFilterUi(_showFavorites
                ? _themes.Count(theme => theme.IsFavorite)
                : _themes.Count);
        }
    }

    private void SetBrush(string key, string color) =>
        Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private void SetGradientBrush(string key, string startColor, string endColor) =>
        Resources[key] = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(startColor),
            (Color)ColorConverter.ConvertFromString(endColor),
            new Point(0, 0),
            new Point(1, 1));

    private void UpdateAppliedThemeState()
    {
        foreach (var theme in _themes)
        {
            theme.IsApplied = !string.IsNullOrWhiteSpace(_activeThemeId) &&
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateCategoryButtons()
    {
        if (ThemesButton is null || FavoritesButton is null) return;
        var themesActive = _rightPane == RightPane.Themes && !_showFavorites;
        ThemesButton.Background = themesActive ? (Brush)Resources["ActiveNav"] : Brushes.Transparent;
        FavoritesButton.Background = _rightPane == RightPane.Themes && _showFavorites ? (Brush)Resources["ActiveNav"] : Brushes.Transparent;
        ThemesButton.Foreground = (Brush)Resources[themesActive ? "Accent" : "MutedText"];
        FavoritesButton.Foreground = (Brush)Resources[_rightPane == RightPane.Themes && _showFavorites ? "Accent" : "MutedText"];
        ThemesButton.Tag = themesActive ? "active" : "inactive";
        FavoritesButton.Tag = _rightPane == RightPane.Themes && _showFavorites ? "active" : "inactive";
        ThemesButton.FontWeight = themesActive ? FontWeights.SemiBold : FontWeights.Normal;
        FavoritesButton.FontWeight = _rightPane == RightPane.Themes && _showFavorites ? FontWeights.SemiBold : FontWeights.Normal;
        FavoritesLabelText.Text = _favoriteThemeIds.Count == 0
            ? "我的收藏"
            : $"我的收藏  {_favoriteThemeIds.Count}";
        UpdateInfoNavigationButton(DiagnosticsButton, _rightPane == RightPane.Diagnostics);
        UpdateInfoNavigationButton(SettingsButton, _rightPane == RightPane.Settings);
        UpdateInfoNavigationButton(ImportGuideButton, _rightPane == RightPane.ImportGuide);
        UpdateInfoNavigationButton(CreatorCenterButton, _rightPane == RightPane.Creator);
        UpdateInfoNavigationButton(AboutButton, _rightPane == RightPane.About);
    }

    private void UpdateLibraryMetrics()
    {
        if (!_uiInitialized || ThemeCountText is null || FavoriteCountText is null)
        {
            return;
        }

        ThemeCountText.Text = _themes.Count.ToString(CultureInfo.InvariantCulture);
        FavoriteCountText.Text = _themes.Count(theme => theme.IsFavorite).ToString(CultureInfo.InvariantCulture);
    }

    private static void AnimateCardPress(Button button)
    {
        if (button.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var easing = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(230)) { EasingFunction = easing });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(230)) { EasingFunction = easing });
    }

    private void RoundedCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || border.ActualWidth <= 0 || border.ActualHeight <= 0)
        {
            return;
        }

        border.Clip = new RectangleGeometry(
            new Rect(0, 0, border.ActualWidth, border.ActualHeight),
            16,
            16);
    }

    private void AnimateSelectionDock()
    {
        if (!_uiInitialized || SelectionDockScale is null)
        {
            return;
        }

        var easing = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };
        SelectionDockScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        SelectionDockScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
    }

    private static void AnimatePage(FrameworkElement page)
    {
        page.Opacity = 0;
        if (page.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            page.RenderTransform = translate;
        }

        translate.Y = 10;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = easing });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = easing });
    }

    private void UpdateInfoNavigationButton(Button button, bool active)
    {
        button.Background = active ? (Brush)Resources["ActiveNav"] : Brushes.Transparent;
        button.Foreground = (Brush)Resources[active ? "Accent" : "MutedText"];
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        button.Tag = active ? "active" : "inactive";
    }

    private void UpdateModeButtons()
    {
        if (LightModeButton is null || DarkModeButton is null) return;
        LightModeButton.Background = _darkMode ? Brushes.Transparent : (Brush)Resources["SkySoft"];
        LightModeButton.Foreground = (Brush)Resources[_darkMode ? "MutedText" : "Sky"];
        LightModeButton.BorderBrush = _darkMode ? Brushes.Transparent : (Brush)Resources["Sky"];
        LightModeButton.FontWeight = _darkMode ? FontWeights.Normal : FontWeights.SemiBold;
        DarkModeButton.Background = _darkMode ? (Brush)Resources["AccentSoft"] : Brushes.Transparent;
        DarkModeButton.Foreground = (Brush)Resources[_darkMode ? "Accent" : "MutedText"];
        DarkModeButton.BorderBrush = _darkMode ? (Brush)Resources["Accent"] : Brushes.Transparent;
        DarkModeButton.FontWeight = _darkMode ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void UpdateCodexModeButton()
    {
        if (!_uiInitialized)
        {
            return;
        }

        UpdateSettingsVisualHeader();
        if (CodexModeButton is null || CodexModeText is null || CodexModeIconPath is null)
        {
            return;
        }

        if (_codexDarkMode is true)
        {
            CodexModeText.Text = "Codex 当前暗色";
            CodexModeIconPath.Data = Geometry.Parse("M 15,3 C 9,4 7,9 8.5,13.5 C 10,18 14.5,19.5 19,17 C 17,20.5 12.5,22 8.5,20 C 4,17.8 2,12.5 4,8 C 6,3.8 11,1.7 15,3 Z");
            CodexModeButton.Background = (Brush)Resources["AccentSoft"];
            CodexModeButton.Foreground = (Brush)Resources["Accent"];
            CodexModeButton.BorderBrush = (Brush)Resources["Accent"];
            CodexModeButton.ToolTip = "Codex 当前为暗色，点击切换到亮色";
            return;
        }

        if (_codexDarkMode is false)
        {
            CodexModeText.Text = "Codex 当前亮色";
            CodexModeIconPath.Data = Geometry.Parse("M 12,7 A 5,5 0 1 1 11.9,7 M 12,1 L 12,3 M 12,21 L 12,23 M 1,12 L 3,12 M 21,12 L 23,12");
            CodexModeButton.Background = (Brush)Resources["SkySoft"];
            CodexModeButton.Foreground = (Brush)Resources["Sky"];
            CodexModeButton.BorderBrush = (Brush)Resources["Sky"];
            CodexModeButton.ToolTip = "Codex 当前为亮色，点击切换到暗色";
            return;
        }

        CodexModeText.Text = "检测 Codex 明暗";
        CodexModeButton.Background = (Brush)Resources["SkySoft"];
        CodexModeButton.Foreground = (Brush)Resources["Sky"];
        CodexModeButton.BorderBrush = (Brush)Resources["Sky"];
        CodexModeButton.ToolTip = "点击切换 Codex 明暗色；首次切换后显示当前状态";
    }

    private void SetBusy(bool busy, string? status)
    {
        if (!_uiInitialized) return;

        ActivateButton.IsEnabled = !busy && _selectedTheme?.IsValid == true;
        RestoreButton.IsEnabled = !busy;
        CodexModeButton.IsEnabled = !busy;
        StartupButton.IsEnabled = !busy;
        QuickSwitchButton.IsEnabled = !busy;
        DeleteButton.IsEnabled = !busy && _selectedTheme?.CanDelete == true;
        ThemeDetailsButton.IsEnabled = !busy && _selectedTheme is not null;
        SettingsThemeSwitchPanel.IsEnabled = !busy;
        if (status is not null) StatusText.Text = status;
    }

    private void SetStatus(string status)
    {
        if (_uiInitialized)
        {
            StatusText.Text = status;
        }
    }

    private Window ProductDialogOwner => IsVisible
        ? this
        : _quickSwitchWindow is { IsVisible: true }
            ? _quickSwitchWindow
            : this;

    private bool ShowProductConfirmation(
        string title,
        string message,
        string confirmText,
        bool dangerous = false) =>
        ProductDialogWindow.Confirm(
            ProductDialogOwner,
            title,
            message,
            confirmText,
            dangerous: dangerous,
            darkMode: _darkMode);

    private void ShowProductMessage(string title, string message, ProductDialogKind kind) =>
        ProductDialogWindow.ShowMessage(ProductDialogOwner, title, message, kind, _darkMode);

    private void ShowToast(string message, bool warning = false)
    {
        if (!_uiInitialized || !IsVisible || ToastPanel is null || _toastTimer is null)
        {
            return;
        }

        _toastTimer.Stop();
        ToastPanel.BeginAnimation(OpacityProperty, null);
        ToastText.Text = message;
        ToastIconText.Text = warning ? "!" : "✓";
        ToastIconText.Foreground = (Brush)Resources[warning ? "Amber" : "Accent"];
        ToastPanel.BorderBrush = (Brush)Resources[warning ? "Amber" : "Border"];
        ToastPanel.Visibility = Visibility.Visible;
        ToastPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        _toastTimer.Start();
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        animation.Completed += (_, _) => ToastPanel.Visibility = Visibility.Collapsed;
        ToastPanel.BeginAnimation(OpacityProperty, animation);
    }

    private void SetEngineState(string status)
    {
        _engineStateText = status;
        if (_uiInitialized)
        {
            EngineStateText.Text = status;
            EngineStateDot.Fill = (Brush)Resources[
                status.Contains("运行中", StringComparison.Ordinal) ||
                status.Contains("可用", StringComparison.Ordinal)
                    ? "Positive"
                    : status.Contains("失败", StringComparison.Ordinal) ||
                      status.Contains("不在", StringComparison.Ordinal)
                        ? "Danger"
                        : "SubtleText"];
        }
    }

    private void Runtime_StatusChanged(object? sender, string status) =>
        _ = Dispatcher.InvokeAsync(() => SetStatus(status));
}
