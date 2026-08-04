using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Tessalume.Core.Creator;
using Tessalume.Core.Themes;

namespace Tessalume.App.Creator;

internal sealed partial class CreatorCenterViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CreatorWorkspaceStore _workspaceStore;
    private readonly ThemeProjectScanner _scanner;
    private readonly ThemeArchiveWriter _archiveWriter;
    private readonly Func<Task> _savePreferencesAsync;
    private readonly CreatorRuntimeBridge _runtimeBridge;
    private readonly SynchronizationContext? _synchronizationContext;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _developmentCancellation;
    private ThemeProjectWatcher? _projectWatcher;
    private CreatorWorkspaceItemViewModel? _selectedWorkspace;
    private ThemeProjectItemViewModel? _selectedProject;
    private bool _isBusy;
    private bool _isDevelopmentBusy;
    private bool _isWatching;
    private bool _autoApplyEnabled;
    private bool _isInitialized;
    private bool _workspaceExists;
    private string _stateTitle = "选择一个创作工作区";
    private string _stateMessage = "创建新工作区，或打开已有工作区继续制作主题。";
    private string _workspaceSummary = "尚未选择工作区";
    private string _watcherStatusText = "选择项目后开始监听";
    private string _watcherActivityText = "Tessalume 会在文件写入稳定后自动体检";
    private string _watcherStatusTone = "idle";
    private string _codexStatusText = "Codex 未连接";
    private string _codexModeText = "明暗状态未知";
    private string _codexStatusTone = "idle";
    private string _lastAppliedText = "尚未从创作项目应用";
    private bool _disposed;

    public CreatorCenterViewModel(
        CreatorWorkspaceStore workspaceStore,
        Func<Task> savePreferencesAsync,
        CreatorRuntimeBridge? runtimeBridge = null)
    {
        _workspaceStore = workspaceStore;
        _savePreferencesAsync = savePreferencesAsync;
        _runtimeBridge = runtimeBridge ?? CreatorRuntimeBridge.Unavailable;
        _synchronizationContext = SynchronizationContext.Current;
        var loader = new ThemePackageLoader();
        _scanner = new ThemeProjectScanner(loader);
        _archiveWriter = new ThemeArchiveWriter(loader);
        ReloadWorkspaceItems();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CreatorWorkspaceItemViewModel> Workspaces { get; } = [];

    public ObservableCollection<ThemeProjectItemViewModel> Projects { get; } = [];

    public ObservableCollection<ThemeHealthGroupViewModel> HealthGroups { get; } = [];

    public CreatorWorkspaceItemViewModel? SelectedWorkspace
    {
        get => _selectedWorkspace;
        private set
        {
            if (SetField(ref _selectedWorkspace, value))
            {
                OnPropertyChanged(nameof(HasSelectedWorkspace));
                OnPropertyChanged(nameof(CanRelocateWorkspace));
            }
        }
    }

    public ThemeProjectItemViewModel? SelectedProject
    {
        get => _selectedProject;
        private set
        {
            if (SetField(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(HasSelectedProject));
                OnPropertyChanged(nameof(CanExportSelectedProject));
                NotifyDevelopmentCommandsChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                NotifyDevelopmentCommandsChanged();
            }
        }
    }

    public bool IsDevelopmentBusy
    {
        get => _isDevelopmentBusy;
        private set
        {
            if (SetField(ref _isDevelopmentBusy, value))
            {
                NotifyDevelopmentCommandsChanged();
            }
        }
    }

    public bool IsWatching
    {
        get => _isWatching;
        private set => SetField(ref _isWatching, value);
    }

    public bool AutoApplyEnabled
    {
        get => _autoApplyEnabled;
        set
        {
            if (!SetField(ref _autoApplyEnabled, value)) return;
            WatcherActivityText = value
                ? "自动应用已开启：仅在体检无错误且 Codex 已连接时执行"
                : "自动应用默认关闭；文件变化仍会自动重新体检";
        }
    }

    public bool WorkspaceExists
    {
        get => _workspaceExists;
        private set
        {
            if (SetField(ref _workspaceExists, value))
            {
                OnPropertyChanged(nameof(CanRelocateWorkspace));
            }
        }
    }

    public string StateTitle
    {
        get => _stateTitle;
        private set => SetField(ref _stateTitle, value);
    }

    public string StateMessage
    {
        get => _stateMessage;
        private set => SetField(ref _stateMessage, value);
    }

    public string WorkspaceSummary
    {
        get => _workspaceSummary;
        private set => SetField(ref _workspaceSummary, value);
    }

    public string WatcherStatusText
    {
        get => _watcherStatusText;
        private set => SetField(ref _watcherStatusText, value);
    }

    public string WatcherActivityText
    {
        get => _watcherActivityText;
        private set => SetField(ref _watcherActivityText, value);
    }

    public string WatcherStatusTone
    {
        get => _watcherStatusTone;
        private set => SetField(ref _watcherStatusTone, value);
    }

    public string CodexStatusText
    {
        get => _codexStatusText;
        private set => SetField(ref _codexStatusText, value);
    }

    public string CodexModeText
    {
        get => _codexModeText;
        private set => SetField(ref _codexModeText, value);
    }

    public string CodexStatusTone
    {
        get => _codexStatusTone;
        private set => SetField(ref _codexStatusTone, value);
    }

    public string LastAppliedText
    {
        get => _lastAppliedText;
        private set => SetField(ref _lastAppliedText, value);
    }

    public string WorkspaceCountText => Workspaces.Count == 0
        ? "尚无记录"
        : $"最近 {Workspaces.Count} 个";

    public bool HasSelectedWorkspace => SelectedWorkspace is not null;

    public bool HasSelectedProject => SelectedProject is not null;

    public bool HasProjects => Projects.Count > 0;

    public bool CanRelocateWorkspace => SelectedWorkspace is not null && !WorkspaceExists;

    public bool CanExportSelectedProject => SelectedProject?.CanExport == true && !IsBusy;

    public bool CanRevalidateSelectedProject =>
        SelectedProject is not null && !IsBusy && !IsDevelopmentBusy;

    public bool CanApplySelectedProject =>
        SelectedProject?.CanExport == true && !IsBusy && !IsDevelopmentBusy;

    public bool CanToggleCodexMode =>
        CodexStatusTone == "ready" && !IsBusy && !IsDevelopmentBusy;

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

        if (SelectedWorkspace is not null)
        {
            await ScanSelectedWorkspaceAsync();
        }
    }

    public async Task AddWorkspaceAsync(string directoryPath, string? displayName = null)
    {
        ThrowIfDisposed();
        _workspaceStore.Touch(directoryPath, DateTimeOffset.UtcNow, displayName);
        await _savePreferencesAsync();
        ReloadWorkspaceItems(directoryPath);
        if (SelectedWorkspace is not null)
        {
            await ScanSelectedWorkspaceAsync();
        }
    }

    public async Task SelectWorkspaceAsync(
        CreatorWorkspaceItemViewModel workspace,
        bool recordUsage = true)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(workspace);
        if (recordUsage)
        {
            _workspaceStore.Touch(workspace.DirectoryPath, DateTimeOffset.UtcNow, workspace.DisplayName);
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
        _workspaceStore.Remove(current.DirectoryPath);
        _workspaceStore.Touch(newDirectoryPath, DateTimeOffset.UtcNow, current.DisplayName);
        await _savePreferencesAsync();
        ReloadWorkspaceItems(newDirectoryPath);
        await ScanSelectedWorkspaceAsync();
    }

    public async Task RemoveSelectedWorkspaceAsync()
    {
        ThrowIfDisposed();
        if (SelectedWorkspace is null) return;
        _workspaceStore.Remove(SelectedWorkspace.DirectoryPath);
        await _savePreferencesAsync();
        var nextPath = _workspaceStore.Entries.Count == 0
            ? null
            : _workspaceStore.Entries[0].DirectoryPath;
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
        return await _archiveWriter.ExportAsync(project.DirectoryPath, archivePath, cancellationToken);
    }

    private async Task ScanSelectedWorkspaceAsync()
    {
        var previouslySelectedPath = SelectedProject?.DirectoryPath;
        CancelCurrentScan();
        StopProjectWatcher();
        CancelDevelopmentOperation();
        Projects.Clear();
        HealthGroups.Clear();
        SelectedProject = null;
        if (SelectedWorkspace is null)
        {
            ClearProjectState("选择一个创作工作区", "创建新工作区，或打开已有工作区继续制作主题。");
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        var cancellationToken = _scanCancellation.Token;
        IsBusy = true;
        OnPropertyChanged(nameof(CanExportSelectedProject));
        StateTitle = "正在扫描工作区";
        StateMessage = "正在读取 themes 目录并检查主题项目，请稍候。";
        WorkspaceSummary = SelectedWorkspace.DirectoryPath;
        try
        {
            var result = await _scanner.ScanWorkspaceAsync(
                SelectedWorkspace.DirectoryPath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceExists = result.Exists;
            SelectedWorkspace.SetExists(result.Exists);
            foreach (var project in result.Projects)
            {
                Projects.Add(new ThemeProjectItemViewModel(project));
            }
            OnPropertyChanged(nameof(HasProjects));

            var ready = Projects.Count(project => project.CanExport);
            var blocked = Projects.Count(project => project.StatusTone == "error");
            WorkspaceSummary = Projects.Count == 0
                ? SelectedWorkspace.DirectoryPath
                : $"{Projects.Count} 个项目 · {ready} 个可导出 · {blocked} 个需要修复";

            var workspaceError = result.Health.Checks.FirstOrDefault(check =>
                check.Severity == ThemeProjectHealthSeverity.Error);
            if (workspaceError is not null)
            {
                StateTitle = workspaceError.Title;
                StateMessage = workspaceError.Message;
            }
            else if (Projects.Count == 0)
            {
                StateTitle = "工作区已经准备好";
                StateMessage = "在 Codex 中完成主题创作后，项目会自动出现在这里。";
            }
            else
            {
                StateTitle = "选择一个主题项目";
                StateMessage = "查看完整体检结果，修复问题后即可导出分享包。";
                var projectToSelect = previouslySelectedPath is null
                    ? Projects[0]
                    : Projects.FirstOrDefault(project =>
                        PathsEqual(project.DirectoryPath, previouslySelectedPath)) ?? Projects[0];
                await SelectProjectAsync(projectToSelect);
            }
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

    private void ReloadWorkspaceItems(string? selectedPath = null)
    {
        selectedPath ??= SelectedWorkspace?.DirectoryPath;
        Workspaces.Clear();
        foreach (var record in _workspaceStore.Snapshot())
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
        SelectedProject = null;
        WorkspaceExists = SelectedWorkspace is not null && Directory.Exists(SelectedWorkspace.DirectoryPath);
        StateTitle = title;
        StateMessage = message;
        WorkspaceSummary = SelectedWorkspace?.DirectoryPath ?? "尚未选择工作区";
        IsBusy = false;
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(CanExportSelectedProject));
    }

    private void StopProjectWatcher(bool keepStatus = false)
    {
        if (_projectWatcher is not null)
        {
            _projectWatcher.Changed -= ProjectWatcher_Changed;
            _projectWatcher.Faulted -= ProjectWatcher_Faulted;
            _projectWatcher.Dispose();
            _projectWatcher = null;
        }
        IsWatching = false;
        if (keepStatus) return;
        WatcherStatusTone = "idle";
        WatcherStatusText = SelectedProject is null ? "选择项目后开始监听" : "文件监听已停止";
        WatcherActivityText = "Tessalume 会在文件写入稳定后自动体检";
    }

    private CancellationTokenSource BeginDevelopmentOperation(CancellationToken cancellationToken)
    {
        CancelDevelopmentOperation();
        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _developmentCancellation = operation;
        return operation;
    }

    private void CancelDevelopmentOperation()
    {
        _developmentCancellation?.Cancel();
        _developmentCancellation = null;
        IsDevelopmentBusy = false;
    }

    private bool CompleteDevelopmentOperation(CancellationTokenSource operation)
    {
        var isCurrent = ReferenceEquals(_developmentCancellation, operation);
        if (isCurrent)
        {
            _developmentCancellation = null;
        }
        operation.Dispose();
        return isCurrent;
    }

    private void CancelCurrentScan()
    {
        if (_scanCancellation is null) return;
        _scanCancellation.Cancel();
        _scanCancellation.Dispose();
        _scanCancellation = null;
    }

    private void NotifyDevelopmentCommandsChanged()
    {
        OnPropertyChanged(nameof(CanRevalidateSelectedProject));
        OnPropertyChanged(nameof(CanApplySelectedProject));
        OnPropertyChanged(nameof(CanToggleCodexMode));
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopProjectWatcher();
        CancelDevelopmentOperation();
        CancelCurrentScan();
    }
}
