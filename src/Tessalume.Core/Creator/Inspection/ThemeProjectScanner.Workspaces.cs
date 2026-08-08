namespace Tessalume.Core.Creator;

public sealed partial class ThemeProjectScanner
{
    public async Task<CreatorWorkspaceScanResult> ScanWorkspaceAsync(
        string workspaceDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var workspace = Path.GetFullPath(workspaceDirectory);
        var themesDirectory = Path.Combine(workspace, "themes");
        var workspaceChecks = new List<ThemeProjectHealthCheck>();
        var workspaceContract = CreatorWorkspaceContract.Inspect(workspace);

        if (!Directory.Exists(workspace))
        {
            workspaceChecks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Workspace,
                "workspace.directory.missing",
                "工作区不可用",
                "工作区文件夹不存在，可能已被移动或删除。",
                ThemeProjectHealthSeverity.Error,
                workspace,
                "重新定位工作区，或将它从最近项目中移除。"));
            return new CreatorWorkspaceScanResult(
                workspace,
                themesDirectory,
                [],
                new ThemeProjectHealthReport(workspaceChecks))
            { Contract = workspaceContract };
        }

        if (!File.Exists(Path.Combine(workspace, "TESSALUME_CREATOR_WORKSPACE.md")))
        {
            workspaceChecks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Workspace,
                "workspace.marker.missing",
                "未找到创作者工作区标记",
                "该文件夹仍可扫描，但可能不是由 Tessalume 创建的标准工作区。",
                ThemeProjectHealthSeverity.Warning,
                workspace,
                "确认所选目录包含 themes 文件夹，或重新创建标准创作工作区。"));
        }

        AddWorkspaceContractCheck(workspaceChecks, workspace, workspaceContract);

        if (!Directory.Exists(themesDirectory))
        {
            workspaceChecks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Workspace,
                "workspace.themes.missing",
                "缺少 themes 文件夹",
                "工作区中没有用于保存主题项目的 themes 文件夹。",
                ThemeProjectHealthSeverity.Error,
                themesDirectory,
                "在工作区根目录创建 themes 文件夹。"));
            return new CreatorWorkspaceScanResult(
                workspace,
                themesDirectory,
                [],
                new ThemeProjectHealthReport(workspaceChecks))
            { Contract = workspaceContract };
        }

        string[] projectDirectories;
        try
        {
            projectDirectories = Directory.EnumerateDirectories(themesDirectory)
                .Where(directory => !Path.GetFileName(directory).StartsWith('.'))
                .Where(directory => (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            workspaceChecks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Workspace,
                "workspace.themes.unreadable",
                "无法读取主题目录",
                exception.Message,
                ThemeProjectHealthSeverity.Error,
                themesDirectory,
                "检查文件夹权限，并确认目录未被其他程序独占。"));
            return new CreatorWorkspaceScanResult(
                workspace,
                themesDirectory,
                [],
                new ThemeProjectHealthReport(workspaceChecks))
            { Contract = workspaceContract };
        }

        var projects = new List<ThemeProjectSnapshot>(projectDirectories.Length);
        foreach (var directory in projectDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            projects.Add(await ScanProjectAsync(directory, cancellationToken));
        }

        workspaceChecks.Add(new ThemeProjectHealthCheck(
            ThemeProjectHealthGroup.Workspace,
            projectDirectories.Length == 0 ? "workspace.projects.empty" : "workspace.ready",
            projectDirectories.Length == 0 ? "还没有主题项目" : "工作区可用",
            projectDirectories.Length == 0
                ? "Codex 生成的主题会出现在 themes 文件夹中。"
                : $"已发现 {projectDirectories.Length} 个主题项目。",
            projectDirectories.Length == 0
                ? ThemeProjectHealthSeverity.Warning
                : ThemeProjectHealthSeverity.Passed,
            themesDirectory,
            projectDirectories.Length == 0 ? "在 Codex 中完成一次主题创作后重新扫描。" : null));

        return new CreatorWorkspaceScanResult(
            workspace,
            themesDirectory,
            projects,
            new ThemeProjectHealthReport(workspaceChecks))
        { Contract = workspaceContract };
    }

    private static void AddWorkspaceContractCheck(
        List<ThemeProjectHealthCheck> checks,
        string workspace,
        CreatorWorkspaceContractInfo contract)
    {
        var markerPath = Path.Combine(workspace, CreatorWorkspaceContract.MarkerFileName);
        var (code, title, severity, action) = contract.State switch
        {
            CreatorWorkspaceContractState.Current => (
                "workspace.contract.current",
                "工作区工具链为最新",
                ThemeProjectHealthSeverity.Passed,
                (string?)null),
            CreatorWorkspaceContractState.Legacy => (
                "workspace.contract.legacy",
                "旧版工作区可以升级",
                ThemeProjectHealthSeverity.Warning,
                "在 Tessalume 创作项目中心点击“安全升级”。"),
            CreatorWorkspaceContractState.UpgradeAvailable => (
                "workspace.contract.upgrade",
                "工作区工具链有可用更新",
                ThemeProjectHealthSeverity.Warning,
                "在 Tessalume 创作项目中心点击“安全升级”。"),
            CreatorWorkspaceContractState.Newer => (
                "workspace.contract.newer",
                "工作区来自更新版本",
                ThemeProjectHealthSeverity.Warning,
                "使用创建这个工作区的 Tessalume 版本，避免降级工具文件。"),
            CreatorWorkspaceContractState.Invalid => (
                "workspace.contract.invalid",
                "工作区版本标记需要修复",
                ThemeProjectHealthSeverity.Warning,
                "使用安全升级备份旧工具文件并重建版本标记。"),
            _ => (
                "workspace.contract.missing",
                "未识别工作区工具链版本",
                ThemeProjectHealthSeverity.Warning,
                "如果这是 Tessalume 工作区，可重新创建标准工作区后迁移 themes 项目。"),
        };
        checks.Add(new ThemeProjectHealthCheck(
            ThemeProjectHealthGroup.Workspace,
            code,
            title,
            contract.Message,
            severity,
            markerPath,
            action));
    }

}
