using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Tessalume.App.Features.Diagnostics;
using Tessalume.App.Infrastructure;

namespace Tessalume.App;

public partial class MainWindow
{
    private bool _diagnosticsRefreshInProgress;

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(Features.Navigation.AppRoute.Diagnostics);
        await RefreshDiagnosticsAsync();
    }

    private async void DiagnosticsPage_RefreshRequested(object sender, RoutedEventArgs e) =>
        await RefreshDiagnosticsAsync();

    private async void DiagnosticsPage_DebugPortPreferenceSaveRequested(
        object? sender,
        DebugPortPreferenceRequestedEventArgs e)
    {
        DiagnosticsPage.SetPortPreferenceSaving(true);
        try
        {
            var state = await _stateStore.LoadAsync() ?? new StudioState();
            await _stateStore.SaveAsync(state with
            {
                PreferredDebugPort = e.PreferredPort,
                UpdatedAt = DateTimeOffset.Now,
            });
            _activePort = null;
            ShowToast(e.PreferredPort is { } port
                ? $"已优先使用本机端口 {port}"
                : "已恢复自动发现 Codex 连接");
            await RefreshDiagnosticsAsync();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowProductMessage(
                "无法保存连接偏好",
                exception.Message,
                ProductDialogKind.Error);
        }
        finally
        {
            DiagnosticsPage.SetPortPreferenceSaving(false);
        }
    }

    private async Task RefreshDiagnosticsAsync()
    {
        if (_diagnosticsRefreshInProgress) return;

        _diagnosticsRefreshInProgress = true;
        DiagnosticsPage.SetLoading(true);
        await Dispatcher.Yield(DispatcherPriority.Render);
        try
        {
            var themes = _themes
                .Select(theme => new DiagnosticsThemeStatus(
                    theme.ThemeId,
                    theme.Name,
                    theme.IsValid))
                .ToArray();
            var snapshot = await _diagnosticsService.InspectAsync(_activePort, themes);
            DiagnosticsPage.Render(snapshot);
        }
        catch (Exception exception)
        {
            DiagnosticsPage.ShowFailure();
            ShowToast($"诊断检查失败：{exception.Message}");
        }
        finally
        {
            _diagnosticsRefreshInProgress = false;
            DiagnosticsPage.SetLoading(false);
        }
    }

    private async void DiagnosticsPage_RestoreBuiltInThemesRequested(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var restoredCount = BuiltInAssetInstaller.RestoreDeletedThemes(_layout);
            if (restoredCount == 0)
            {
                ShowProductMessage(
                    "无需恢复",
                    "当前没有被删除的内置主题。",
                    ProductDialogKind.Information);
                return;
            }

            await ReloadThemesAsync();
            LocalLog.Write($"Restored {restoredCount} deleted built-in theme(s).");
            ShowToast($"已恢复 {restoredCount} 个内置主题");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Restoring built-in themes failed.", exception);
            ShowProductMessage(
                "无法恢复内置主题",
                exception.Message,
                ProductDialogKind.Error);
        }
    }

    private void DiagnosticsPage_OpenLogDirectoryRequested(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LocalLog.LogDirectory);
            Process.Start(new ProcessStartInfo(
                "explorer.exe",
                $"\"{LocalLog.LogDirectory}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowProductMessage(
                "无法打开日志目录",
                exception.Message,
                ProductDialogKind.Error);
        }
    }
}
