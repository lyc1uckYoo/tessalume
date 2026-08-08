using System.IO;
using System.Text.Json;
using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal sealed partial class CreatorCenterViewModel
{
    public async Task ActivateAsync()
    {
        ThrowIfDisposed();
        if (!_isInitialized)
        {
            _isInitialized = true;
            if (Workspaces.FirstOrDefault() is { } first)
            {
                await SelectWorkspaceAsync(first, recordUsage: false);
            }
            return;
        }

        if (SelectedWorkspace is not null) await ScanSelectedWorkspaceAsync();
    }

    public async Task AddWorkspaceAsync(string directoryPath, string? displayName = null)
    {
        ThrowIfDisposed();
        _workspaceRepository.Touch(directoryPath, DateTimeOffset.UtcNow, displayName);
        await _savePreferencesAsync();
        ReloadWorkspaceItems(directoryPath);
        if (SelectedWorkspace is not null) await ScanSelectedWorkspaceAsync();
    }

    public async Task SelectWorkspaceAsync(
        CreatorWorkspaceItemViewModel workspace,
        bool recordUsage = true)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(workspace);
        if (recordUsage)
        {
            _workspaceRepository.Touch(workspace.DirectoryPath, DateTimeOffset.UtcNow, workspace.DisplayName);
            await _savePreferencesAsync();
            ReloadWorkspaceItems(workspace.DirectoryPath);
        }
        else
        {
            SelectedWorkspace = workspace;
        }
        await ScanSelectedWorkspaceAsync();
    }

    public async Task RelocateSelectedWorkspaceAsync(string newDirectoryPath)
    {
        ThrowIfDisposed();
        var current = SelectedWorkspace
            ?? throw new InvalidOperationException("尚未选择需要重新定位的工作区。");
        _workspaceRepository.Remove(current.DirectoryPath);
        _workspaceRepository.Touch(newDirectoryPath, DateTimeOffset.UtcNow, current.DisplayName);
        await _savePreferencesAsync();
        ReloadWorkspaceItems(newDirectoryPath);
        await ScanSelectedWorkspaceAsync();
    }

    public async Task RemoveSelectedWorkspaceAsync()
    {
        ThrowIfDisposed();
        if (SelectedWorkspace is null) return;
        _workspaceRepository.Remove(SelectedWorkspace.DirectoryPath);
        await _savePreferencesAsync();
        var nextPath = _workspaceRepository.Entries.Count == 0
            ? null
            : _workspaceRepository.Entries[0].DirectoryPath;
        ReloadWorkspaceItems(nextPath);
        if (SelectedWorkspace is not null)
        {
            await ScanSelectedWorkspaceAsync();
        }
        else
        {
            ClearProjectState("选择一个创作工作区", "创建新工作区，或打开已有工作区继续制作主题。");
        }
    }

    public Task RefreshAsync()
    {
        ThrowIfDisposed();
        return ScanSelectedWorkspaceAsync();
    }

    public async Task<ThemeArchiveExportResult> ExportSelectedProjectAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var project = SelectedProject
            ?? throw new InvalidOperationException("尚未选择要导出的主题项目。");
        if (!project.CanExport)
        {
            throw new InvalidDataException("主题项目仍有阻断错误，暂时不能导出。");
        }
        if (!_acceptance.Passed)
        {
            throw new InvalidDataException("亮暗、输入框、消息框和多视口运行验收尚未全部通过，暂时不能导出。");
        }
        return await _projectExport.ExportAsync(project.DirectoryPath, archivePath, cancellationToken);
    }

    private async Task ScanSelectedWorkspaceAsync()
    {
        var previouslySelectedPath = SelectedProject?.DirectoryPath;
        PrepareForWorkspaceScan();
        if (SelectedWorkspace is null)
        {
            ClearProjectState("选择一个创作工作区", "创建新工作区，或打开已有工作区继续制作主题。");
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        var cancellationToken = _scanCancellation.Token;
        BeginWorkspaceScan();
        try
        {
            var result = await _projectInspection.ScanWorkspaceAsync(
                SelectedWorkspace.DirectoryPath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await ApplyWorkspaceScanResultAsync(result, previouslySelectedPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            WorkspaceExists = Directory.Exists(SelectedWorkspace.DirectoryPath);
            StateTitle = "无法完成工作区扫描";
            StateMessage = exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsBusy = false;
                OnPropertyChanged(nameof(CanExportSelectedProject));
            }
        }
    }

    private void PrepareForWorkspaceScan()
    {
        CancelCurrentScan();
        StopProjectWatcher();
        CancelDevelopmentOperation();
        Projects.Clear();
        HealthGroups.Clear();
        SetSelectedProject(null);
    }

    private void BeginWorkspaceScan()
    {
        IsBusy = true;
        OnPropertyChanged(nameof(CanExportSelectedProject));
        StateTitle = "正在扫描工作区";
        StateMessage = "正在读取 themes 目录并检查主题项目，请稍候。";
        WorkspaceSummary = SelectedWorkspace!.DirectoryPath;
    }

    private async Task ApplyWorkspaceScanResultAsync(
        CreatorWorkspaceScanResult result,
        string? previouslySelectedPath)
    {
        WorkspaceExists = result.Exists;
        SelectedWorkspace!.SetExists(result.Exists);
        ApplyWorkspaceContract(result.Contract);
        foreach (var project in result.Projects)
        {
            Projects.Add(new ThemeProjectItemViewModel(project));
        }
        OnPropertyChanged(nameof(HasProjects));
        UpdateWorkspaceSummary();

        var workspaceError = result.Health.Checks.FirstOrDefault(check =>
            check.Severity == ThemeProjectHealthSeverity.Error);
        if (workspaceError is not null)
        {
            StateTitle = workspaceError.Title;
            StateMessage = workspaceError.Message;
            return;
        }
        if (Projects.Count == 0)
        {
            StateTitle = "工作区已经准备好";
            StateMessage = "在 Codex 中完成主题创作后，项目会自动出现在这里。";
            return;
        }

        StateTitle = "选择一个主题项目";
        StateMessage = "查看完整体检结果，修复问题后即可导出分享包。";
        var projectToSelect = previouslySelectedPath is null
            ? Projects[0]
            : Projects.FirstOrDefault(project =>
                PathsEqual(project.DirectoryPath, previouslySelectedPath)) ?? Projects[0];
        await SelectProjectAsync(projectToSelect);
    }

    private void ReloadWorkspaceItems(string? selectedPath = null)
    {
        selectedPath ??= SelectedWorkspace?.DirectoryPath;
        Workspaces.Clear();
        foreach (var record in _workspaceRepository.Entries)
        {
            Workspaces.Add(new CreatorWorkspaceItemViewModel(record));
        }
        SelectedWorkspace = selectedPath is null
            ? null
            : Workspaces.FirstOrDefault(item => PathsEqual(item.DirectoryPath, selectedPath));
        OnPropertyChanged(nameof(WorkspaceCountText));
    }

    private void ClearProjectState(string title, string message)
    {
        CancelCurrentScan();
        StopProjectWatcher();
        CancelDevelopmentOperation();
        Projects.Clear();
        HealthGroups.Clear();
        SetSelectedProject(null);
        WorkspaceExists = SelectedWorkspace is not null && Directory.Exists(SelectedWorkspace.DirectoryPath);
        StateTitle = title;
        StateMessage = message;
        WorkspaceSummary = SelectedWorkspace?.DirectoryPath ?? "尚未选择工作区";
        WorkspaceVersionText = "尚未读取版本";
        WorkspaceVersionDetail = "选择可用工作区后检查工具链版本。";
        WorkspaceVersionTone = "idle";
        CanUpgradeWorkspace = false;
        IsBusy = false;
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(CanExportSelectedProject));
    }

    private void ApplyWorkspaceContract(CreatorWorkspaceContractInfo contract)
    {
        WorkspaceVersionText = contract.State switch
        {
            CreatorWorkspaceContractState.Current => $"工作区 v{contract.WorkspaceVersion} · 最新",
            CreatorWorkspaceContractState.Legacy => "旧版工作区 · 可升级",
            CreatorWorkspaceContractState.UpgradeAvailable => $"工作区 v{contract.WorkspaceVersion ?? "未知"} · 可升级",
            CreatorWorkspaceContractState.Newer => $"工作区 v{contract.WorkspaceVersion} · 来自新版",
            CreatorWorkspaceContractState.Invalid => "版本标记异常 · 可修复",
            _ => "非标准工作区",
        };
        WorkspaceVersionDetail = contract.Message;
        WorkspaceVersionTone = contract.State switch
        {
            CreatorWorkspaceContractState.Current => "ready",
            CreatorWorkspaceContractState.Newer or CreatorWorkspaceContractState.Missing => "warning",
            _ => "upgrade",
        };
        CanUpgradeWorkspace = contract.CanUpgrade && WorkspaceExists;
    }
}
