using System.Windows.Controls;
using System.Windows.Threading;

namespace Tessalume.App.Creator;

public partial class CreatorCenterView : UserControl, IDisposable
{
    private CreatorCenterViewModel? _viewModel;
    private ICreatorWorkspaceProvisioningService? _provisioner;
    private Func<bool>? _isDarkMode;
    private Action<string>? _showToast;
    private Func<string?, CreatorPromptDraft>? _loadPromptDraft;
    private Func<string?, CreatorPromptDraft, Task>? _savePromptDraftAsync;
    private CreatorPromptDraft _promptDraft = new();
    private string? _promptWorkspacePath;
    private readonly DispatcherTimer _promptSaveTimer;
    private bool _updatingPrompt;
    private bool _promptEditorExpanded;
    private bool _promptDraftDirty;
    private CreatorCenterRoute _currentRoute = CreatorCenterRoute.Workspace;
    private bool _disposed;

    public CreatorCenterView()
    {
        _promptSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        _promptSaveTimer.Tick += PromptSaveTimer_Tick;
        InitializeComponent();
    }

    internal void Configure(
        string applicationRoot,
        CreatorWorkspaceStore workspaceStore,
        Func<Task> savePreferencesAsync,
        Func<string?, CreatorPromptDraft> loadPromptDraft,
        Func<string?, CreatorPromptDraft, Task> savePromptDraftAsync,
        CreatorRuntimeBridge runtimeBridge,
        Func<bool> isDarkMode,
        Action<string> showToast)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_viewModel is not null) return;

        _provisioner = CreateProvisioningService(applicationRoot);
        _isDarkMode = isDarkMode;
        _showToast = showToast;
        _loadPromptDraft = loadPromptDraft;
        _savePromptDraftAsync = savePromptDraftAsync;
        LoadPromptDraft(loadPromptDraft(null));
        _viewModel = new CreatorCenterViewModel(workspaceStore, savePreferencesAsync, runtimeBridge);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = _viewModel;
        UpdatePromptGuidanceState(copied: false);
        RenderState();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The presentation layer depends on the application port by design.")]
    private static ICreatorWorkspaceProvisioningService CreateProvisioningService(string applicationRoot) =>
        new CreatorWorkspaceProvisioningService(applicationRoot);

    internal async Task ActivateAsync()
    {
        if (_viewModel is null || _disposed) return;
        await RunOperationAsync(
            () => _viewModel.ActivateAsync(),
            "无法打开创作项目中心");
        await SwitchPromptContextAsync(_viewModel.SelectedWorkspace?.DirectoryPath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _promptSaveTimer.Stop();
        _promptSaveTimer.Tick -= PromptSaveTimer_Tick;
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
