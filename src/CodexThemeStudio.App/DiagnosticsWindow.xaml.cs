using System.Windows;

namespace CodexThemeStudio.App;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(
        bool codexRunning,
        int? port,
        bool portReady,
        int validThemes,
        int invalidThemes,
        string? activeTheme,
        bool themeEnabled,
        string rootDirectory,
        string themesDirectory)
    {
        InitializeComponent();
        CodexStateText.Text = codexRunning ? "正在运行" : "未运行";
        PortStateText.Text = port is null ? "未分配" : portReady ? $"{port} · 正常" : $"{port} · 未连接";
        ThemeStateText.Text = invalidThemes == 0 ? $"{validThemes} 个有效" : $"{validThemes} 有效 / {invalidThemes} 异常";
        DetailsText.Text = string.Join(
            Environment.NewLine,
            $"Studio 根目录：{rootDirectory}",
            $"本地主题库：{themesDirectory}",
            $"Codex 进程：{(codexRunning ? "已发现" : "未发现")}",
            $"回环 CDP：{(portReady ? $"127.0.0.1:{port} 可用" : "当前不可用")}",
            $"主题状态：{(themeEnabled ? "已启用" : "默认外观")}",
            $"当前主题：{activeTheme ?? "无"}",
            $"主题包校验：{validThemes} 个通过，{invalidThemes} 个未通过",
            string.Empty,
            "网络能力：无公网请求、无远程下载、无在线更新",
            "注入范围：仅本机 Codex 主渲染页面；宠物浮层自动排除");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
