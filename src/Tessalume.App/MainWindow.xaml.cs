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
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;
using Tessalume.Core.Updates;
using Microsoft.Win32;

namespace Tessalume.App;

public partial class MainWindow : Window, IAsyncDisposable
{
    private const string BuiltInTemplateFolderName = "theme-template-v1";
    private const string CreatorWorkspaceFolderName = "Tessalume-Creator";

    private enum RightPane
    {
        Themes,
        ImportGuide,
        UsageGuide,
        Settings,
        Diagnostics,
        About,
    }

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
    private readonly LoopbackCdpDiscovery _launcherDiscovery = new();
    private readonly CodexPackageLauncher _launcher;
    private readonly ThemeRuntime _runtime;
    private readonly CodexUsageReader _usageReader = new();
    private readonly ReleaseUpdateClient _updateClient;
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly HashSet<string> _favoriteThemeIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThemeVisualSettings> _themeVisualSettings =
        new(StringComparer.OrdinalIgnoreCase);
    private ThemeCardModel? _selectedTheme;
    private ThemeQuickSwitchWindow? _quickSwitchWindow;
    private string? _activeThemeId;
    private string? _lastThemeId;
    private string _engineStateText = "主题引擎待启动";
    private int? _activePort;
    private bool _showFavorites;
    private bool _darkMode;
    private bool? _codexDarkMode;
    private bool _updatingStartupSetting;
    private bool _updatingAutomaticUpdateSetting;
    private bool _automaticUpdateChecks;
    private bool _updateCheckInProgress;
    private bool _startupInitialized;
    private bool _shutdownRequested;
    private bool _onboardingCompleted;
    private bool _uiInitialized;
    private bool _mainContentLoaded;
    private bool _editingVisualDarkMode;
    private bool _updatingVisualControls;
    private ThemeLibraryFilter _themeLibraryFilter;
    private string _themeSearchQuery = string.Empty;
    private string _updateStatusMessage = "尚未检查更新";
    private DateTimeOffset? _lastUpdateCheckAt;
    private PortableUpdateResult? _startupUpdateResult;
    private DispatcherTimer? _visualSettingsDebounce;
    private DispatcherTimer? _toastTimer;
    private RightPane _rightPane = RightPane.Themes;

    internal MainWindow(PortableLayout? layout = null)
    {
        _layout = layout ?? PortableLayout.Create();
        _stateStore = new StudioStateStore(_layout.DataDirectory);
        _preferencesStore = new UiPreferencesStore(_layout.DataDirectory);
        _launcher = new CodexPackageLauncher(_launcherDiscovery);
        _updateClient = new ReleaseUpdateClient(
            BrandInfo.RepositoryOwner,
            BrandInfo.RepositoryName,
            _layout.DataDirectory,
            Version.Parse(BrandInfo.Version));
        _runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
                    _layout.RootDirectory,
                    "Compatibility",
                    "theme-runtime-v2.js"),
            }));

        var hadSavedPreferences = _preferencesStore.Exists;
        var preferences = _preferencesStore.Load();
        _darkMode = preferences.DarkMode;
        _onboardingCompleted = preferences.OnboardingCompleted || hadSavedPreferences;
        _automaticUpdateChecks = preferences.AutomaticUpdateChecks;
        _lastUpdateCheckAt = preferences.LastUpdateCheckAt;
        _favoriteThemeIds.UnionWith(preferences.FavoriteThemeIds.Where(id => !string.IsNullOrWhiteSpace(id)));
        foreach (var (themeId, settings) in preferences.ThemeVisualSettings)
        {
            if (!string.IsNullOrWhiteSpace(themeId))
            {
                _themeVisualSettings[themeId] = (settings ?? new ThemeVisualSettings()).Normalize();
            }
        }
        _runtime.StatusChanged += Runtime_StatusChanged;
        Closed += MainWindow_Closed;
    }

    private void EnsureMainUiInitialized()
    {
        if (_uiInitialized) return;

        InitializeComponent();
        _uiInitialized = true;
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
        UpdateStartupButton();
        UpdateUpdateControls();
        UpdateVisualAdjustmentControls();
    }

    private void FitWindowToWorkArea()
    {
        const double outerMargin = 24;
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(640, workArea.Width - outerMargin);
        var availableHeight = Math.Max(360, workArea.Height - outerMargin);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
    }

    private void AdaptiveViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double designWidth = 1080;
        const double designHeight = 720;
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;

        var scale = Math.Min(1, Math.Min(e.NewSize.Width / designWidth, e.NewSize.Height / designHeight));
        if (Math.Abs(AdaptiveScale.ScaleX - scale) < 0.001) return;
        AdaptiveScale.ScaleX = scale;
        AdaptiveScale.ScaleY = scale;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
            SetEngineState("等待选择主题");
            SetStatus(codexInstalled
                ? "欢迎使用 Tessalume，请选择喜欢的主题后手动应用"
                : "可以先浏览主题；应用前请先安装 Windows 版 Codex Desktop");
            ShowStartupUpdateResult();
            ScheduleAutomaticUpdateCheck();
            return;
        }

        OpenQuickSwitchWindow();
        if (state is not null)
        {
            await TryResumeAsync(state);
        }
        else
        {
            SetEngineState("Codex 默认外观");
            SetStatus("请选择主题并手动应用到 Codex");
        }
        ShowStartupUpdateResult();
        ScheduleAutomaticUpdateCheck();
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
        _quickSwitchWindow?.Close();
        _quickSwitchWindow = null;
        _runtime.StatusChanged -= Runtime_StatusChanged;
        var visualSettingsPending = _visualSettingsDebounce?.IsEnabled == true;
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
        _updateClient.Dispose();
        if (visualSettingsPending)
        {
            await SavePreferencesAsync();
        }
        _preferencesStore.Dispose();
        await _runtime.DisposeAsync();
        _launcherDiscovery.Dispose();
        GC.SuppressFinalize(this);
    }

}
