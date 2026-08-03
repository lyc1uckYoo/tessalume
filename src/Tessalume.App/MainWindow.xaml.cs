using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;
using Microsoft.Win32;

namespace Tessalume.App;

public partial class MainWindow : Window, IAsyncDisposable
{
    private const string BuiltInTemplateFolderName = "theme-template-v1";
    private const string CreatorWorkspaceFolderName = "Tessalume-Creator";
    private const string CreatorPrompt = "请使用 $author-tessalume-theme 为《作品名》的角色名制作一套 Tessalume 主题；先完成角色研究和 11 张素材计划，等我确认后再生成、校验并交付可导入的主题文件夹。";

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
    private bool _startupInitialized;
    private bool _shutdownRequested;
    private bool _onboardingCompleted;
    private bool _uiInitialized;
    private bool _mainContentLoaded;
    private bool _editingVisualDarkMode;
    private bool _updatingVisualControls;
    private ThemeLibraryFilter _themeLibraryFilter;
    private string _themeSearchQuery = string.Empty;
    private DispatcherTimer? _visualSettingsDebounce;
    private DispatcherTimer? _toastTimer;
    private RightPane _rightPane = RightPane.Themes;

    internal MainWindow(PortableLayout? layout = null)
    {
        _layout = layout ?? PortableLayout.Create();
        _stateStore = new StudioStateStore(_layout.DataDirectory);
        _preferencesStore = new UiPreferencesStore(_layout.DataDirectory);
        _launcher = new CodexPackageLauncher(_launcherDiscovery);
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
        UpdateVisualAdjustmentControls();
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
            OpenQuickSwitchWindow();
            SetEngineState("等待选择主题");
            SetStatus(codexInstalled
                ? "欢迎使用 Tessalume，请选择喜欢的主题后手动应用"
                : "可以先浏览主题；应用前请先安装 Windows 版 Codex Desktop");
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
        if (visualSettingsPending)
        {
            await SavePreferencesAsync();
        }
        await _runtime.DisposeAsync();
        _launcherDiscovery.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ReloadThemesAsync(string? preferredId = null, bool? loadPreviews = null)
    {
        if (_uiInitialized)
        {
            StatusText.Text = "正在验证本地主题包…";
        }

        preferredId ??= _selectedTheme?.CatalogItem.Package?.Manifest.Id;
        var catalog = await new ThemeCatalog(new ThemePackageLoader()).ScanAsync(_layout.ThemesDirectory);
        _themes.Clear();
        foreach (var item in catalog)
        {
            var themeId = item.Package?.Manifest.Id;
            var theme = new ThemeCardModel(
                item,
                themeId is not null && _favoriteThemeIds.Contains(themeId),
                loadPreviews ?? _uiInitialized);
            theme.SetDarkMode(_darkMode);
            theme.IsApplied = string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase);
            _themes.Add(theme);
        }

        if (!_uiInitialized)
        {
            RefreshQuickSwitchWindow();
            return;
        }

        if (_showFavorites)
        {
            ShowFavorites(preferredId);
        }
        else
        {
            ShowThemes(preferredId);
        }
        var validCount = _themes.Count(theme => theme.IsValid);
        StatusText.Text = validCount > 0
            ? $"本地库共 {_themes.Count} 个主题，{validCount} 个可用"
            : _themes.Count == 0
                ? "本地主题库为空"
                : "主题包格式不完整，请打开诊断查看";
        UpdateLibraryMetrics();
        RefreshQuickSwitchWindow();
    }

    private void ShowThemes(string? preferredId = null)
    {
        _showFavorites = false;
        CategoryTitleText.Text = "主题画廊";
        CategoryDescriptionText.Text = "浏览本地主题，并在应用前确认完整的视觉体验。";
        ImportDeclarationTitleText.Text = "管理你的主题体验";
        ImportDeclarationBodyText.Text = "导入、收藏与实时切换都在本机完成，Codex 安装文件保持不变。";
        DeclarationIconText.Text = "✦";
        ImportButton.Content = "导入主题";
        ImportButton.Visibility = Visibility.Visible;
        UpdateCategoryButtons();
        ApplyThemeLibraryFilter(preferredId);
    }

    private void ShowFavorites(string? preferredId = null)
    {
        _showFavorites = true;
        CategoryTitleText.Text = "我的收藏";
        CategoryDescriptionText.Text = "收藏会优先进入主题浮窗，方便快速切换。";
        ImportDeclarationTitleText.Text = "把常用主题留在手边";
        ImportDeclarationBodyText.Text = "收藏仅保存在本机，可从卡片右上角随时加入或移除。";
        DeclarationIconText.Text = "♥";
        ImportButton.Visibility = Visibility.Collapsed;
        UpdateCategoryButtons();
        ApplyThemeLibraryFilter(preferredId);
    }

    private void ApplyThemeLibraryFilter(string? preferredId = null)
    {
        var source = _showFavorites
            ? _themes.Where(theme => theme.IsFavorite)
            : _themes.AsEnumerable();
        var sourceCount = source.Count();
        var query = _themeSearchQuery.Trim();

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(theme =>
                theme.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                theme.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                theme.Author.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (theme.ThemeId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        source = _themeLibraryFilter switch
        {
            ThemeLibraryFilter.Light => source.Where(theme => theme.SupportsLight),
            ThemeLibraryFilter.Dark => source.Where(theme => theme.SupportsDark),
            _ => source,
        };

        _visibleThemes.Clear();
        foreach (var theme in source)
        {
            _visibleThemes.Add(theme);
        }

        UpdateThemeFilterUi(sourceCount);
        UpdateEmptyState();

        var selectedId = preferredId ?? _selectedTheme?.ThemeId ?? _activeThemeId;
        var preferred = _visibleThemes.FirstOrDefault(theme =>
            string.Equals(theme.ThemeId, selectedId, StringComparison.OrdinalIgnoreCase) && theme.IsValid);
        var next = preferred ?? _visibleThemes.FirstOrDefault(theme => theme.IsValid);
        if (next is not null)
        {
            SelectTheme(next);
            return;
        }

        foreach (var theme in _themes) theme.IsSelected = false;
        _selectedTheme = null;
        SelectedThemeText.Text = _showFavorites ? "还没有可用的收藏主题" : "这里还没有可用主题";
        ActivateButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
    }

    private void UpdateThemeFilterUi(int sourceCount)
    {
        if (!_uiInitialized || AllThemesFilterButton is null) return;

        ThemeSearchPlaceholder.Visibility = string.IsNullOrEmpty(ThemeSearchBox.Text) &&
            !ThemeSearchBox.IsKeyboardFocused
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearThemeSearchButton.Visibility = string.IsNullOrEmpty(ThemeSearchBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        UpdateThemeFilterButton(AllThemesFilterButton, _themeLibraryFilter == ThemeLibraryFilter.All);
        UpdateThemeFilterButton(LightThemesFilterButton, _themeLibraryFilter == ThemeLibraryFilter.Light);
        UpdateThemeFilterButton(DarkThemesFilterButton, _themeLibraryFilter == ThemeLibraryFilter.Dark);

        var filtered = HasActiveThemeFilter;
        ThemeResultText.Text = filtered
            ? $"找到 {_visibleThemes.Count} / {sourceCount} 个主题"
            : $"共 {sourceCount} 个主题";
    }

    private void UpdateThemeFilterButton(Button button, bool active)
    {
        button.Background = active ? (Brush)Resources["ActiveNav"] : (Brush)Resources["Surface"];
        button.BorderBrush = active ? (Brush)Resources["Accent"] : (Brush)Resources["Border"];
        button.Foreground = active ? (Brush)Resources["Accent"] : (Brush)Resources["MutedText"];
    }

    private bool HasActiveThemeFilter =>
        !string.IsNullOrWhiteSpace(_themeSearchQuery) || _themeLibraryFilter != ThemeLibraryFilter.All;

    private void ThemeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiInitialized) return;
        _themeSearchQuery = ThemeSearchBox.Text;
        ApplyThemeLibraryFilter();
    }

    private void ThemeSearchBox_FocusChanged(object sender, RoutedEventArgs e)
    {
        ThemeSearchPlaceholder.Visibility = string.IsNullOrEmpty(ThemeSearchBox.Text) &&
            !ThemeSearchBox.IsKeyboardFocused
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ClearThemeSearch_Click(object sender, RoutedEventArgs e)
    {
        ThemeSearchBox.Clear();
        ThemeSearchBox.Focus();
    }

    private void ThemeFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filter }) return;
        _themeLibraryFilter = filter switch
        {
            "light" => ThemeLibraryFilter.Light,
            "dark" => ThemeLibraryFilter.Dark,
            _ => ThemeLibraryFilter.All,
        };
        ApplyThemeLibraryFilter();
    }

    private void UpdateEmptyState()
    {
        var isEmpty = _visibleThemes.Count == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        SelectionDock.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (HasActiveThemeFilter)
        {
            EmptyStateTitleText.Text = "没有匹配的主题";
            EmptyStateBodyText.Text = "可以更换搜索词，或清除亮暗模式筛选后再试。";
            EmptyStateActionButton.Content = "清除筛选";
            return;
        }

        EmptyStateTitleText.Text = _showFavorites ? "还没有收藏的主题" : "这里还没有主题";
        EmptyStateBodyText.Text = _showFavorites
            ? "从主题画廊收藏常用主题，它们也会优先出现在主题浮窗中。"
            : "导入一个完整主题包，即可开始预览和应用。";
        EmptyStateActionButton.Content = _showFavorites ? "浏览主题画廊" : "导入主题";
    }

    private void EmptyStateAction_Click(object sender, RoutedEventArgs e)
    {
        if (HasActiveThemeFilter)
        {
            _themeLibraryFilter = ThemeLibraryFilter.All;
            ThemeSearchBox.Clear();
            ApplyThemeLibraryFilter();
            return;
        }

        if (_showFavorites)
        {
            ShowThemes_Click(sender, e);
            return;
        }

        ImportTheme_Click(sender, e);
    }

    private async Task TryResumeAsync(StudioState state)
    {
        var theme = _themes.FirstOrDefault(item =>
            item.CatalogItem.Package?.Manifest.Id == state.ThemeId && item.IsValid);
        if (theme?.CatalogItem.Package is null)
        {
            SetEngineState("上次主题已不在本地库");
            return;
        }

        _lastThemeId = theme.CatalogItem.Package.Manifest.Id;
        SelectTheme(theme);
        if (!state.Enabled)
        {
            SetEngineState("Codex 默认外观");
            SetStatus("上次关闭时使用默认外观，已保留最后运行的主题");
            RefreshQuickSwitchWindow();
            return;
        }

        if (await ApplyThemeAsync(theme))
        {
            SetStatus($"{theme.Name} 已恢复为上次关闭时运行的主题");
        }
    }

    private ThemeCardModel[] GetQuickSwitchCandidates()
    {
        var favorites = _themes.Where(theme => theme.IsFavorite && theme.IsValid).ToArray();
        return favorites.Length > 0
            ? favorites
            : _themes.Where(theme => theme.IsValid).ToArray();
    }

    private void ShowThemes_Click(object sender, RoutedEventArgs e)
    {
        ShowThemeLibraryPage();
        ShowThemes();
    }

    private void ShowFavorites_Click(object sender, RoutedEventArgs e)
    {
        ShowThemeLibraryPage();
        ShowFavorites();
    }

    private void ThemeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ThemeCardModel theme } button)
        {
            return;
        }

        AnimateCardPress(button);
        SelectTheme(theme);
    }

    private async void FavoriteTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ThemeCardModel theme } || theme.ThemeId is not { Length: > 0 } themeId)
        {
            return;
        }

        theme.IsFavorite = !theme.IsFavorite;
        if (theme.IsFavorite)
        {
            _favoriteThemeIds.Add(themeId);
        }
        else
        {
            _favoriteThemeIds.Remove(themeId);
        }

        await SavePreferencesAsync();
        if (_showFavorites && !theme.IsFavorite)
        {
            ShowFavorites();
        }
        else
        {
            UpdateCategoryButtons();
        }

        UpdateLibraryMetrics();
        StatusText.Text = theme.IsFavorite
            ? $"{theme.Name} 已加入我的收藏"
            : $"{theme.Name} 已移出我的收藏";
        ShowToast(theme.IsFavorite ? $"已收藏 {theme.Name}" : $"已取消收藏 {theme.Name}");
        RefreshQuickSwitchWindow();
    }

    private void SelectTheme(ThemeCardModel theme)
    {
        foreach (var item in _themes) item.IsSelected = ReferenceEquals(item, theme);
        _selectedTheme = theme;
        if (!_uiInitialized) return;

        SelectedThemeText.Text = theme.Name;
        ActivateButton.IsEnabled = theme.IsValid;
        DeleteButton.IsEnabled = theme.CanDelete;
        StatusText.Text = theme.IsValid
            ? $"v{theme.Version} · 沉浸式主题 · 启用时检查本地源码"
            : string.Join("；", theme.CatalogItem.Validation.Issues.Select(issue => issue.Message));
        AnimateSelectionDock();
        UpdateVisualAdjustmentControls();
    }

    private async void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择主题文件夹（manifest.json + theme.js）",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var loader = new ThemePackageLoader();
            var result = await loader.LoadAsync(dialog.FolderName);
            if (result.Package is null)
            {
                throw new InvalidDataException(string.Join(
                    Environment.NewLine,
                    result.Validation.Issues.Select(issue => $"• {issue.Message}")));
            }

            if (!result.Package.IsAdvanced)
            {
                throw new InvalidDataException("这个文件夹不是受支持的沉浸式主题；主题必须包含 theme.js。");
            }

            var destinationDirectory = _layout.ThemesDirectory;
            var destination = Path.Combine(destinationDirectory, result.Package.Manifest.Id);
            var overwrite = false;
            if (Directory.Exists(destination) &&
                !string.Equals(Path.GetFullPath(dialog.FolderName), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                overwrite = ShowProductConfirmation(
                    "替换本地主题",
                    $"本地库中已有“{result.Package.Manifest.Name}”。是否替换为所选版本？",
                    "替换主题");
                if (!overwrite) return;
            }

            var imported = await new ThemeImporter(loader).ImportAsync(dialog.FolderName, destinationDirectory, overwrite);
            _showFavorites = false;
            await ReloadThemesAsync(imported.Manifest.Id);
            StatusText.Text = $"{imported.Manifest.Name} 已加入主题库";
            ShowToast($"{imported.Manifest.Name} 已加入主题库");
            LocalLog.Write($"Imported theme {imported.Manifest.Id}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Theme import failed.", exception);
            ShowProductMessage("无法导入主题", exception.Message, ProductDialogKind.Error);
        }
    }

    private async void RefreshThemes_Click(object sender, RoutedEventArgs e)
    {
        await ReloadThemesAsync();
        StatusText.Text = $"本地主题库已刷新，共 {_themes.Count} 个主题";
        ShowToast($"主题库已刷新，共 {_themes.Count} 个主题");
    }

    private async void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTheme is not { } theme)
        {
            return;
        }

        var package = theme.CatalogItem.Package;
        var themeId = package?.Manifest.Id;
        var themeName = theme.Name;
        var state = await _stateStore.LoadAsync();
        var isActive = state?.Enabled == true && themeId is not null &&
            string.Equals(state.ThemeId, themeId, StringComparison.OrdinalIgnoreCase);
        var message = isActive
            ? $"“{themeName}”当前正在使用。删除后将先恢复 Codex 默认外观。\n\n确定永久删除这个本地主题吗？"
            : $"确定永久删除本地主题“{themeName}”吗？\n\n此操作不会删除 Codex 数据，但主题文件夹将从本地库移除。";
        if (!ShowProductConfirmation("删除本地主题", message, "永久删除", dangerous: true))
        {
            return;
        }

        SetBusy(true, "正在删除本地主题…");
        try
        {
            EnsureDeletableThemePath(theme.CatalogItem.Directory);
            if (isActive && state is not null)
            {
                var port = _activePort ?? state.Port;
                if (port > 0 && await _launcher.IsDebugPortReadyAsync(port))
                {
                    await _runtime.RemoveAsync(port);
                }

                await _stateStore.SaveAsync(state with
                {
                    UpdatedAt = DateTimeOffset.Now,
                    Enabled = false,
                });
                SetEngineState("Codex 默认外观");
                _activeThemeId = null;
                UpdateAppliedThemeState();
            }

            Directory.Delete(theme.CatalogItem.Directory, recursive: true);
            if (theme.IsBuiltIn && themeId is not null)
            {
                BuiltInAssetInstaller.MarkDeleted(_layout, themeId);
            }
            if (themeId is not null && _favoriteThemeIds.Remove(themeId))
            {
                await SavePreferencesAsync();
            }
            _selectedTheme = null;
            await ReloadThemesAsync();
            StatusText.Text = $"{themeName} 已从本地主题库删除";
            ShowToast($"{themeName} 已从本地主题库删除", warning: true);
            LocalLog.Write($"Deleted theme {themeId ?? themeName}.");
            RefreshQuickSwitchWindow();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Theme deletion failed.", exception);
            StatusText.Text = exception.Message;
            ShowProductMessage("无法删除主题", exception.Message, ProductDialogKind.Error);
        }
        finally
        {
            SetBusy(false, null);
            IdleMemoryTrimmer.Schedule();
        }
    }

    private void ImportGuide_Click(object sender, RoutedEventArgs e) => ShowInfoPage(RightPane.ImportGuide);

    private void UsageGuide_Click(object sender, RoutedEventArgs e) => ShowInfoPage(RightPane.UsageGuide);

    private void About_Click(object sender, RoutedEventArgs e)
    {
        AboutRootText.Text = _layout.RootDirectory;
        AboutDataText.Text = _layout.DataDirectory;
        var validCount = _themes.Count(theme => theme.IsValid);
        var favoriteCount = _themes.Count(theme => theme.IsFavorite);
        AboutLibrarySummaryText.Text =
            $"本地库共 {_themes.Count} 个主题 · {validCount} 个通过校验 · {favoriteCount} 个收藏";
        ShowInfoPage(RightPane.About);
    }

    private void OpenRootDirectory_Click(object sender, RoutedEventArgs e) =>
        OpenDirectory(_layout.RootDirectory);

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e) =>
        OpenDirectory(_layout.DataDirectory);

    private void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        ShowToast("已在文件资源管理器中打开目录");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        UpdateStartupButton();
        if (_codexDarkMode is { } dark)
        {
            _editingVisualDarkMode = dark;
        }
        ShowInfoPage(RightPane.Settings);
        UpdateVisualAdjustmentControls();
        _ = RefreshCodexColorSchemeAsync();
    }

    private async void SettingsPreviousTheme_Click(object sender, RoutedEventArgs e) =>
        await ApplyRelativeSettingsThemeAsync(-1);

    private async void SettingsNextTheme_Click(object sender, RoutedEventArgs e) =>
        await ApplyRelativeSettingsThemeAsync(1);

    private async Task ApplyRelativeSettingsThemeAsync(int offset)
    {
        var candidates = GetQuickSwitchCandidates();
        if (candidates.Length == 0)
        {
            SetStatus("还没有可切换的有效主题");
            UpdateSettingsVisualHeader();
            return;
        }

        var currentId = _activeThemeId ?? GetVisualAdjustmentTheme()?.ThemeId;
        var currentIndex = Array.FindIndex(candidates, theme =>
            string.Equals(theme.ThemeId, currentId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = offset > 0 ? -1 : 0;
        }

        var nextIndex = (currentIndex + offset + candidates.Length) % candidates.Length;
        var nextTheme = candidates[nextIndex];
        SelectTheme(nextTheme);
        if (await ApplyThemeAsync(nextTheme))
        {
            UpdateVisualAdjustmentControls();
        }
    }

    private async void SettingsColorMode_Click(object sender, RoutedEventArgs e)
    {
        var dark = await ToggleCodexColorSchemeAsync();
        if (dark is null) return;

        _editingVisualDarkMode = dark.Value;
        UpdateVisualAdjustmentControls();
    }

    private void QuickSwitchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quickSwitchWindow is { IsVisible: true })
        {
            _quickSwitchWindow.Close();
            return;
        }

        OpenQuickSwitchWindow();
    }

    private void OpenQuickSwitchWindow()
    {
        try
        {
            _quickSwitchWindow = new ThemeQuickSwitchWindow(
                ApplyThemeAsync,
                ToggleRestoreThemeAsync,
                ToggleCodexColorSchemeAsync,
                ReadCodexColorSchemeAsync,
                ShowMainInterface,
                () => _usageReader.ReadAsync());
            _quickSwitchWindow.SetShellTheme(_darkMode);
            _quickSwitchWindow.Closed += (_, _) =>
            {
                _quickSwitchWindow = null;
                UpdateQuickSwitchButton();
            };
            RefreshQuickSwitchWindow();
            _quickSwitchWindow.Show();
            UpdateQuickSwitchButton();
        }
        catch (Exception exception)
        {
            _quickSwitchWindow = null;
            if (_uiInitialized)
            {
                StatusText.Text = $"无法打开主题浮窗：{exception.Message}";
            }

            ShowProductMessage("无法打开主题浮窗", exception.Message, ProductDialogKind.Error);
        }
    }

    internal async void ShowMainInterface()
    {
        EnsureMainUiInitialized();
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        _ = Dispatcher.InvokeAsync(
            () =>
            {
                Activate();
                NativeWindowActivation.TryActivate(Title);
            },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        if (_mainContentLoaded) return;

        _mainContentLoaded = true;
        try
        {
            await ReloadThemesAsync(_activeThemeId, loadPreviews: true);
            _ = RefreshCodexColorSchemeAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void RefreshQuickSwitchWindow()
    {
        if (_quickSwitchWindow is null) return;
        var isDefaultAppearance = string.IsNullOrWhiteSpace(_activeThemeId);
        var currentThemeName = isDefaultAppearance
            ? "Codex 默认外观"
            : _themes.FirstOrDefault(theme =>
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))?.Name ?? "未应用主题";
        _quickSwitchWindow.Refresh(
            _activeThemeId ?? string.Empty,
            currentThemeName,
            isDefaultAppearance,
            GetQuickSwitchCandidates());
    }

    private void StartupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var enabled = !StartupRegistration.IsEnabled();
            StartupRegistration.SetEnabled(enabled);
            UpdateStartupButton();
            StatusText.Text = enabled ? "已启用开机自动启动" : "已关闭开机自动启动";
            ShowToast(enabled ? "已启用开机自动启动" : "已关闭开机自动启动");
        }
        catch (Exception exception)
        {
            UpdateStartupButton();
            ShowProductMessage("无法更新开机启动设置", exception.Message, ProductDialogKind.Error);
        }
    }

    private void UpdateStartupButton()
    {
        if (!_uiInitialized || StartupButton is null) return;
        var enabled = StartupRegistration.IsEnabled();
        StartupButton.Tag = enabled ? "active" : "inactive";
        StartupButton.Content = enabled ? "开机启动已开启" : "开启开机启动";
        StartupButton.ToolTip = enabled ? "点击关闭登录 Windows 后自动启动" : "点击开启登录 Windows 后自动启动";
        _updatingStartupSetting = true;
        StartupCheckBox.IsChecked = enabled;
        _updatingStartupSetting = false;
    }

    private void UpdateQuickSwitchButton()
    {
        if (!_uiInitialized || QuickSwitchButton is null)
        {
            return;
        }

        var enabled = _quickSwitchWindow is { IsVisible: true };
        QuickSwitchButton.Tag = enabled ? "active" : "inactive";
        QuickSwitchButton.Content = enabled ? "浮窗已开启" : "打开主题浮窗";
        QuickSwitchButton.ToolTip = enabled ? "点击关闭主题浮窗" : "点击打开主题浮窗";
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingStartupSetting)
        {
            return;
        }

        try
        {
            var enabled = StartupCheckBox.IsChecked == true;
            StartupRegistration.SetEnabled(enabled);
            UpdateStartupButton();
            StatusText.Text = enabled ? "已启用开机自动启动" : "已关闭开机自动启动";
        }
        catch (Exception exception)
        {
            _updatingStartupSetting = true;
            StartupCheckBox.IsChecked = StartupRegistration.IsEnabled();
            _updatingStartupSetting = false;
            UpdateStartupButton();
            ShowProductMessage("无法更新开机启动设置", exception.Message, ProductDialogKind.Error);
        }
    }

    private void ShowThemeLibraryPage()
    {
        _rightPane = RightPane.Themes;
        ThemeLibraryPage.Visibility = Visibility.Visible;
        InfoPage.Visibility = Visibility.Collapsed;
        UpdateCategoryButtons();
        AnimatePage(ThemeLibraryPage);
    }

    private void ShowInfoPage(RightPane page)
    {
        _rightPane = page;
        ThemeLibraryPage.Visibility = Visibility.Collapsed;
        InfoPage.Visibility = Visibility.Visible;
        ImportInfoPanel.Visibility = page == RightPane.ImportGuide ? Visibility.Visible : Visibility.Collapsed;
        UsageInfoPanel.Visibility = page == RightPane.UsageGuide ? Visibility.Visible : Visibility.Collapsed;
        SettingsInfoPanel.Visibility = page == RightPane.Settings ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsInfoPanel.Visibility = page == RightPane.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
        AboutInfoPanel.Visibility = page == RightPane.About ? Visibility.Visible : Visibility.Collapsed;
        UpdateCategoryButtons();
        AnimatePage(InfoPage);
    }

    private void OpenTemplate_Click(object sender, RoutedEventArgs e)
    {
        var path = GetTemplatePath();
        if (!Directory.Exists(path))
        {
            ShowProductMessage("找不到模板", "模板文件尚未释放，请重启应用后再试。", ProductDialogKind.Error);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private async void PrepareCreatorWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 Codex 主题创作者工作区的保存位置",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var destination = GetAvailableCreatorWorkspacePath(dialog.FolderName);
            BuiltInAssetInstaller.CreateCreatorWorkspace(destination);
            var promptCopied = await TryCopyCreatorPromptAsync(showSuccessToast: false);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{destination}\"") { UseShellExecute = true });
            ShowProductMessage(
                "Codex 创作工作区已准备",
                $"工作区已经创建并打开：\n{destination}\n\n请在 Codex 中打开整个文件夹，然后发送一句角色主题需求。" +
                (promptCopied ? "\n\n示例创作指令已复制到剪贴板。" : string.Empty),
                ProductDialogKind.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowProductMessage("无法创建创作工作区", exception.Message, ProductDialogKind.Error);
        }
    }

    private async void CopyCreatorPrompt_Click(object sender, RoutedEventArgs e) =>
        await TryCopyCreatorPromptAsync(showSuccessToast: true);

    private async Task<bool> TryCopyCreatorPromptAsync(bool showSuccessToast)
    {
        if (!await TrySetClipboardTextAsync(CreatorPrompt, "无法复制创作指令"))
        {
            return false;
        }

        if (showSuccessToast)
        {
            ShowToast("一句话创作指令已复制");
        }
        return true;
    }

    private static string GetAvailableCreatorWorkspacePath(string parentDirectory)
    {
        var first = Path.Combine(parentDirectory, CreatorWorkspaceFolderName);
        if (!Directory.Exists(first) && !File.Exists(first)) return first;

        for (var suffix = 2; suffix <= 99; suffix++)
        {
            var candidate = Path.Combine(parentDirectory, $"{CreatorWorkspaceFolderName}-{suffix}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }

        throw new IOException("所选位置已有过多 Tessalume-Creator 工作区，请换一个文件夹后重试。");
    }

    private void CopyTemplate_Click(object sender, RoutedEventArgs e)
    {
        var source = GetTemplatePath();
        if (!Directory.Exists(source))
        {
            ShowProductMessage("找不到模板", "模板文件尚未释放，请重启应用后再试。", ProductDialogKind.Error);
            return;
        }

        var dialog = new OpenFolderDialog { Title = "选择新主题的保存位置", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;

        const string name = "my-character-theme";
        var destination = Path.Combine(dialog.FolderName, name);
        if (Directory.Exists(destination))
        {
            ShowProductMessage("无法复制模板", $"目标位置已存在文件夹：\n{destination}", ProductDialogKind.Warning);
            return;
        }

        CopyDirectory(source, destination);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{destination}\"") { UseShellExecute = true });
        ShowToast("主题模板已复制并打开");
    }

    private string GetTemplatePath() => Path.Combine(
        _layout.RootDirectory,
        "Templates",
        BuiltInTemplateFolderName);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }

    private void EnsureDeletableThemePath(string themeDirectory)
    {
        var library = Path.GetFullPath(_layout.ThemesDirectory);
        var target = Path.GetFullPath(themeDirectory);
        var relative = Path.GetRelativePath(library, target);
        if (relative is "." or ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("主题路径不在本地主题库内，已拒绝删除。");
        }

        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("符号链接或重解析目录不能通过 Studio 删除。");
        }
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDiagnosticsAsync();
        ShowInfoPage(RightPane.Diagnostics);
    }

    private async Task RefreshDiagnosticsAsync()
    {
        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port;
        var portReady = port is not null && await _launcher.IsDebugPortReadyAsync(port.Value);
        var validThemes = _themes.Count(theme => theme.IsValid);
        var activeTheme = state?.Enabled == true
            ? _themes.FirstOrDefault(theme => theme.CatalogItem.Package?.Manifest.Id == state.ThemeId)?.Name
            : null;
        var codexRunning = CodexPackageLauncher.IsCodexRunning();
        DiagnosticCodexText.Text = codexRunning ? "正在运行" : "未运行";
        DiagnosticPortText.Text = port is null ? "未分配" : portReady ? $"{port} · 正常" : $"{port} · 未连接";
        DiagnosticThemesText.Text = _themes.Count - validThemes == 0
            ? $"{validThemes} 个有效"
            : $"{validThemes} 有效 / {_themes.Count - validThemes} 异常";
        DiagnosticCodexText.SetResourceReference(TextBlock.ForegroundProperty, codexRunning ? "Positive" : "MutedText");
        DiagnosticPortText.SetResourceReference(TextBlock.ForegroundProperty, portReady ? "Positive" : port is null ? "MutedText" : "Amber");
        DiagnosticThemesText.SetResourceReference(TextBlock.ForegroundProperty, _themes.Count == validThemes ? "Positive" : "Amber");
        DiagnosticRootText.Text = _layout.RootDirectory;
        DiagnosticLibraryText.Text = _layout.ThemesDirectory;
        DiagnosticProcessText.Text = codexRunning ? "已发现 · 正在运行" : "未发现";
        DiagnosticLoopbackText.Text = portReady ? $"127.0.0.1:{port} · 可用" : "当前不可用";
        DiagnosticThemeStateText.Text = state?.Enabled == true ? "沉浸式主题已启用" : "Codex 默认外观";
        DiagnosticCurrentThemeText.Text = $"当前主题：{activeTheme ?? "无"}";
        DiagnosticValidationText.Text = $"{validThemes} 个通过 · {_themes.Count - validThemes} 个异常";
        var invalidThemes = _themes.Count - validThemes;
        if (codexRunning && portReady && invalidThemes == 0)
        {
            DiagnosticHealthTitleText.Text = "运行状态良好";
            DiagnosticHealthBodyText.Text = "Codex、本机运行时与全部主题包均处于可用状态。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Positive"];
        }
        else if (!codexRunning)
        {
            DiagnosticHealthTitleText.Text = "Codex 尚未运行";
            DiagnosticHealthBodyText.Text = "应用任意主题时，软件会自动启动并建立本地连接。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Amber"];
        }
        else if (!portReady)
        {
            DiagnosticHealthTitleText.Text = "本机运行时需要重新连接";
            DiagnosticHealthBodyText.Text = "Codex 正在运行，但当前回环端口不可用；重新应用主题即可修复。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Amber"];
        }
        else
        {
            DiagnosticHealthTitleText.Text = "发现需要处理的主题包";
            DiagnosticHealthBodyText.Text = $"{invalidThemes} 个主题未通过本地校验，请检查对应主题源码。";
            DiagnosticHealthDot.Fill = (Brush)Resources["Danger"];
        }
        DiagnosticUpdatedText.Text = $"刚刚更新 · {DateTime.Now:HH:mm:ss}";
    }

    private async void CopyDiagnosticReport_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDiagnosticsAsync();
        var report = new StringBuilder()
            .AppendLine("Tessalume 诊断报告")
            .AppendLine(CultureInfo.InvariantCulture, $"生成时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine(CultureInfo.InvariantCulture, $"软件版本：{BrandInfo.VersionLabel}")
            .AppendLine(CultureInfo.InvariantCulture, $"Windows：{Environment.OSVersion}")
            .AppendLine(CultureInfo.InvariantCulture, $"进程架构：{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}")
            .AppendLine(CultureInfo.InvariantCulture, $"Codex：{DiagnosticCodexText.Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"本机端口：{DiagnosticPortText.Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"主题包：{DiagnosticThemesText.Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"主题状态：{DiagnosticThemeStateText.Text}")
            .AppendLine(DiagnosticCurrentThemeText.Text)
            .AppendLine(CultureInfo.InvariantCulture, $"应用目录：{_layout.RootDirectory}")
            .AppendLine(CultureInfo.InvariantCulture, $"主题目录：{_layout.ThemesDirectory}")
            .AppendLine(CultureInfo.InvariantCulture, $"日志目录：{LocalLog.LogDirectory}")
            .AppendLine()
            .AppendLine("最近日志：");
        foreach (var line in LocalLog.ReadTail())
        {
            report.AppendLine(line);
        }

        if (await TrySetClipboardTextAsync(report.ToString(), "无法复制诊断报告"))
        {
            LocalLog.Write("Diagnostic report copied.");
            ShowToast("诊断报告已复制，可直接粘贴到问题反馈中");
        }
    }

    private async Task<bool> TrySetClipboardTextAsync(string text, string errorTitle)
    {
        System.Runtime.InteropServices.ExternalException? lastException = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException exception)
            {
                lastException = exception;
                if (attempt < 5)
                {
                    await Task.Delay(80);
                }
            }
        }

        LocalLog.Write($"{errorTitle}: the Windows clipboard remained unavailable.", lastException);
        ShowProductMessage(errorTitle, lastException?.Message ?? "Windows 剪贴板暂时不可用，请稍后重试。", ProductDialogKind.Error);
        return false;
    }

    private async void RestoreBuiltInThemes_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var restoredCount = BuiltInAssetInstaller.RestoreDeletedThemes(_layout);
            if (restoredCount == 0)
            {
                ShowProductMessage("无需恢复", "当前没有被删除的内置主题。", ProductDialogKind.Information);
                return;
            }

            await ReloadThemesAsync();
            LocalLog.Write($"Restored {restoredCount} deleted built-in theme(s).");
            ShowToast($"已恢复 {restoredCount} 个内置主题");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Restoring built-in themes failed.", exception);
            ShowProductMessage("无法恢复内置主题", exception.Message, ProductDialogKind.Error);
        }
    }

    private void OpenLogDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LocalLog.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LocalLog.LogDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowProductMessage("无法打开日志目录", exception.Message, ProductDialogKind.Error);
        }
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTheme is not null)
        {
            await ApplyThemeAsync(_selectedTheme);
        }
    }

    private async Task<bool> ApplyThemeAsync(ThemeCardModel theme)
    {
        if (theme.CatalogItem.Package is not { } package) return false;
        SetBusy(true, "正在连接本机 Codex…");
        try
        {
            var state = await _stateStore.LoadAsync();
            var port = state?.Port ?? 0;
            if (port <= 0 || !await _launcher.IsDebugPortReadyAsync(port))
            {
                port = await _launcher.FindRunningDebugPortAsync() ?? 0;
            }

            if (port <= 0)
            {
                if (CodexPackageLauncher.IsCodexRunning())
                {
                    var confirmed = ShowProductConfirmation(
                        "需要重新启动 Codex",
                        "Codex 当前没有可用的主题连接。为了应用所选主题，需要关闭并重新启动 Codex。\n\n请先保存正在编辑的内容并确认当前任务可以中断。",
                        "已保存，重新启动");
                    if (!confirmed)
                    {
                        SetStatus("已取消应用，Codex 保持当前状态");
                        return false;
                    }

                    SetStatus("正在关闭并重新启动 Codex…");
                    await CodexPackageLauncher.CloseCodexAsync();
                }

                port = CodexPackageLauncher.FindFreePort();
                SetStatus($"正在本机端口 {port} 启动 Codex…");
                await _launcher.LaunchAndWaitAsync(port);
            }

            SetStatus("正在应用本地主题…");
            await _runtime.StartAsync(port, package, GetVisualSettings(package.Manifest.Id));
            await LegacyInjectorMigrator.TryStopAsync();
            await _stateStore.SaveAsync(new StudioState
            {
                Port = port,
                ThemeId = package.Manifest.Id,
                UpdatedAt = DateTimeOffset.Now,
                Enabled = true,
            });
            _activePort = port;
            _activeThemeId = package.Manifest.Id;
            _lastThemeId = _activeThemeId;
            UpdateAppliedThemeState();
            SetEngineState($"运行中 · 本机 {port}");
            SetStatus($"{package.Manifest.Name} 已应用，可继续实时切换");
            LocalLog.Write($"Applied theme {package.Manifest.Id} on port {port}.");
            RefreshQuickSwitchWindow();
            UpdateVisualAdjustmentControls();
            return true;
        }
        catch (Exception exception)
        {
            LocalLog.Write($"Applying theme {package.Manifest.Id} failed.", exception);
            SetEngineState("启动失败");
            SetStatus(exception.Message);
            ShowProductMessage("无法应用主题", exception.Message, ProductDialogKind.Error);
            return false;
        }
        finally
        {
            SetBusy(false, null);
            IdleMemoryTrimmer.Schedule();
        }
    }

    private async void RestoreTheme_Click(object sender, RoutedEventArgs e)
    {
        await RestoreDefaultAsync();
    }

    private async Task<bool> ToggleRestoreThemeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_activeThemeId))
        {
            return await RestoreDefaultAsync();
        }

        var lastTheme = _themes.FirstOrDefault(theme =>
            theme.IsValid && string.Equals(theme.ThemeId, _lastThemeId, StringComparison.OrdinalIgnoreCase));
        if (lastTheme is null)
        {
            SetStatus("没有可恢复的上一主题");
            return false;
        }

        return await ApplyThemeAsync(lastTheme);
    }

    private async Task<bool> RestoreDefaultAsync()
    {
        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port;
        if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
        {
            SetStatus("当前没有活动的本地主题");
            return false;
        }

        SetBusy(true, "正在恢复 Codex 默认外观…");
        try
        {
            await LegacyInjectorMigrator.TryStopAsync();
            await _runtime.RemoveAsync(port.Value);
            await _stateStore.SaveAsync(new StudioState
            {
                Port = port.Value,
                ThemeId = _selectedTheme?.CatalogItem.Package?.Manifest.Id ?? state?.ThemeId ?? string.Empty,
                UpdatedAt = DateTimeOffset.Now,
                Enabled = false,
            });
            SetEngineState("Codex 默认外观");
            if (!string.IsNullOrWhiteSpace(_activeThemeId))
            {
                _lastThemeId = _activeThemeId;
            }
            _activeThemeId = null;
            UpdateAppliedThemeState();
            SetStatus("本地主题已移除，Codex 安装文件未被修改");
            RefreshQuickSwitchWindow();
            UpdateVisualAdjustmentControls();
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            ShowProductMessage("恢复失败", exception.Message, ProductDialogKind.Error);
            return false;
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void StudioMode_Click(object sender, RoutedEventArgs e)
    {
        _darkMode = string.Equals((sender as Button)?.Tag?.ToString(), "dark", StringComparison.OrdinalIgnoreCase);
        ApplyStudioTheme(_darkMode);
        await SavePreferencesAsync();
    }

    private async void CodexMode_Click(object sender, RoutedEventArgs e)
    {
        await ToggleCodexColorSchemeAsync();
    }

    private async Task<bool?> ToggleCodexColorSchemeAsync()
    {
        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port ?? await _launcher.FindRunningDebugPortAsync();
        if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
        {
            SetStatus($"请先用 {BrandInfo.ProductName} 启动 Codex");
            return null;
        }

        SetBusy(true, "正在切换 Codex 明暗色…");
        try
        {
            _activePort = port.Value;
            var dark = await _runtime.ToggleColorSchemeAsync(port.Value);
            _codexDarkMode = dark;
            if (_rightPane == RightPane.Settings)
            {
                _editingVisualDarkMode = dark;
            }
            UpdateCodexModeButton();
            if (_rightPane == RightPane.Settings)
            {
                UpdateVisualAdjustmentControls();
            }

            SetStatus(dark ? "Codex 已切换为暗色" : "Codex 已切换为亮色");
            return dark;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            ShowProductMessage("无法切换 Codex 明暗色", exception.Message, ProductDialogKind.Error);
            return null;
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async Task<bool?> ReadCodexColorSchemeAsync()
    {
        try
        {
            var state = await _stateStore.LoadAsync();
            var port = _activePort ?? state?.Port ?? await _launcher.FindRunningDebugPortAsync();
            if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
            {
                return null;
            }

            _activePort = port.Value;
            var dark = await _runtime.ReadColorSchemeAsync(port.Value);
            _codexDarkMode = dark;
            if (_rightPane == RightPane.Settings)
            {
                _editingVisualDarkMode = dark;
            }
            UpdateCodexModeButton();
            if (_rightPane == RightPane.Settings)
            {
                UpdateVisualAdjustmentControls();
            }
            return dark;
        }
        catch
        {
            return null;
        }
    }

    private async Task RefreshCodexColorSchemeAsync()
    {
        var dark = await ReadCodexColorSchemeAsync();
        if (dark is null)
        {
            return;
        }

        _codexDarkMode = dark;
        UpdateCodexModeButton();
    }

    private void ApplyStudioTheme(bool dark)
    {
        SetGradientBrush(
            "WindowBackground",
            dark ? "#0D1017" : "#F6F8FC",
            dark ? "#111620" : "#F0F3F8");
        SetGradientBrush(
            "SidebarBackground",
            dark ? "#12161F" : "#FCFDFE",
            dark ? "#171C26" : "#F7F9FC");
        SetGradientBrush(
            "PrimaryGradient",
            dark ? "#7480F4" : "#5968EA",
            dark ? "#A16BE7" : "#8A5FDF");
        SetGradientBrush(
            "PrimaryActionGradient",
            dark ? "#736BFA" : "#615FE8",
            dark ? "#A45AD9" : "#8C58D5");
        SetGradientBrush(
            "AdvancedPreview",
            dark ? "#17212C" : "#E8EDF3",
            dark ? "#33475C" : "#BECBD8");
        SetGradientBrush(
            "SettingsControlBar",
            dark ? "#211827" : "#F0F2F8",
            dark ? "#35243E" : "#E7EAF4");
        SetGradientBrush(
            "SettingsCurrentThemeGradient",
            dark ? "#3A2B45" : "#FFFFFF",
            dark ? "#2A233A" : "#E9E7F8");
        SetBrush("Surface", dark ? "#1C222C" : "#FFFFFF");
        SetBrush("SurfaceAlt", dark ? "#252C37" : "#F2F4F8");
        SetBrush("SurfaceElevated", dark ? "#202732" : "#FFFFFF");
        SetBrush("HoverSurface", dark ? "#2C3441" : "#E9ECF3");
        SetBrush("InfoSurface", dark ? "#292D46" : "#F0EFFF");
        SetBrush("InfoBorder", dark ? "#464C71" : "#DAD8F7");
        SetBrush("PrimaryText", dark ? "#EFF2F8" : "#171927");
        SetBrush("MutedText", dark ? "#ADB6C6" : "#62697A");
        SetBrush("SubtleText", dark ? "#858FA1" : "#9299AA");
        SetBrush("Border", dark ? "#353E4E" : "#DDE2EC");
        SetBrush("Accent", dark ? "#978BFF" : "#675CF0");
        SetBrush("AccentSoft", dark ? "#332F58" : "#EFEDFF");
        SetBrush("ActiveNav", dark ? "#302D50" : "#EFEEFF");
        SetBrush("Positive", dark ? "#55D6A6" : "#24B987");
        SetBrush("Danger", dark ? "#FF829E" : "#D94C70");
        SetBrush("DangerSoft", dark ? "#38232B" : "#FFF0F4");
        SetBrush("Sky", dark ? "#8EAAFF" : "#4D7FE8");
        SetBrush("SkySoft", dark ? "#293752" : "#EEF4FF");
        SetBrush("Amber", dark ? "#F1B85B" : "#D88A24");
        SetBrush("AmberSoft", dark ? "#3A3020" : "#FFF5E7");
        SetBrush("Rose", dark ? "#F58CB6" : "#D9598C");
        SetBrush("RoseSoft", dark ? "#412737" : "#FFF0F7");
        SetBrush("Teal", dark ? "#55D4D1" : "#159A9C");
        SetBrush("TealSoft", dark ? "#203B3D" : "#EAF9F7");
        SetBrush("SettingsBarBorder", dark ? "#61815B8C" : "#BAC4D8");
        SetBrush("SettingsBarPrimaryText", dark ? "#FFF7FA" : "#25293B");
        SetBrush("SettingsBarMutedText", dark ? "#B8DFD4E5" : "#697087");
        SetBrush("SettingsControlSurface", dark ? "#18FFFFFF" : "#F9FBFE");
        SetBrush("SettingsControlBorder", dark ? "#32FFFFFF" : "#C7CDDE");
        SetBrush("SettingsControlHover", dark ? "#29FFFFFF" : "#FFFFFF");
        SetBrush("SettingsTrack", dark ? "#46505D" : "#D9DDE8");
        Resources["SettingsBarShadow"] = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = (Color)ColorConverter.ConvertFromString(dark ? "#120B18" : "#59637A"),
            BlurRadius = dark ? 28 : 24,
            ShadowDepth = dark ? 8 : 7,
            Opacity = dark ? 0.4 : 0.18,
        };
        if (SettingsThemeControlBar is not null)
        {
            SettingsThemeControlBar.Effect = (System.Windows.Media.Effects.Effect)Resources["SettingsBarShadow"];
        }
        foreach (var theme in _themes)
        {
            theme.SetDarkMode(dark);
        }
        _quickSwitchWindow?.SetShellTheme(dark);
        SetEngineState(_engineStateText);
        NativeTitleBar.Apply(this, dark);
        UpdateCategoryButtons();
        UpdateModeButtons();
        UpdateStartupButton();
        UpdateQuickSwitchButton();
        UpdateCodexModeButton();
        if (_uiInitialized && AllThemesFilterButton is not null)
        {
            UpdateThemeFilterUi(_showFavorites
                ? _themes.Count(theme => theme.IsFavorite)
                : _themes.Count);
        }
    }

    private void SetBrush(string key, string color) =>
        Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private void SetGradientBrush(string key, string startColor, string endColor) =>
        Resources[key] = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(startColor),
            (Color)ColorConverter.ConvertFromString(endColor),
            new Point(0, 0),
            new Point(1, 1));

    private void UpdateAppliedThemeState()
    {
        foreach (var theme in _themes)
        {
            theme.IsApplied = !string.IsNullOrWhiteSpace(_activeThemeId) &&
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateCategoryButtons()
    {
        if (ThemesButton is null || FavoritesButton is null) return;
        var themesActive = _rightPane == RightPane.Themes && !_showFavorites;
        ThemesButton.Background = themesActive ? (Brush)Resources["ActiveNav"] : Brushes.Transparent;
        FavoritesButton.Background = _rightPane == RightPane.Themes && _showFavorites ? (Brush)Resources["ActiveNav"] : Brushes.Transparent;
        ThemesButton.Foreground = (Brush)Resources[themesActive ? "Accent" : "MutedText"];
        FavoritesButton.Foreground = (Brush)Resources[_rightPane == RightPane.Themes && _showFavorites ? "Accent" : "MutedText"];
        ThemesButton.Tag = themesActive ? "active" : "inactive";
        FavoritesButton.Tag = _rightPane == RightPane.Themes && _showFavorites ? "active" : "inactive";
        ThemesButton.FontWeight = themesActive ? FontWeights.SemiBold : FontWeights.Normal;
        FavoritesButton.FontWeight = _rightPane == RightPane.Themes && _showFavorites ? FontWeights.SemiBold : FontWeights.Normal;
        FavoritesLabelText.Text = _favoriteThemeIds.Count == 0
            ? "我的收藏"
            : $"我的收藏  {_favoriteThemeIds.Count}";
        UpdateInfoNavigationButton(DiagnosticsButton, _rightPane == RightPane.Diagnostics);
        UpdateInfoNavigationButton(SettingsButton, _rightPane == RightPane.Settings);
        UpdateInfoNavigationButton(ImportGuideButton, _rightPane == RightPane.ImportGuide);
        UpdateInfoNavigationButton(UsageGuideButton, _rightPane == RightPane.UsageGuide);
        UpdateInfoNavigationButton(AboutButton, _rightPane == RightPane.About);
    }

    private void UpdateLibraryMetrics()
    {
        if (!_uiInitialized || ThemeCountText is null || FavoriteCountText is null)
        {
            return;
        }

        ThemeCountText.Text = _themes.Count.ToString(CultureInfo.InvariantCulture);
        FavoriteCountText.Text = _themes.Count(theme => theme.IsFavorite).ToString(CultureInfo.InvariantCulture);
    }

    private static void AnimateCardPress(Button button)
    {
        if (button.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var easing = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(230)) { EasingFunction = easing });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(230)) { EasingFunction = easing });
    }

    private void RoundedCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || border.ActualWidth <= 0 || border.ActualHeight <= 0)
        {
            return;
        }

        border.Clip = new RectangleGeometry(
            new Rect(0, 0, border.ActualWidth, border.ActualHeight),
            16,
            16);
    }

    private void AnimateSelectionDock()
    {
        if (!_uiInitialized || SelectionDockScale is null)
        {
            return;
        }

        var easing = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };
        SelectionDockScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        SelectionDockScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
    }

    private static void AnimatePage(FrameworkElement page)
    {
        page.Opacity = 0;
        if (page.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            page.RenderTransform = translate;
        }

        translate.Y = 10;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = easing });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = easing });
    }

    private void UpdateInfoNavigationButton(Button button, bool active)
    {
        button.Background = active ? (Brush)Resources["ActiveNav"] : Brushes.Transparent;
        button.Foreground = (Brush)Resources[active ? "Accent" : "MutedText"];
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        button.Tag = active ? "active" : "inactive";
    }

    private Task SavePreferencesAsync() => _preferencesStore.SaveAsync(new UiPreferences
    {
        DarkMode = _darkMode,
        OnboardingCompleted = _onboardingCompleted,
        FavoriteThemeIds = _favoriteThemeIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
        ThemeVisualSettings = _themeVisualSettings.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Normalize(),
            StringComparer.OrdinalIgnoreCase),
    });

    private ThemeVisualSettings GetVisualSettings(string themeId)
    {
        if (_themeVisualSettings.TryGetValue(themeId, out var settings))
        {
            return settings.Normalize();
        }

        settings = new ThemeVisualSettings();
        _themeVisualSettings[themeId] = settings;
        return settings;
    }

    private ThemeCardModel? GetVisualAdjustmentTheme()
    {
        if (!string.IsNullOrWhiteSpace(_activeThemeId))
        {
            var active = _themes.FirstOrDefault(theme =>
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase));
            if (active is not null) return active;
        }

        return _selectedTheme;
    }

    private void VisualAdjustmentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVisualControls || sender is not Slider { Tag: string tag }) return;
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        var parts = tag.Split('.', 2);
        if (parts.Length != 2) return;

        var settings = GetVisualSettings(themeId);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        var adjustment = parts[0] switch
        {
            "hero" => mode.Hero,
            "sidebar" => mode.Sidebar,
            "chat" => mode.Chat,
            _ => null,
        };
        if (adjustment is null) return;

        adjustment = parts[1] switch
        {
            "brightness" => adjustment with { Brightness = e.NewValue },
            "contrast" => adjustment with { Contrast = e.NewValue },
            "saturation" => adjustment with { Saturation = e.NewValue },
            "opacity" => adjustment with { Opacity = e.NewValue },
            _ => adjustment,
        };
        mode = parts[0] switch
        {
            "hero" => mode with { Hero = adjustment },
            "sidebar" => mode with { Sidebar = adjustment },
            "chat" => mode with { Chat = adjustment },
            _ => mode,
        };
        _themeVisualSettings[themeId] = (_editingVisualDarkMode
            ? settings with { Dark = mode }
            : settings with { Light = mode }).Normalize();
        UpdateVisualAdjustmentLabels();
        ScheduleVisualSettingsUpdate();
    }

    private void ResetVisualRegion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string region }) return;
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        var settings = GetVisualSettings(themeId);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        mode = region switch
        {
            "hero" => mode with { Hero = new ThemeArtworkAdjustment() },
            "sidebar" => mode with { Sidebar = new ThemeArtworkAdjustment() },
            "chat" => mode with { Chat = new ThemeArtworkAdjustment() },
            _ => mode,
        };
        _themeVisualSettings[themeId] = _editingVisualDarkMode
            ? settings with { Dark = mode }
            : settings with { Light = mode };
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
    }

    private void ResetAllVisualSettings_Click(object sender, RoutedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        _themeVisualSettings[themeId] = new ThemeVisualSettings();
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
    }

    private void ScheduleVisualSettingsUpdate()
    {
        if (_visualSettingsDebounce is null) return;
        _visualSettingsDebounce.Stop();
        _visualSettingsDebounce.Start();
    }

    private async void VisualSettingsDebounce_Tick(object? sender, EventArgs e)
    {
        _visualSettingsDebounce?.Stop();
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        try
        {
            await SavePreferencesAsync();
            if (!string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"{theme.Name} 的图像参数已保存，应用主题后生效");
                return;
            }

            var state = await _stateStore.LoadAsync();
            var port = _activePort ?? state?.Port;
            if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
            {
                SetStatus("图像参数已保存；Codex 下次连接时自动生效");
                return;
            }

            await _runtime.ApplyVisualSettingsAsync(port.Value, themeId, GetVisualSettings(themeId));
            SetStatus($"已实时更新 {theme.Name} 的图像参数");
        }
        catch (Exception exception)
        {
            SetStatus($"图像参数已保留，但实时更新失败：{exception.Message}");
        }
    }

    private void UpdateVisualAdjustmentControls()
    {
        if (!_uiInitialized || VisualAdjustmentEditor is null) return;
        var theme = GetVisualAdjustmentTheme();
        var available = theme?.ThemeId is { Length: > 0 };
        VisualAdjustmentEditor.IsEnabled = available;
        var isApplied = available && string.Equals(
            theme!.ThemeId,
            _activeThemeId,
            StringComparison.OrdinalIgnoreCase);
        VisualThemeNameText.Text = available
            ? isApplied
                ? $"{theme!.Name} · 当前修改会立即显示在 Codex 中"
                : $"{theme!.Name} · 参数会保存并在应用主题时生效"
            : "请先在主题画廊中选择一个有效主题";
        VisualEditingModeText.Text = _codexDarkMode is null
            ? $"{(_editingVisualDarkMode ? "暗色" : "亮色")}参数 · 待检测"
            : _editingVisualDarkMode ? "暗色参数" : "亮色参数";
        VisualEditingModeBadge.Background = (Brush)Resources[_editingVisualDarkMode ? "AccentSoft" : "SkySoft"];
        VisualEditingModeBadge.BorderBrush = (Brush)Resources[_editingVisualDarkMode ? "Accent" : "Sky"];
        VisualEditingModeText.Foreground = (Brush)Resources[_editingVisualDarkMode ? "Accent" : "Sky"];
        UpdateSettingsVisualHeader();
        if (!available) return;

        var settings = GetVisualSettings(theme!.ThemeId!);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        _updatingVisualControls = true;
        try
        {
            SetAdjustmentControls(mode.Hero, HeroBrightnessSlider, HeroContrastSlider, HeroSaturationSlider, HeroOpacitySlider);
            SetAdjustmentControls(mode.Sidebar, SidebarBrightnessSlider, SidebarContrastSlider, SidebarSaturationSlider, SidebarOpacitySlider);
            SetAdjustmentControls(mode.Chat, ChatBrightnessSlider, ChatContrastSlider, ChatSaturationSlider, ChatOpacitySlider);
            UpdateVisualAdjustmentLabels();
        }
        finally
        {
            _updatingVisualControls = false;
        }
    }

    private static void SetAdjustmentControls(
        ThemeArtworkAdjustment adjustment,
        Slider brightness,
        Slider contrast,
        Slider saturation,
        Slider opacity)
    {
        brightness.Value = adjustment.Brightness;
        contrast.Value = adjustment.Contrast;
        saturation.Value = adjustment.Saturation;
        opacity.Value = adjustment.Opacity;
    }

    private void UpdateVisualAdjustmentLabels()
    {
        if (!_uiInitialized) return;
        HeroBrightnessValue.Text = $"{HeroBrightnessSlider.Value:0}%";
        HeroContrastValue.Text = $"{HeroContrastSlider.Value:0}%";
        HeroSaturationValue.Text = $"{HeroSaturationSlider.Value:0}%";
        HeroOpacityValue.Text = $"{HeroOpacitySlider.Value:0}%";
        SidebarBrightnessValue.Text = $"{SidebarBrightnessSlider.Value:0}%";
        SidebarContrastValue.Text = $"{SidebarContrastSlider.Value:0}%";
        SidebarSaturationValue.Text = $"{SidebarSaturationSlider.Value:0}%";
        SidebarOpacityValue.Text = $"{SidebarOpacitySlider.Value:0}%";
        ChatBrightnessValue.Text = $"{ChatBrightnessSlider.Value:0}%";
        ChatContrastValue.Text = $"{ChatContrastSlider.Value:0}%";
        ChatSaturationValue.Text = $"{ChatSaturationSlider.Value:0}%";
        ChatOpacityValue.Text = $"{ChatOpacitySlider.Value:0}%";
    }

    private void UpdateSettingsVisualHeader()
    {
        if (!_uiInitialized || SettingsThemeControlBar is null) return;

        var candidates = GetQuickSwitchCandidates();
        var adjustmentTheme = GetVisualAdjustmentTheme();
        var activeTheme = string.IsNullOrWhiteSpace(_activeThemeId)
            ? null
            : _themes.FirstOrDefault(theme =>
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase));
        var positionTheme = activeTheme ?? adjustmentTheme;
        var position = positionTheme is null
            ? -1
            : Array.FindIndex(candidates, theme =>
                string.Equals(theme.ThemeId, positionTheme.ThemeId, StringComparison.OrdinalIgnoreCase));

        SettingsCurrentThemeNameText.Text = activeTheme?.Name ?? "Codex 默认外观";
        SettingsThemeStateText.Text = activeTheme is not null
            ? "已应用 · 下方调节实时生效"
            : adjustmentTheme is not null
                ? $"默认外观 · 待应用 {adjustmentTheme.Name}"
                : "还没有可用主题";
        SettingsThemePositionText.Text = position >= 0
            ? $"{position + 1:00} / {candidates.Length:00}"
            : $"— / {candidates.Length:00}";
        SettingsLiveDot.Fill = (Brush)Resources[activeTheme is not null
            ? "Positive"
            : adjustmentTheme is not null ? "Amber" : "SubtleText"];
        SettingsPreviousThemeButton.IsEnabled = candidates.Length > 0;
        SettingsNextThemeButton.IsEnabled = candidates.Length > 0;

        SettingsModeMoonIcon.Visibility = _codexDarkMode is true ? Visibility.Visible : Visibility.Collapsed;
        SettingsModeSunIcon.Visibility = _codexDarkMode is false ? Visibility.Visible : Visibility.Collapsed;
        SettingsModeUnknownText.Visibility = _codexDarkMode is null ? Visibility.Visible : Visibility.Collapsed;
        if (_codexDarkMode is true)
        {
            SettingsColorModeText.Text = "Codex 当前暗色";
            SettingsColorModeHintText.Text = "点击切换到亮色";
            SettingsColorModeButton.Background = (Brush)Resources["AccentSoft"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["Accent"];
            SettingsColorModeButton.ToolTip = "Codex 当前为暗色，点击切换到亮色";
        }
        else if (_codexDarkMode is false)
        {
            SettingsColorModeText.Text = "Codex 当前亮色";
            SettingsColorModeHintText.Text = "点击切换到暗色";
            SettingsColorModeButton.Background = (Brush)Resources["SkySoft"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["Sky"];
            SettingsColorModeButton.ToolTip = "Codex 当前为亮色，点击切换到暗色";
        }
        else
        {
            SettingsColorModeText.Text = "检测显示模式";
            SettingsColorModeHintText.Text = "点击连接并切换";
            SettingsColorModeButton.Background = (Brush)Resources["SettingsControlSurface"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["SettingsControlBorder"];
            SettingsColorModeButton.ToolTip = "连接 Codex 后读取并切换亮暗模式";
        }
    }

    private void UpdateModeButtons()
    {
        if (LightModeButton is null || DarkModeButton is null) return;
        LightModeButton.Background = _darkMode ? Brushes.Transparent : (Brush)Resources["SkySoft"];
        LightModeButton.Foreground = (Brush)Resources[_darkMode ? "MutedText" : "Sky"];
        LightModeButton.BorderBrush = _darkMode ? Brushes.Transparent : (Brush)Resources["Sky"];
        LightModeButton.FontWeight = _darkMode ? FontWeights.Normal : FontWeights.SemiBold;
        DarkModeButton.Background = _darkMode ? (Brush)Resources["AccentSoft"] : Brushes.Transparent;
        DarkModeButton.Foreground = (Brush)Resources[_darkMode ? "Accent" : "MutedText"];
        DarkModeButton.BorderBrush = _darkMode ? (Brush)Resources["Accent"] : Brushes.Transparent;
        DarkModeButton.FontWeight = _darkMode ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void UpdateCodexModeButton()
    {
        if (!_uiInitialized)
        {
            return;
        }

        UpdateSettingsVisualHeader();
        if (CodexModeButton is null || CodexModeText is null || CodexModeIconPath is null)
        {
            return;
        }

        if (_codexDarkMode is true)
        {
            CodexModeText.Text = "Codex 当前暗色";
            CodexModeIconPath.Data = Geometry.Parse("M 15,3 C 9,4 7,9 8.5,13.5 C 10,18 14.5,19.5 19,17 C 17,20.5 12.5,22 8.5,20 C 4,17.8 2,12.5 4,8 C 6,3.8 11,1.7 15,3 Z");
            CodexModeButton.Background = (Brush)Resources["AccentSoft"];
            CodexModeButton.Foreground = (Brush)Resources["Accent"];
            CodexModeButton.BorderBrush = (Brush)Resources["Accent"];
            CodexModeButton.ToolTip = "Codex 当前为暗色，点击切换到亮色";
            return;
        }

        if (_codexDarkMode is false)
        {
            CodexModeText.Text = "Codex 当前亮色";
            CodexModeIconPath.Data = Geometry.Parse("M 12,7 A 5,5 0 1 1 11.9,7 M 12,1 L 12,3 M 12,21 L 12,23 M 1,12 L 3,12 M 21,12 L 23,12");
            CodexModeButton.Background = (Brush)Resources["SkySoft"];
            CodexModeButton.Foreground = (Brush)Resources["Sky"];
            CodexModeButton.BorderBrush = (Brush)Resources["Sky"];
            CodexModeButton.ToolTip = "Codex 当前为亮色，点击切换到暗色";
            return;
        }

        CodexModeText.Text = "检测 Codex 明暗";
        CodexModeButton.Background = (Brush)Resources["SkySoft"];
        CodexModeButton.Foreground = (Brush)Resources["Sky"];
        CodexModeButton.BorderBrush = (Brush)Resources["Sky"];
        CodexModeButton.ToolTip = "点击切换 Codex 明暗色；首次切换后显示当前状态";
    }

    private void SetBusy(bool busy, string? status)
    {
        if (!_uiInitialized) return;

        ActivateButton.IsEnabled = !busy && _selectedTheme?.IsValid == true;
        RestoreButton.IsEnabled = !busy;
        CodexModeButton.IsEnabled = !busy;
        StartupButton.IsEnabled = !busy;
        QuickSwitchButton.IsEnabled = !busy;
        DeleteButton.IsEnabled = !busy && _selectedTheme?.CanDelete == true;
        SettingsThemeSwitchPanel.IsEnabled = !busy;
        if (status is not null) StatusText.Text = status;
    }

    private void SetStatus(string status)
    {
        if (_uiInitialized)
        {
            StatusText.Text = status;
        }
    }

    private Window ProductDialogOwner => IsVisible
        ? this
        : _quickSwitchWindow is { IsVisible: true }
            ? _quickSwitchWindow
            : this;

    private bool ShowProductConfirmation(
        string title,
        string message,
        string confirmText,
        bool dangerous = false) =>
        ProductDialogWindow.Confirm(
            ProductDialogOwner,
            title,
            message,
            confirmText,
            dangerous: dangerous,
            darkMode: _darkMode);

    private void ShowProductMessage(string title, string message, ProductDialogKind kind) =>
        ProductDialogWindow.ShowMessage(ProductDialogOwner, title, message, kind, _darkMode);

    private void ShowToast(string message, bool warning = false)
    {
        if (!_uiInitialized || !IsVisible || ToastPanel is null || _toastTimer is null)
        {
            return;
        }

        _toastTimer.Stop();
        ToastPanel.BeginAnimation(OpacityProperty, null);
        ToastText.Text = message;
        ToastIconText.Text = warning ? "!" : "✓";
        ToastIconText.Foreground = (Brush)Resources[warning ? "Amber" : "Accent"];
        ToastPanel.BorderBrush = (Brush)Resources[warning ? "Amber" : "Border"];
        ToastPanel.Visibility = Visibility.Visible;
        ToastPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        _toastTimer.Start();
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        animation.Completed += (_, _) => ToastPanel.Visibility = Visibility.Collapsed;
        ToastPanel.BeginAnimation(OpacityProperty, animation);
    }

    private void SetEngineState(string status)
    {
        _engineStateText = status;
        if (_uiInitialized)
        {
            EngineStateText.Text = status;
            EngineStateDot.Fill = (Brush)Resources[
                status.Contains("运行中", StringComparison.Ordinal) ||
                status.Contains("可用", StringComparison.Ordinal)
                    ? "Positive"
                    : status.Contains("失败", StringComparison.Ordinal) ||
                      status.Contains("不在", StringComparison.Ordinal)
                        ? "Danger"
                        : "SubtleText"];
        }
    }

    private void Runtime_StatusChanged(object? sender, string status) =>
        _ = Dispatcher.InvokeAsync(() => SetStatus(status));
}
