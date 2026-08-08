using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Tessalume.App.Creator;

public partial class CreatorCenterView
{
    private async void CreateWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _provisioner is null) return;
        var dialog = new OpenFolderDialog
        {
            Title = "选择新创作者工作区的保存位置",
            Multiselect = false,
        };
        if (dialog.ShowDialog(GetOwner()) != true) return;

        await RunOperationAsync(async () =>
        {
            var destination = _provisioner.CreateWorkspace(dialog.FolderName);
            await _viewModel.AddWorkspaceAsync(destination);
            OpenDirectory(destination);
            ShowMessage(
                "创作工作区已准备",
                $"工作区已经创建并加入最近项目：\n{destination}\n\n请在 Codex 中打开整个文件夹，然后发送上方提示词或你自己的角色需求。",
                ProductDialogKind.Information);
        }, "无法创建创作工作区");
    }

    private async void OpenWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _provisioner is null) return;
        var selected = PickWorkspaceDirectory("选择已有 Tessalume 创作者工作区");
        if (selected is null) return;
        await RunOperationAsync(async () =>
        {
            var workspace = _provisioner.ResolveExistingWorkspace(selected);
            await _viewModel.AddWorkspaceAsync(workspace);
            _showToast?.Invoke("工作区已打开并完成扫描");
        }, "无法打开创作工作区");
    }

    private async void RelocateWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedWorkspace is null || _provisioner is null) return;
        var selected = PickWorkspaceDirectory("重新定位创作者工作区");
        if (selected is null) return;
        await RunOperationAsync(async () =>
        {
            var workspace = _provisioner.ResolveExistingWorkspace(selected);
            await _viewModel.RelocateSelectedWorkspaceAsync(workspace);
            _showToast?.Invoke("工作区位置已更新");
        }, "无法重新定位工作区");
    }

    private async void RemoveWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedWorkspace is not { } workspace) return;
        if (!ProductDialogWindow.Confirm(
                GetOwner(),
                "从最近项目中移除？",
                $"只会移除工作区记录，不会删除任何主题或素材：\n{workspace.DirectoryPath}",
                "移除记录",
                "取消",
                dangerous: false,
                darkMode: IsDarkMode())) return;

        await RunOperationAsync(
            () => _viewModel.RemoveSelectedWorkspaceAsync(),
            "无法移除工作区记录");
    }

    private async void RefreshWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await RunOperationAsync(() => _viewModel.RefreshAsync(), "无法刷新工作区");
    }

    private async void WorkspaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null ||
            WorkspacePage.SelectedWorkspace is not { } selected ||
            ReferenceEquals(selected, _viewModel.SelectedWorkspace)) return;

        await RunOperationAsync(
            () => _viewModel.SelectWorkspaceAsync(selected),
            "无法切换工作区");
    }

    private void OpenWorkspaceFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedWorkspace is not { } workspace) return;
        if (!Directory.Exists(workspace.DirectoryPath))
        {
            ShowMessage(
                "工作区位置不可用",
                "这个工作区可能已被移动或删除，请使用“重新定位”。",
                ProductDialogKind.Warning);
            return;
        }
        TryOpenDirectory(workspace.DirectoryPath);
    }

    private async void UpgradeWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedWorkspace is not { } workspace ||
            _provisioner is null ||
            !_viewModel.CanUpgradeWorkspace) return;

        var confirmed = ProductDialogWindow.Confirm(
            GetOwner(),
            "安全升级创作者工作区",
            "升级会更新工作区内由 Tessalume 管理的 Skill、Schema、说明和共享校验文件。\n\n" +
            "被替换的文件会先备份到 .tessalume-backups；themes 目录中的主题、图片和源码不会被读取、删除或覆盖。",
            "备份并升级",
            dangerous: false,
            darkMode: IsDarkMode());
        if (!confirmed) return;

        await RunOperationAsync(async () =>
        {
            var result = _provisioner.UpgradeWorkspace(workspace.DirectoryPath);
            await _viewModel.RefreshAsync();
            var backupText = result.BackupDirectory is null
                ? "工作区文件已经是最新版本。"
                : $"升级前文件已保存在：\n{result.BackupDirectory}";
            ShowMessage(
                "创作者工作区已升级",
                $"已更新 {result.UpdatedFileCount} 个受管理文件。\n\n{backupText}\n\n用户主题项目保持不变。",
                ProductDialogKind.Information);
        }, "无法升级创作者工作区");
    }

    private async void CopyManualTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_provisioner is null) return;
        var dialog = new OpenFolderDialog
        {
            Title = "选择手动模板的保存位置",
            Multiselect = false,
        };
        if (dialog.ShowDialog(GetOwner()) != true) return;

        await RunOperationAsync(() =>
        {
            var destination = _provisioner.CopyManualTemplate(dialog.FolderName);
            OpenDirectory(destination);
            _showToast?.Invoke("Template 1.0 已复制并打开");
            return Task.CompletedTask;
        }, "无法复制手动模板");
    }

    private string? PickWorkspaceDirectory(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog(GetOwner()) == true ? dialog.FolderName : null;
    }
}
