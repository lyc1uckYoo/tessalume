using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Tessalume.App.Creator;

public partial class CreatorCenterView
{
    private async void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null) return;
        await RunOperationAsync(
            () => _viewModel.SelectProjectAsync(WorkspacePage.SelectedProject),
            "无法切换主题项目");
    }

    private async void RevalidateProject_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await RunOperationAsync(() => _viewModel.RevalidateSelectedProjectAsync(), "无法重新体检主题项目");
    }

    private async void ApplyProjectToCodex_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await RunOperationAsync(async () =>
        {
            var result = await _viewModel.ApplySelectedProjectAsync();
            if (result.Succeeded)
            {
                _showToast?.Invoke("创作项目已重新应用到 Codex");
                return;
            }
            ShowMessage("无法应用创作项目", result.Message, ProductDialogKind.Warning);
        }, "无法应用创作项目");
    }

    private async void ToggleCreatorCodexMode_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await RunOperationAsync(async () =>
        {
            var status = await _viewModel.ToggleCodexModeAsync();
            if (status.IsConnected)
            {
                _showToast?.Invoke(status.IsDarkMode == true ? "Codex 已切换为暗色" : "Codex 已切换为亮色");
            }
        }, "无法切换 Codex 明暗色");
    }

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedProject is { } project) TryOpenDirectory(project.DirectoryPath);
    }

    private void CopyRepairPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedProject is not { } project ||
            !CreatorRepairPromptComposer.CanCopy(project.Snapshot)) return;
        try
        {
            Clipboard.SetText(CreatorRepairPromptComposer.Compose(project.Snapshot));
            _showToast?.Invoke($"已复制 {project.ErrorCount + project.WarningCount} 项主题修复提示");
        }
        catch (ExternalException)
        {
            _showToast?.Invoke("剪贴板正忙，请再点一次");
        }
    }

    private async void ExportProject_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedProject is not { } project) return;
        var safeId = string.Concat(project.ThemeId.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        var dialog = new SaveFileDialog
        {
            Title = "导出 Tessalume 主题分享包",
            Filter = "ZIP 主题包 (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"{safeId}-{project.VersionText.TrimStart('v')}.zip",
        };
        if (dialog.ShowDialog(GetOwner()) != true) return;

        await RunOperationAsync(async () =>
        {
            var result = await _viewModel.ExportSelectedProjectAsync(dialog.FileName);
            ShowMessage(
                "主题分享包已导出",
                $"{result.ThemeId} · v{result.ThemeVersion}\n" +
                $"文件：{result.FileCount} 个 · {FormatBytes(result.CompressedBytes)}\n" +
                $"SHA-256：{result.Sha256}\n\n{result.ArchivePath}",
                ProductDialogKind.Information);
        }, "无法导出主题分享包");
    }

    private void OpenHealthPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }
            if (Directory.Exists(path))
            {
                OpenDirectory(path);
                return;
            }
            var parent = Path.GetDirectoryName(path);
            while (!string.IsNullOrWhiteSpace(parent) && !Directory.Exists(parent))
            {
                parent = Path.GetDirectoryName(parent);
            }
            if (parent is not null)
            {
                OpenDirectory(parent);
                _showToast?.Invoke("目标文件尚不存在，已打开最近目录");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            ShowMessage("无法定位文件", exception.Message, ProductDialogKind.Error);
        }
    }
}
