using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Tessalume.App.Creator;

public partial class CreatorCenterView : UserControl, IDisposable
{
    private CreatorCenterViewModel? _viewModel;
    private CreatorWorkspaceProvisioner? _provisioner;
    private Func<bool>? _isDarkMode;
    private Action<string>? _showToast;
    private bool _synchronizingSelection;
    private bool _disposed;

    public CreatorCenterView()
    {
        InitializeComponent();
    }

    internal void Configure(
        string applicationRoot,
        CreatorWorkspaceStore workspaceStore,
        Func<Task> savePreferencesAsync,
        CreatorRuntimeBridge runtimeBridge,
        Func<bool> isDarkMode,
        Action<string> showToast)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_viewModel is not null) return;

        _provisioner = new CreatorWorkspaceProvisioner(applicationRoot);
        _isDarkMode = isDarkMode;
        _showToast = showToast;
        _viewModel = new CreatorCenterViewModel(workspaceStore, savePreferencesAsync, runtimeBridge);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = _viewModel;
        RenderState();
    }

    internal async Task ActivateAsync()
    {
        if (_viewModel is null || _disposed) return;
        await RunOperationAsync(
            () => _viewModel.ActivateAsync(),
            "无法打开创作项目中心");
    }

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
            var destination = CreatorWorkspaceProvisioner.CreateWorkspace(dialog.FolderName);
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
            var workspace = CreatorWorkspaceProvisioner.ResolveExistingWorkspace(selected);
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
            var workspace = CreatorWorkspaceProvisioner.ResolveExistingWorkspace(selected);
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
                darkMode: IsDarkMode()))
        {
            return;
        }

        await RunOperationAsync(
            () => _viewModel.RemoveSelectedWorkspaceAsync(),
            "无法移除工作区记录");
    }

    private async void RefreshWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await RunOperationAsync(
            () => _viewModel.RefreshAsync(),
            "无法刷新工作区");
    }

    private async void WorkspaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection ||
            _viewModel is null ||
            WorkspaceList.SelectedItem is not CreatorWorkspaceItemViewModel selected ||
            ReferenceEquals(selected, _viewModel.SelectedWorkspace))
        {
            return;
        }

        await RunOperationAsync(
            () => _viewModel.SelectWorkspaceAsync(selected),
            "无法切换工作区");
    }

    private async void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection || _viewModel is null) return;
        await RunOperationAsync(
            () => _viewModel.SelectProjectAsync(ProjectList.SelectedItem as ThemeProjectItemViewModel),
            "无法切换主题项目");
    }

    private async void RevalidateProject_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await RunOperationAsync(
            () => _viewModel.RevalidateSelectedProjectAsync(),
            "无法重新体检主题项目");
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

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedProject is { } project)
        {
            TryOpenDirectory(project.DirectoryPath);
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
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                });
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

    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(CreatorPromptText.Text);
            _showToast?.Invoke("提示词已复制");
        }
        catch (ExternalException)
        {
            _showToast?.Invoke("剪贴板正忙，请再点一次");
        }
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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RenderState();

    private void RenderState()
    {
        if (_viewModel is null) return;
        _synchronizingSelection = true;
        try
        {
            WorkspaceEmptyPanel.Visibility = _viewModel.Workspaces.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            WorkspaceList.Visibility = _viewModel.Workspaces.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            WorkspaceList.SelectedItem = _viewModel.SelectedWorkspace;

            ProjectLoadingPanel.Visibility = _viewModel.IsBusy
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProjectList.Visibility = !_viewModel.IsBusy && _viewModel.HasProjects
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProjectStatePanel.Visibility = !_viewModel.IsBusy && !_viewModel.HasProjects
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProjectList.SelectedItem = _viewModel.SelectedProject;
            ProjectDetailCard.Visibility = _viewModel.HasSelectedProject
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private string? PickWorkspaceDirectory(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog(GetOwner()) == true ? dialog.FolderName : null;
    }

    private async Task RunOperationAsync(Func<Task> operation, string errorTitle)
    {
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            Win32Exception)
        {
            ShowMessage(errorTitle, exception.Message, ProductDialogKind.Error);
        }
        finally
        {
            RenderState();
        }
    }

    private static void OpenDirectory(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void TryOpenDirectory(string path)
    {
        try
        {
            OpenDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            ShowMessage("无法打开目录", exception.Message, ProductDialogKind.Error);
        }
    }

    private void ShowMessage(string title, string message, ProductDialogKind kind) =>
        ProductDialogWindow.ShowMessage(GetOwner(), title, message, kind, IsDarkMode());

    private Window GetOwner() => Window.GetWindow(this)
        ?? throw new InvalidOperationException("创作项目中心尚未连接到主窗口。");

    private bool IsDarkMode() => _isDarkMode?.Invoke() == true;

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GiB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MiB",
        >= 1024L => $"{bytes / 1024d:0.0} KiB",
        _ => $"{bytes} B",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }
        DataContext = null;
        GC.SuppressFinalize(this);
    }
}
