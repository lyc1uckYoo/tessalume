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
    private void OpenQuickSwitchWindow()
    {
        try
        {
            _quickSwitchWindow = new ThemeQuickSwitchWindow(
                ApplyThemeAsync,
                ToggleRestoreThemeAsync,
                ToggleCodexColorSchemeAsync,
                ReadCodexColorSchemeAsync,
                ShowMainInterface,
                () => _usageReader.ReadAsync());
            _quickSwitchWindow.SetShellTheme(_darkMode);
            _quickSwitchWindow.Closed += (_, _) =>
            {
                _quickSwitchWindow = null;
                UpdateQuickSwitchButton();
            };
            RefreshQuickSwitchWindow();
            _quickSwitchWindow.Show();
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

    internal async void ShowMainInterface()
    {
        if (_quickSwitchWindow is { IsVisible: true })
        {
            _quickSwitchWindow.Close();
        }
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
