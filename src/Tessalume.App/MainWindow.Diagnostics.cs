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
using Tessalume.App.Diagnostics;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;
using Tessalume.Core.Updates;
using Microsoft.Win32;

namespace Tessalume.App;

public partial class MainWindow
{
    private bool _diagnosticsRefreshInProgress;

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        ShowInfoPage(RightPane.Diagnostics);
        if (_diagnosticsRefreshInProgress) return;

        _diagnosticsRefreshInProgress = true;
        SetDiagnosticsLoadingState(true);
        await Dispatcher.Yield(DispatcherPriority.Render);
        try
        {
            await RefreshDiagnosticsAsync();
        }
        catch (Exception exception)
        {
            DiagnosticHealthTitleText.Text = "本次检查未完成";
            DiagnosticHealthBodyText.Text = "诊断数据没有被修改，可以稍后重新检查。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Danger"];
            DiagnosticUpdatedText.Text = "检查失败";
            ShowToast($"诊断检查失败：{exception.Message}");
        }
        finally
        {
            _diagnosticsRefreshInProgress = false;
            SetDiagnosticsLoadingState(false);
        }
    }

    private void SetDiagnosticsLoadingState(bool isLoading)
    {
        if (!_uiInitialized || DiagnosticLoadingPanel is null) return;
        DiagnosticLoadingPanel.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsRefreshButton.IsEnabled = !isLoading;
        DiagnosticUpdatedText.Text = isLoading ? "正在检查…" : DiagnosticUpdatedText.Text;
    }

    private async Task RefreshDiagnosticsAsync()
    {
        var state = await _stateStore.LoadAsync();
        var compatibility = await CompatibilityHealthService.InspectAsync(state);
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
        DiagnosticCodexVersionText.Text = compatibility.InstalledCodexVersion is { Length: > 0 } version
            ? $"v{version}"
            : "未读取到 Store 版本";
        DiagnosticRuntimeContractText.Text = compatibility.RequiresPreflight
            ? $"v{compatibility.RuntimeContractVersion} · 等待重新预检"
            : compatibility.LastSuccessfulApplyAt is null
                ? $"v{compatibility.RuntimeContractVersion} · 等待首次验证"
                : $"v{compatibility.RuntimeContractVersion} · 已验证";
        DiagnosticLastSuccessText.Text = compatibility.LastSuccessfulApplyAt is { } lastSuccess
            ? lastSuccess.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : "暂无成功记录";
        DiagnosticLastFailureText.Text = CompatibilityHealthService.GetFailureStageLabel(
            compatibility.LastFailureStage);
        DiagnosticLastFailureText.SetResourceReference(
            TextBlock.ForegroundProperty,
            compatibility.LastFailureStage == ThemeRuntimeFailureStage.None ? "Positive" : "Danger");
        DiagnosticCompatibilityHintText.Text = compatibility.RequiresPreflight
            ? "检测到环境版本变化；下一次应用主题前会自动执行完整预检。"
            : CompatibilityHealthService.GetRecommendation(compatibility.LastFailureStage);
        DiagnosticCompatibilityDetailText.Text = GetCompatibilityDetail(compatibility);
        var invalidThemes = _themes.Count - validThemes;
        if (compatibility.LastFailureStage != ThemeRuntimeFailureStage.None)
        {
            DiagnosticHealthTitleText.Text = "最近一次应用未完成";
            DiagnosticHealthBodyText.Text =
                $"失败阶段：{CompatibilityHealthService.GetFailureStageLabel(compatibility.LastFailureStage)}。数据已保留，可按下方建议恢复。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Danger"];
        }
        else if (compatibility.RequiresPreflight)
        {
            DiagnosticHealthTitleText.Text = "环境变化，等待兼容性预检";
            DiagnosticHealthBodyText.Text = "检测到 Codex 或 Tessalume 运行时版本变化；下次应用主题时会自动验证。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Amber"];
        }
        else if (codexRunning && portReady && invalidThemes == 0)
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

    private static string GetCompatibilityDetail(CompatibilityHealthSnapshot compatibility)
    {
        if (compatibility.CodexVersionChanged)
        {
            return "Codex 版本与最近成功应用时不同；预检通过后会自动更新兼容性基线。";
        }

        if (compatibility.RuntimeContractChanged)
        {
            return "主题运行时契约已经升级；预检通过后会自动更新兼容性基线。";
        }

        if (compatibility.LastFailureStage == ThemeRuntimeFailureStage.None)
        {
            return "兼容性记录只保存在本机，不会修改 Codex 安装文件，也不会上传诊断数据。";
        }

        var failureTime = compatibility.LastFailureAt is { } value
            ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : "时间未知";
        var message = string.IsNullOrWhiteSpace(compatibility.LastFailureMessage)
            ? "未记录额外错误信息"
            : compatibility.LastFailureMessage.ReplaceLineEndings(" ").Trim();
        if (message.Length > 180)
        {
            message = string.Concat(message.AsSpan(0, 177), "…");
        }

        return $"{failureTime} · {message}";
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
