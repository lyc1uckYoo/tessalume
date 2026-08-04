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
    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDiagnosticsAsync();
        ShowInfoPage(RightPane.Diagnostics);
    }

    private async Task RefreshDiagnosticsAsync()
    {
        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port;
        var portReady = port is not null && await _launcher.IsDebugPortReadyAsync(port.Value);
        var validThemes = _themes.Count(theme => theme.IsValid);
        var activeTheme = state?.Enabled == true
            ? _themes.FirstOrDefault(theme => theme.CatalogItem.Package?.Manifest.Id == state.ThemeId)?.Name
            : null;
        var codexRunning = CodexPackageLauncher.IsCodexRunning();
        DiagnosticCodexText.Text = codexRunning ? "正在运行" : "未运行";
        DiagnosticPortText.Text = port is null ? "未分配" : portReady ? $"{port} · 正常" : $"{port} · 未连接";
        DiagnosticThemesText.Text = _themes.Count - validThemes == 0
            ? $"{validThemes} 个有效"
            : $"{validThemes} 有效 / {_themes.Count - validThemes} 异常";
        DiagnosticCodexText.SetResourceReference(TextBlock.ForegroundProperty, codexRunning ? "Positive" : "MutedText");
        DiagnosticPortText.SetResourceReference(TextBlock.ForegroundProperty, portReady ? "Positive" : port is null ? "MutedText" : "Amber");
        DiagnosticThemesText.SetResourceReference(TextBlock.ForegroundProperty, _themes.Count == validThemes ? "Positive" : "Amber");
        DiagnosticRootText.Text = _layout.RootDirectory;
        DiagnosticLibraryText.Text = _layout.ThemesDirectory;
        DiagnosticProcessText.Text = codexRunning ? "已发现 · 正在运行" : "未发现";
        DiagnosticLoopbackText.Text = portReady ? $"127.0.0.1:{port} · 可用" : "当前不可用";
        DiagnosticThemeStateText.Text = state?.Enabled == true ? "沉浸式主题已启用" : "Codex 默认外观";
        DiagnosticCurrentThemeText.Text = $"当前主题：{activeTheme ?? "无"}";
        DiagnosticValidationText.Text = $"{validThemes} 个通过 · {_themes.Count - validThemes} 个异常";
        var invalidThemes = _themes.Count - validThemes;
        if (codexRunning && portReady && invalidThemes == 0)
        {
            DiagnosticHealthTitleText.Text = "运行状态良好";
            DiagnosticHealthBodyText.Text = "Codex、本机运行时与全部主题包均处于可用状态。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Positive"];
        }
        else if (!codexRunning)
        {
            DiagnosticHealthTitleText.Text = "Codex 尚未运行";
            DiagnosticHealthBodyText.Text = "应用任意主题时，软件会自动启动并建立本地连接。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Amber"];
        }
        else if (!portReady)
        {
            DiagnosticHealthTitleText.Text = "本机运行时需要重新连接";
            DiagnosticHealthBodyText.Text = "Codex 正在运行，但当前回环端口不可用；重新应用主题即可修复。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Amber"];
        }
        else
        {
            DiagnosticHealthTitleText.Text = "发现需要处理的主题包";
            DiagnosticHealthBodyText.Text = $"{invalidThemes} 个主题未通过本地校验，请检查对应主题源码。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Danger"];
        }
        DiagnosticUpdatedText.Text = $"刚刚更新 · {DateTime.Now:HH:mm:ss}";
    }

    private async void RestoreBuiltInThemes_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var restoredCount = BuiltInAssetInstaller.RestoreDeletedThemes(_layout);
            if (restoredCount == 0)
            {
                ShowProductMessage("无需恢复", "当前没有被删除的内置主题。", ProductDialogKind.Information);
                return;
            }

            await ReloadThemesAsync();
            LocalLog.Write($"Restored {restoredCount} deleted built-in theme(s).");
            ShowToast($"已恢复 {restoredCount} 个内置主题");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Restoring built-in themes failed.", exception);
            ShowProductMessage("无法恢复内置主题", exception.Message, ProductDialogKind.Error);
        }
    }

    private void OpenLogDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LocalLog.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LocalLog.LogDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowProductMessage("无法打开日志目录", exception.Message, ProductDialogKind.Error);
        }
    }

}
