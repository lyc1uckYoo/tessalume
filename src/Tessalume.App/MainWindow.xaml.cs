using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tessalume.App.Creator;
using Tessalume.App.Features.About;
using Tessalume.App.Features.Diagnostics;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Personalization;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.App.Features.Pets;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Backup;
using Tessalume.Core.Compatibility;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;
using Tessalume.Core.Updates;
using Microsoft.Win32;

namespace Tessalume.App;

public partial class MainWindow : Window, IAsyncDisposable
{
    private enum ThemeLibraryFilter
    {
        All,
        Light,
        Dark,
    }

    private readonly PortableLayout _layout;
    private readonly ObservableCollection<ThemeCardModel> _themes = [];
    private readonly ObservableCollection<ThemeCardModel> _visibleThemes = [];
    private readonly StudioStateStore _stateStore;
    private readonly UiPreferencesStore _preferencesStore;
    private readonly CreatorWorkspaceStore _creatorWorkspaces;
    private readonly AboutDataService _aboutDataService;
    private readonly LoopbackCdpDiscovery _launcherDiscovery = new();
    private readonly CodexPackageLauncher _launcher;
    private readonly ThemeRuntime _runtime;
    private readonly CodexUsageReader _usageReader = new();
    private readonly CompatibilityPackStore _compatibilityPacks;
    private readonly DiagnosticsInspectionService _diagnosticsService;
    private readonly AboutUpdateService _aboutUpdateService;
    private readonly PersonalImageStore _personalImageStore;
    private readonly ArtworkThemeDefaultsStore _artworkDefaultsStore = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly CancellationTokenSource _backupCancellation = new();
    private readonly CancellationTokenSource _personalizationCancellation = new();
    private readonly HashSet<string> _favoriteThemeIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThemeUsageRecord> _themeUsage =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThemeVisualSettings> _themeVisualSettings =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThemeVisualSettingsOverride> _themeVisualOverrides =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThemeArtworkDefaultsDocument> _themeArtworkDefaults =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ArtworkThemeDefaultsLoadResult> _themeArtworkDefaultLoads =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThemeVisualSettingsResolution> _themeVisualResolutions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CreatorPromptDraftStore _creatorPromptDrafts;
    private ThemeCardModel? _selectedTheme;
    private ThemeQuickSwitchWindow? _quickSwitchWindow;
    private string? _activeThemeId;
    private string? _lastThemeId;
    private string _engineStateText = "主题引擎待启动";
    private int? _activePort;
    private bool _showFavorites;
    private bool _darkMode;
    private bool _quickSwitchVisible = true;
    private bool _suppressQuickSwitchPreferenceChange;
    private bool? _codexDarkMode;
    private bool _automaticUpdateChecks;
    private bool _updateCheckInProgress;
    private bool _automaticUpdateCheckScheduled;
    private bool _rollbackInProgress;
    private bool _startupInitialized;
    private bool _shutdownRequested;
    private bool _userDataRestoreCompleted;
    private int _disposeStarted;
    private bool _onboardingCompleted;
    private bool _uiInitialized;
    private bool _mainContentLoaded;
    private bool _editingVisualDarkMode;
    private ThemeLibraryFilter _themeLibraryFilter;
    private string _themeSearchQuery = string.Empty;
    private string _themeLibrarySort = ThemeLibraryState.DefaultSort;
    private string _updateStatusMessage = "尚未检查更新";
    private DateTimeOffset? _lastUpdateCheckAt;
    private ReleaseUpdate? _availableUpdate;
    private PortableUpdateResult? _startupUpdateResult;
    private UpdateRollbackInfo? _availableRollback;
    private string? _startupHealthToken;
    private DispatcherTimer? _visualSettingsDebounce;
    private DispatcherTimer? _toastTimer;
    private AppRoute _currentRoute = AppRoute.ThemeLibrary;

    internal MainWindow(
        PortableLayout? layout = null,
        PetApplicationServiceOptions? petOptions = null,
        IPetCommandClipboard? petClipboard = null)
    {
        _layout = layout ?? PortableLayout.Create();
        _petOptions = petOptions;
        _petClipboard = petClipboard ?? new SystemPetCommandClipboard();
        _personalImageStore = new PersonalImageStore(_layout.DataDirectory);
        _stateStore = new StudioStateStore(_layout.DataDirectory);
        _preferencesStore = new UiPreferencesStore(_layout.DataDirectory);
        _launcher = new CodexPackageLauncher(_launcherDiscovery);
        BuiltInAssetInstaller.EnsureCompatibilityInstalled(_layout);
        _compatibilityPacks = new CompatibilityPackStore(
            Path.Combine(_layout.RootDirectory, "Compatibility"),
            _layout.DataDirectory,
            Version.Parse(BrandInfo.Version),
            ThemeRuntime.ContractVersion);
        _diagnosticsService = new DiagnosticsInspectionService(
            _layout,
            _stateStore,
            _compatibilityPacks,
            _launcher);
        _aboutUpdateService = new AboutUpdateService(
            _layout,
            _compatibilityPacks,
            Version.Parse(BrandInfo.Version),
            $"{BrandInfo.ProductName}.exe");
        _runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(() => _compatibilityPacks.Resolve().RuntimeAssets),
            _personalImageStore.ResolveForRuntime);

        var hadSavedPreferences = _preferencesStore.Exists;
        var preferences = _preferencesStore.Load();
        _darkMode = preferences.DarkMode;
        _onboardingCompleted = preferences.OnboardingCompleted || hadSavedPreferences;
        _automaticUpdateChecks = preferences.AutomaticUpdateChecks;
        _quickSwitchVisible = preferences.QuickSwitchVisible;
        _lastUpdateCheckAt = preferences.LastUpdateCheckAt;
        _themeLibrarySort = ThemeLibraryState.NormalizeSort(preferences.ThemeLibrarySort);
        _creatorWorkspaces = new CreatorWorkspaceStore(preferences.RecentCreatorWorkspaces);
        _creatorPromptDrafts = new CreatorPromptDraftStore(
            preferences.CreatorPromptDrafts,
            preferences.CreatorPromptDraft);
        _aboutDataService = new AboutDataService(
            _layout.RootDirectory,
            _layout.DataDirectory,
            _layout.ThemesDirectory,
            BuiltInAssetInstaller.ThemeIds);
        _favoriteThemeIds.UnionWith(preferences.FavoriteThemeIds.Where(id => !string.IsNullOrWhiteSpace(id)));
        foreach (var usage in ThemeLibraryState.NormalizeUsage(preferences.RecentThemeUsage))
        {
            _themeUsage[usage.ThemeId] = usage;
        }
        foreach (var (themeId, settings) in preferences.ThemeVisualOverrides)
        {
            if (!string.IsNullOrWhiteSpace(themeId))
            {
                _themeVisualOverrides[themeId] =
                    (settings ?? new ThemeVisualSettingsOverride()).Normalize();
            }
        }
        _runtime.StatusChanged += Runtime_StatusChanged;
        Closed += MainWindow_Closed;
    }

    private void EnsureMainUiInitialized()
    {
        if (_uiInitialized) return;

        InitializeComponent();
        InitializePetCenterFeature();
        InitializeArtworkWorkbench();
        DiagnosticsPage.RefreshRequested += DiagnosticsPage_RefreshRequested;
        DiagnosticsPage.OpenLogDirectoryRequested += DiagnosticsPage_OpenLogDirectoryRequested;
        DiagnosticsPage.RestoreBuiltInThemesRequested += DiagnosticsPage_RestoreBuiltInThemesRequested;
        AboutPage.OpenRootDirectoryRequested += AboutPage_OpenRootDirectoryRequested;
        AboutPage.OpenDataDirectoryRequested += AboutPage_OpenDataDirectoryRequested;
        AboutPage.BackupRequested += AboutPage_BackupRequested;
        AboutPage.RestoreRequested += AboutPage_RestoreRequested;
        AboutPage.StartupSettingChanged += AboutPage_StartupSettingChanged;
        AboutPage.AutomaticUpdateSettingChanged += AboutPage_AutomaticUpdateSettingChanged;
        AboutPage.CheckForUpdatesRequested += AboutPage_CheckForUpdatesRequested;
        AboutPage.RollbackRequested += AboutPage_RollbackRequested;
        ThemeSortComboBox.SelectedValue = _themeLibrarySort;
        _uiInitialized = true;
        CreatorCenter.Configure(
            _layout.RootDirectory,
            _creatorWorkspaces,
            SavePreferencesAsync,
            workspacePath => _creatorPromptDrafts.Get(workspacePath),
            (workspacePath, draft) =>
            {
                _creatorPromptDrafts.Set(workspacePath, draft);
                return SavePreferencesAsync();
            },
            new CreatorRuntimeBridge(
                ApplyCreatorProjectAsync,
                ReadCreatorRuntimeStatusAsync,
                ToggleCreatorRuntimeColorSchemeAsync,
                RunCreatorAcceptanceAsync),
            () => _darkMode,
            message => ShowToast(message));
        FitWindowToWorkArea();
        SourceInitialized += (_, _) => NativeTitleBar.Apply(this, _darkMode);
        ThemeItems.ItemsSource = _visibleThemes;
        _visualSettingsDebounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(140),
        };
        _visualSettingsDebounce.Tick += VisualSettingsDebounce_Tick;
        _toastTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3.2),
        };
        _toastTimer.Tick += ToastTimer_Tick;
        ApplyStudioTheme(_darkMode);
        // The quick-start scan already provides validated theme metadata. Select from
        // that lightweight catalog immediately so Settings never waits for preview
        // decoding before its artwork controls become usable.
        ShowThemes(_activeThemeId);
        UpdateStartupButton();
        UpdateUpdateControls();
        UpdateVisualAdjustmentControls();
    }

    private void FitWindowToWorkArea()
    {
        const double outerMargin = 24;
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(1, workArea.Width - outerMargin);
        var availableHeight = Math.Max(1, workArea.Height - outerMargin);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
    }

    private void AdaptiveViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        // Keep text at the native WPF/DPI scale. The previous whole-window
        // LayoutTransform made ClearType text soft at 125–200% DPI and hid
        // layout problems instead of allowing each page to reflow and scroll.
        AdaptiveScale.ScaleX = 1;
        AdaptiveScale.ScaleY = 1;
        var compact = e.NewSize.Width < 900;
        ShellSidebarColumn.Width = new GridLength(compact ? 184 : 226);
        if (InfoPage is not null)
        {
            InfoPage.Margin = compact
                ? new Thickness(16, 16, 16, 14)
                : new Thickness(32, 25, 32, 22);
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ThemeDetailPanel.Visibility == Visibility.Visible)
        {
            CloseThemeDetailPanel();
            e.Handled = true;
            return;
        }
        if (_currentRoute == AppRoute.ArtworkStudio && Keyboard.FocusedElement is not TextBox)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                ArtworkWorkbench.Undo();
                e.Handled = true;
                return;
            }
            if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) ||
                (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z))
            {
                ArtworkWorkbench.Redo();
                e.Handled = true;
                return;
            }
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            ShowThemes();
            ThemeSearchBox.Focus();
            ThemeSearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.I)
        {
            ImportTheme_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.I)
        {
            ImportArchive_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F5)
        {
            RefreshThemes_Click(sender, e);
            e.Handled = true;
        }
    }

    internal async Task StartInQuickModeAsync()
    {
        if (_startupInitialized) return;
        _startupInitialized = true;
        await ReloadThemesAsync(loadPreviews: false);
        var state = await _stateStore.LoadAsync();
        if (state is null && !_onboardingCompleted)
        {
            ShowMainInterface();
            var codexInstalled = await CodexPackageLauncher.IsCodexInstalledAsync();
            if (!FirstRunWindow.Show(this, _darkMode, codexInstalled))
            {
                Close();
                return;
            }

            _onboardingCompleted = true;
            await SavePreferencesAsync();
            if (_quickSwitchVisible)
            {
                OpenQuickSwitchWindow();
            }
            SetEngineState("等待选择主题");
            SetStatus(codexInstalled
                ? "欢迎使用 Tessalume，请选择喜欢的主题后手动应用"
                : "可以先浏览主题；应用前请先安装 Windows 版 Codex Desktop");
            await ShowStartupUpdateResultAsync();
            ScheduleAutomaticUpdateCheck();
            await ConfirmPendingUpdateHealthAsync();
            return;
        }

        if (_quickSwitchVisible)
        {
            OpenQuickSwitchWindow();
        }
        if (state is not null)
        {
            await TryResumeAsync(state);
        }
        else
        {
            SetEngineState("Codex 默认外观");
            SetStatus("请选择主题并手动应用到 Codex");
        }
        if (!_quickSwitchVisible)
        {
            ShowMainInterface();
        }
        await ShowStartupUpdateResultAsync();
        ScheduleAutomaticUpdateCheck();
        await ConfirmPendingUpdateHealthAsync();
    }

    private async Task ConfirmPendingUpdateHealthAsync()
    {
        if (_startupHealthToken is not { } healthToken) return;
        await UpdateBootstrapper.ConfirmStartupHealthyAsync(_layout, healthToken);
        _startupHealthToken = null;
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        await DisposeAsync();
        if (!_shutdownRequested)
        {
            _shutdownRequested = true;
            Application.Current.Shutdown();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;

        CloseQuickSwitchWindow(rememberClosed: false);
        _quickSwitchWindow = null;
        _runtime.StatusChanged -= Runtime_StatusChanged;
        var visualSettingsPending =
            _preferencesDirty || _visualSettingsDebounce?.IsEnabled == true;
        if (_visualSettingsDebounce is not null)
        {
            _visualSettingsDebounce.Stop();
            _visualSettingsDebounce.Tick -= VisualSettingsDebounce_Tick;
            _visualSettingsDebounce = null;
        }
        if (_toastTimer is not null)
        {
            _toastTimer.Stop();
            _toastTimer.Tick -= ToastTimer_Tick;
            _toastTimer = null;
        }
        _updateCancellation.Cancel();
        _updateCancellation.Dispose();
        _backupCancellation.Cancel();
        _backupCancellation.Dispose();
        _visualApplyCancellation?.Cancel();
        _visualApplyCancellation?.Dispose();
        _visualApplyCancellation = null;
        _personalizationCancellation.Cancel();
        _personalizationCancellation.Dispose();
        _petCancellation.Cancel();
        if (_petApplicationService is not null)
        {
            await _petApplicationService.WaitForIdleAsync();
            _petApplicationService.Dispose();
            _petApplicationService = null;
        }
        _petCancellation.Dispose();
        PetCenterPage?.Dispose();
        _aboutUpdateService.Dispose();
        if (CreatorCenter is not null)
        {
            await CreatorCenter.FlushPendingPromptDraftAsync();
            CreatorCenter.Dispose();
        }
        DisposeArtworkWorkbench();
        if (visualSettingsPending && !_userDataRestoreCompleted)
        {
            await SavePreferencesAsync();
        }
        _preferencesStore.Dispose();
        await _runtime.DisposeAsync();
        _launcherDiscovery.Dispose();
        GC.SuppressFinalize(this);
    }

}
