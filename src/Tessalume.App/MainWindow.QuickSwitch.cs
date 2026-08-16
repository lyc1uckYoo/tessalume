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
    private void OpenQuickSwitchWindow(bool rememberVisibility = false)
    {
        if (_quickSwitchWindow is { IsVisible: true }) return;

        try
        {
            _quickSwitchWindow = new ThemeQuickSwitchWindow(
                ApplyThemeAsync,
                ToggleRestoreThemeAsync,
                ToggleCodexColorSchemeAsync,
                ReadCodexColorSchemeAsync,
                ShowMainInterface,
                CloseQuickSwitchAndShowMainInterface,
                () => _usageReader.ReadAsync());
            _quickSwitchWindow.SetShellTheme(_darkMode);
            _quickSwitchWindow.Closed += QuickSwitchWindow_Closed;
            RefreshQuickSwitchWindow();
            _quickSwitchWindow.Show();
            if (rememberVisibility && !_quickSwitchVisible)
            {
                _quickSwitchVisible = true;
                _ = SaveQuickSwitchVisibilityAsync();
            }
            UpdateQuickSwitchButton();
        }
        catch (Exception exception)
        {
            _quickSwitchWindow = null;
            if (_uiInitialized)
            {
                StatusText.Text = $"无法打开主题浮窗：{exception.Message}";
            }

            ShowProductMessage("无法打开主题浮窗", exception.Message, ProductDialogKind.Error);
        }
    }

    private void CloseQuickSwitchWindow(bool rememberClosed)
    {
        if (_quickSwitchWindow is not { IsVisible: true } window) return;

        var previousSuppression = _suppressQuickSwitchPreferenceChange;
        _suppressQuickSwitchPreferenceChange = !rememberClosed;
        try
        {
            window.Close();
        }
        finally
        {
            _suppressQuickSwitchPreferenceChange = previousSuppression;
        }
    }

    private void QuickSwitchWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is ThemeQuickSwitchWindow window)
        {
            window.Closed -= QuickSwitchWindow_Closed;
        }
        if (ReferenceEquals(_quickSwitchWindow, sender))
        {
            _quickSwitchWindow = null;
        }
        UpdateQuickSwitchButton();

        if (_suppressQuickSwitchPreferenceChange || !_quickSwitchVisible) return;
        _quickSwitchVisible = false;
        _ = SaveQuickSwitchVisibilityAsync();
    }

    private async Task SaveQuickSwitchVisibilityAsync()
    {
        try
        {
            await SavePreferencesAsync();
        }
        catch (Exception exception)
        {
            LocalLog.Write("Saving the quick-switch visibility preference failed.", exception);
            if (_uiInitialized)
            {
                StatusText.Text = "主题浮窗状态未能保存";
            }
        }
    }

    internal async void ShowMainInterface()
    {
        CloseQuickSwitchWindow(rememberClosed: false);
        EnsureMainUiInitialized();
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        _ = Dispatcher.InvokeAsync(
            () =>
            {
                Activate();
                NativeWindowActivation.TryActivate(Title);
            },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        if (_mainContentLoaded) return;

        _mainContentLoaded = true;
        try
        {
            await ReloadThemesAsync(_activeThemeId, loadPreviews: true);
            _ = RefreshCodexColorSchemeAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void CloseQuickSwitchAndShowMainInterface()
    {
        CloseQuickSwitchWindow(rememberClosed: true);
        ShowMainInterface();
    }

    private void RefreshQuickSwitchWindow()
    {
        if (_quickSwitchWindow is null) return;
        var isDefaultAppearance = string.IsNullOrWhiteSpace(_activeThemeId);
        var currentThemeName = isDefaultAppearance
            ? "Codex 默认外观"
            : _themes.FirstOrDefault(theme =>
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))?.Name ?? "未应用主题";
        _quickSwitchWindow.Refresh(
            _activeThemeId ?? string.Empty,
            currentThemeName,
            isDefaultAppearance,
            GetQuickSwitchCandidates());
    }

}
