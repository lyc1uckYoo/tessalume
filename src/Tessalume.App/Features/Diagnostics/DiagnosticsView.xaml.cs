using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Diagnostics;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
    }

    internal event RoutedEventHandler? RefreshRequested;

    internal event RoutedEventHandler? OpenLogDirectoryRequested;

    internal event RoutedEventHandler? RestoreBuiltInThemesRequested;

    internal event EventHandler<DebugPortPreferenceRequestedEventArgs>? DebugPortPreferenceSaveRequested;

    internal void SetLoading(bool isLoading)
    {
        DiagnosticLoadingPanel.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsRefreshButton.IsEnabled = !isLoading;
        if (isLoading)
        {
            DiagnosticUpdatedText.Text = "正在检查…";
        }
    }

    internal void ShowFailure()
    {
        DiagnosticHealthTitleText.Text = "本次检查未完成";
        DiagnosticHealthBodyText.Text = "诊断数据没有被修改，可以稍后重新检查。";
        SetTone(DiagnosticHealthDot, "Danger");
        DiagnosticUpdatedText.Text = "检查失败";
    }

    internal void Render(DiagnosticsSnapshot snapshot)
    {
        var compatibility = snapshot.Compatibility;
        DiagnosticCodexText.Text = snapshot.CodexRunning ? "正在运行" : "未运行";
        DiagnosticPortText.Text = snapshot.Port is null
            ? "未分配"
            : snapshot.PortReady
                ? snapshot.Port == CodexDebugPortPolicy.CodexPlusPlusPort
                    ? $"{snapshot.Port} · Codex++"
                    : $"{snapshot.Port} · 正常"
                : $"{snapshot.Port} · 未连接";
        DiagnosticThemesText.Text = snapshot.InvalidThemes == 0
            ? $"{snapshot.ValidThemes} 个有效"
            : $"{snapshot.ValidThemes} 有效 / {snapshot.InvalidThemes} 异常";
        SetTone(DiagnosticCodexText, snapshot.CodexRunning ? "Positive" : "MutedText");
        SetTone(DiagnosticPortText, snapshot.PortReady ? "Positive" : snapshot.Port is null ? "MutedText" : "Amber");
        SetTone(DiagnosticThemesText, snapshot.InvalidThemes == 0 ? "Positive" : "Amber");

        DiagnosticRootText.Text = snapshot.ApplicationRoot;
        DiagnosticLibraryText.Text = snapshot.ThemesDirectory;
        DiagnosticProcessText.Text = snapshot.CodexRunning ? "已发现 · 正在运行" : "未发现";
        DiagnosticLoopbackText.Text = snapshot.PortReady
            ? $"127.0.0.1:{snapshot.Port} · 可用"
            : "当前不可用";
        PreferredDebugPortTextBox.Text = snapshot.PreferredDebugPort?.ToString(
            CultureInfo.InvariantCulture) ?? string.Empty;
        RenderPortPreference(snapshot);
        DiagnosticThemeStateText.Text = snapshot.ThemeEnabled ? "沉浸式主题已启用" : "Codex 默认外观";
        DiagnosticCurrentThemeText.Text = $"当前主题：{snapshot.ActiveThemeName ?? "无"}";
        DiagnosticValidationText.Text = $"{snapshot.ValidThemes} 个通过 · {snapshot.InvalidThemes} 个异常";
        DiagnosticCodexVersionText.Text = compatibility.InstalledCodexVersion is { Length: > 0 } version
            ? $"v{version}"
            : "未读取到 Store 版本";
        DiagnosticRuntimeContractText.Text = compatibility.RequiresPreflight
            ? $"运行时 v{compatibility.RuntimeContractVersion} · 规则 {compatibility.CompatibilityPackVersion} · 等待重新预检"
            : compatibility.LastSuccessfulApplyAt is null
                ? $"运行时 v{compatibility.RuntimeContractVersion} · 规则 {compatibility.CompatibilityPackVersion} · 等待首次验证"
                : $"运行时 v{compatibility.RuntimeContractVersion} · 规则 {compatibility.CompatibilityPackVersion} · 已验证";
        DiagnosticLastSuccessText.Text = compatibility.LastSuccessfulApplyAt is { } lastSuccess
            ? lastSuccess.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : "暂无成功记录";
        DiagnosticLastFailureText.Text = CompatibilityHealthService.GetFailureStageLabel(
            compatibility.LastFailureStage);
        SetTone(
            DiagnosticLastFailureText,
            compatibility.LastFailureStage == ThemeRuntimeFailureStage.None ? "Positive" : "Danger");
        DiagnosticCompatibilityHintText.Text = compatibility.RequiresPreflight
            ? "检测到环境版本变化；下一次应用主题前会自动执行完整预检。"
            : CompatibilityHealthService.GetRecommendation(compatibility.LastFailureStage);
        DiagnosticCompatibilityDetailText.Text = GetCompatibilityDetail(compatibility);
        RenderHealth(snapshot);
        DiagnosticUpdatedText.Text = $"刚刚更新 · {snapshot.CheckedAt.ToLocalTime():HH:mm:ss}";
    }

    internal void SetPortPreferenceSaving(bool isSaving)
    {
        SaveDebugPortPreferenceButton.IsEnabled = !isSaving;
        PreferredDebugPortTextBox.IsEnabled = !isSaving;
        if (isSaving)
        {
            DiagnosticPortPreferenceText.Text = "正在保存并重新检查本机连接…";
            SetTone(DiagnosticPortPreferenceText, "MutedText");
        }
    }

    private void RenderPortPreference(DiagnosticsSnapshot snapshot)
    {
        if (snapshot.PreferredDebugPort is not { } preferredPort)
        {
            DiagnosticPortPreferenceText.Text = snapshot.Port == CodexDebugPortPolicy.CodexPlusPlusPort
                ? "已自动连接 Codex++；无需修改端口。"
                : "当前使用自动发现；留空保存可恢复此模式。";
            SetTone(DiagnosticPortPreferenceText, snapshot.PortReady ? "Positive" : "SubtleText");
            return;
        }

        if (snapshot.Port == preferredPort)
        {
            DiagnosticPortPreferenceText.Text = $"优先端口 {preferredPort} 已连接。";
            SetTone(DiagnosticPortPreferenceText, "Positive");
            return;
        }

        DiagnosticPortPreferenceText.Text = snapshot.PortReady
            ? $"优先端口 {preferredPort} 暂不可用，当前已自动连接 {snapshot.Port}。"
            : $"优先端口 {preferredPort} 暂不可用；启动对应宿主后重新检查。";
        SetTone(DiagnosticPortPreferenceText, "Amber");
    }

    private void RenderHealth(DiagnosticsSnapshot snapshot)
    {
        var compatibility = snapshot.Compatibility;
        if (compatibility.LastFailureStage != ThemeRuntimeFailureStage.None)
        {
            DiagnosticHealthTitleText.Text = "最近一次应用未完成";
            DiagnosticHealthBodyText.Text =
                $"失败阶段：{CompatibilityHealthService.GetFailureStageLabel(compatibility.LastFailureStage)}。数据已保留，可按下方建议恢复。";
            SetTone(DiagnosticHealthDot, "Danger");
        }
        else if (compatibility.RequiresPreflight)
        {
            DiagnosticHealthTitleText.Text = "环境变化，等待兼容性预检";
            DiagnosticHealthBodyText.Text = "检测到 Codex 或 Tessalume 运行时版本变化；下次应用主题时会自动验证。";
            SetTone(DiagnosticHealthDot, "Amber");
        }
        else if (snapshot.CodexRunning && snapshot.PortReady && snapshot.InvalidThemes == 0)
        {
            DiagnosticHealthTitleText.Text = "运行状态良好";
            DiagnosticHealthBodyText.Text = "Codex、本机运行时与全部主题包均处于可用状态。";
            SetTone(DiagnosticHealthDot, "Positive");
        }
        else if (!snapshot.CodexRunning)
        {
            DiagnosticHealthTitleText.Text = "Codex 尚未运行";
            DiagnosticHealthBodyText.Text = "应用任意主题时，软件会自动启动并建立本地连接。";
            SetTone(DiagnosticHealthDot, "Amber");
        }
        else if (!snapshot.PortReady)
        {
            DiagnosticHealthTitleText.Text = "本机运行时需要重新连接";
            DiagnosticHealthBodyText.Text = "Codex 正在运行，但当前回环端口不可用；重新应用主题即可修复。";
            SetTone(DiagnosticHealthDot, "Amber");
        }
        else
        {
            DiagnosticHealthTitleText.Text = "发现需要处理的主题包";
            DiagnosticHealthBodyText.Text = $"{snapshot.InvalidThemes} 个主题未通过本地校验，请检查对应主题源码。";
            SetTone(DiagnosticHealthDot, "Danger");
        }
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

        if (compatibility.CompatibilityPackChanged)
        {
            return $"页面兼容规则已更新到 {compatibility.CompatibilityPackVersion}；预检通过后会自动更新本机兼容性基线。";
        }

        if (compatibility.LastFailureStage == ThemeRuntimeFailureStage.None)
        {
            var source = compatibility.CompatibilityPackIsBuiltIn ? "随软件内置" : "官方小型兼容补丁";
            return $"当前页面兼容规则 {compatibility.CompatibilityPackVersion}（{source}）。记录只保存在本机，不会修改 Codex 安装文件，也不会上传诊断数据。";
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

    private static void SetTone(TextBlock target, string resourceKey) =>
        target.SetResourceReference(TextBlock.ForegroundProperty, resourceKey);

    private static void SetTone(Shape target, string resourceKey) =>
        target.SetResourceReference(Shape.FillProperty, resourceKey);

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, e);

    private void OpenLogDirectory_Click(object sender, RoutedEventArgs e) =>
        OpenLogDirectoryRequested?.Invoke(this, e);

    private void RestoreBuiltInThemes_Click(object sender, RoutedEventArgs e) =>
        RestoreBuiltInThemesRequested?.Invoke(this, e);

    private void SaveDebugPortPreference_Click(object sender, RoutedEventArgs e)
    {
        var text = PreferredDebugPortTextBox.Text.Trim();
        int? preferredPort = null;
        if (text.Length > 0)
        {
            if (!int.TryParse(
                    text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedPort) ||
                !CodexDebugPortPolicy.IsValid(parsedPort))
            {
                DiagnosticPortPreferenceText.Text = "请输入 1024–65535，或留空使用自动发现。";
                SetTone(DiagnosticPortPreferenceText, "Danger");
                PreferredDebugPortTextBox.Focus();
                PreferredDebugPortTextBox.SelectAll();
                return;
            }

            preferredPort = parsedPort;
        }

        DebugPortPreferenceSaveRequested?.Invoke(
            this,
            new DebugPortPreferenceRequestedEventArgs(preferredPort));
    }
}

internal sealed class DebugPortPreferenceRequestedEventArgs(int? preferredPort) : EventArgs
{
    public int? PreferredPort { get; } = preferredPort;
}
