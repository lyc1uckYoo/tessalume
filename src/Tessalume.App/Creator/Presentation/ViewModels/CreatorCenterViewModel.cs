using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal sealed partial class CreatorCenterViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ICreatorWorkspaceRepository _workspaceRepository;
    private readonly ICreatorProjectInspectionService _projectInspection;
    private readonly ICreatorProjectExportService _projectExport;
    private readonly ICreatorWorkflowEvaluator _workflowEvaluator;
    private readonly ICreatorAcceptanceService _acceptanceService;
    private readonly Func<Task> _savePreferencesAsync;
    private readonly ICreatorRuntimeGateway _runtimeGateway;
    private readonly IThemeProjectWatcherFactory _watcherFactory;
    private readonly SynchronizationContext? _synchronizationContext;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _developmentCancellation;
    private IThemeProjectWatcher? _projectWatcher;
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
    private string _workspaceVersionText = "尚未读取版本";
    private string _workspaceVersionDetail = "选择工作区后检查工具链版本。";
    private string _workspaceVersionTone = "idle";
    private bool _canUpgradeWorkspace;
    private bool _disposed;

    public CreatorCenterViewModel(
        CreatorWorkspaceStore workspaceStore,
        Func<Task> savePreferencesAsync,
        ICreatorRuntimeGateway? runtimeGateway = null)
        : this(
            workspaceStore,
            savePreferencesAsync,
            CreatorApplicationServices.CreateDefault(runtimeGateway))
    {
    }

    internal CreatorCenterViewModel(
        ICreatorWorkspaceRepository workspaceRepository,
        Func<Task> savePreferencesAsync,
        CreatorApplicationServices services)
    {
        _workspaceRepository = workspaceRepository;
        _savePreferencesAsync = savePreferencesAsync;
        _projectInspection = services.ProjectInspection;
        _projectExport = services.ProjectExport;
        _workflowEvaluator = services.WorkflowEvaluator;
        _acceptanceService = services.Acceptance;
        _runtimeGateway = services.Runtime;
        _watcherFactory = services.WatcherFactory;
        _synchronizationContext = SynchronizationContext.Current;
        RenderAcceptance();
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
                UpdateGuidance();
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
                UpdateGuidance();
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
                UpdateGuidance();
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
                UpdateGuidance();
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
                UpdateGuidance();
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

    public string WorkspaceVersionText
    {
        get => _workspaceVersionText;
        private set => SetField(ref _workspaceVersionText, value);
    }

    public string WorkspaceVersionDetail
    {
        get => _workspaceVersionDetail;
        private set => SetField(ref _workspaceVersionDetail, value);
    }

    public string WorkspaceVersionTone
    {
        get => _workspaceVersionTone;
        private set => SetField(ref _workspaceVersionTone, value);
    }

    public bool CanUpgradeWorkspace
    {
        get => _canUpgradeWorkspace;
        private set => SetField(ref _canUpgradeWorkspace, value);
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

    public bool CanRunAcceptance =>
        SelectedProject is not null && !IsBusy && !IsDevelopmentBusy;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

}
