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
    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTheme is not null)
        {
            await ApplyThemeAsync(_selectedTheme);
        }
    }

    private async Task<bool> ApplyThemeAsync(ThemeCardModel theme)
    {
        if (theme.CatalogItem.Package is not { } package) return false;
        SetBusy(true, "正在连接本机 Codex…");
        try
        {
            var state = await _stateStore.LoadAsync();
            var port = state?.Port ?? 0;
            if (port <= 0 || !await _launcher.IsDebugPortReadyAsync(port))
            {
                port = await _launcher.FindRunningDebugPortAsync() ?? 0;
            }

            if (port <= 0)
            {
                if (CodexPackageLauncher.IsCodexRunning())
                {
                    var confirmed = ShowProductConfirmation(
                        "需要重新启动 Codex",
                        "Codex 当前没有可用的主题连接。为了应用所选主题，需要关闭并重新启动 Codex。\n\n请先保存正在编辑的内容并确认当前任务可以中断。",
                        "已保存，重新启动");
                    if (!confirmed)
                    {
                        SetStatus("已取消应用，Codex 保持当前状态");
                        return false;
                    }

                    SetStatus("正在关闭并重新启动 Codex…");
                    await CodexPackageLauncher.CloseCodexAsync();
                }

                port = CodexPackageLauncher.FindFreePort();
                SetStatus($"正在本机端口 {port} 启动 Codex…");
                await _launcher.LaunchAndWaitAsync(port);
            }

            SetStatus("正在应用本地主题…");
            await _runtime.StartAsync(port, package, GetVisualSettings(package.Manifest.Id));
            await LegacyInjectorMigrator.TryStopAsync();
            await _stateStore.SaveAsync(new StudioState
            {
                Port = port,
                ThemeId = package.Manifest.Id,
                UpdatedAt = DateTimeOffset.Now,
                Enabled = true,
            });
            _activePort = port;
            _activeThemeId = package.Manifest.Id;
            _lastThemeId = _activeThemeId;
            UpdateAppliedThemeState();
            SetEngineState($"运行中 · 本机 {port}");
            SetStatus($"{package.Manifest.Name} 已应用，可继续实时切换");
            LocalLog.Write($"Applied theme {package.Manifest.Id} on port {port}.");
            RefreshQuickSwitchWindow();
            UpdateVisualAdjustmentControls();
            return true;
        }
        catch (Exception exception)
        {
            LocalLog.Write($"Applying theme {package.Manifest.Id} failed.", exception);
            SetEngineState("启动失败");
            SetStatus(exception.Message);
            ShowProductMessage("无法应用主题", exception.Message, ProductDialogKind.Error);
            return false;
        }
        finally
        {
            SetBusy(false, null);
            IdleMemoryTrimmer.Schedule();
        }
    }

    private async void RestoreTheme_Click(object sender, RoutedEventArgs e)
    {
        await RestoreDefaultAsync();
    }

    private async Task<bool> ToggleRestoreThemeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_activeThemeId))
        {
            return await RestoreDefaultAsync();
        }

        var lastTheme = _themes.FirstOrDefault(theme =>
            theme.IsValid && string.Equals(theme.ThemeId, _lastThemeId, StringComparison.OrdinalIgnoreCase));
        if (lastTheme is null)
        {
            SetStatus("没有可恢复的上一主题");
            return false;
        }

        return await ApplyThemeAsync(lastTheme);
    }

    private async Task<bool> RestoreDefaultAsync()
    {
        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port;
        if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
        {
            SetStatus("当前没有活动的本地主题");
            return false;
        }

        SetBusy(true, "正在恢复 Codex 默认外观…");
        try
        {
            await LegacyInjectorMigrator.TryStopAsync();
            await _runtime.RemoveAsync(port.Value);
            await _stateStore.SaveAsync(new StudioState
            {
                Port = port.Value,
                ThemeId = _selectedTheme?.CatalogItem.Package?.Manifest.Id ?? state?.ThemeId ?? string.Empty,
                UpdatedAt = DateTimeOffset.Now,
                Enabled = false,
            });
            SetEngineState("Codex 默认外观");
            if (!string.IsNullOrWhiteSpace(_activeThemeId))
            {
                _lastThemeId = _activeThemeId;
            }
            _activeThemeId = null;
            UpdateAppliedThemeState();
            SetStatus("本地主题已移除，Codex 安装文件未被修改");
            RefreshQuickSwitchWindow();
            UpdateVisualAdjustmentControls();
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            ShowProductMessage("恢复失败", exception.Message, ProductDialogKind.Error);
            return false;
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void StudioMode_Click(object sender, RoutedEventArgs e)
    {
        _darkMode = string.Equals((sender as Button)?.Tag?.ToString(), "dark", StringComparison.OrdinalIgnoreCase);
        ApplyStudioTheme(_darkMode);
        await SavePreferencesAsync();
    }

    private async void CodexMode_Click(object sender, RoutedEventArgs e)
    {
        await ToggleCodexColorSchemeAsync();
    }

    private async Task<bool?> ToggleCodexColorSchemeAsync()
    {
        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port ?? await _launcher.FindRunningDebugPortAsync();
        if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
        {
            SetStatus($"请先用 {BrandInfo.ProductName} 启动 Codex");
            return null;
        }

        SetBusy(true, "正在切换 Codex 明暗色…");
        try
        {
            _activePort = port.Value;
            var dark = await _runtime.ToggleColorSchemeAsync(port.Value);
            _codexDarkMode = dark;
            if (_rightPane == RightPane.Settings)
            {
                _editingVisualDarkMode = dark;
            }
            UpdateCodexModeButton();
            if (_rightPane == RightPane.Settings)
            {
                UpdateVisualAdjustmentControls();
            }

            SetStatus(dark ? "Codex 已切换为暗色" : "Codex 已切换为亮色");
            return dark;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            ShowProductMessage("无法切换 Codex 明暗色", exception.Message, ProductDialogKind.Error);
            return null;
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async Task<bool?> ReadCodexColorSchemeAsync()
    {
        try
        {
            var state = await _stateStore.LoadAsync();
            var port = _activePort ?? state?.Port ?? await _launcher.FindRunningDebugPortAsync();
            if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
            {
                return null;
            }

            _activePort = port.Value;
            var dark = await _runtime.ReadColorSchemeAsync(port.Value);
            _codexDarkMode = dark;
            if (_rightPane == RightPane.Settings)
            {
                _editingVisualDarkMode = dark;
            }
            UpdateCodexModeButton();
            if (_rightPane == RightPane.Settings)
            {
                UpdateVisualAdjustmentControls();
            }
            return dark;
        }
        catch
        {
            return null;
        }
    }

    private async Task RefreshCodexColorSchemeAsync()
    {
        var dark = await ReadCodexColorSchemeAsync();
        if (dark is null)
        {
            return;
        }

        _codexDarkMode = dark;
        UpdateCodexModeButton();
    }

}
