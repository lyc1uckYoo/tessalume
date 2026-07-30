using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CodexThemeStudio.App.Infrastructure;
using CodexThemeStudio.App.Models;
using CodexThemeStudio.Core.Runtime;
using CodexThemeStudio.Core.Security;
using CodexThemeStudio.Core.Themes;
using Microsoft.Win32;

namespace CodexThemeStudio.App;

public partial class MainWindow : Window, IAsyncDisposable
{
    private const string BuiltInTemplateFolderName = "theme-template-v1";

    private enum RightPane
    {
        Themes,
        ImportGuide,
        UsageGuide,
        Settings,
        Diagnostics,
    }

    private readonly PortableLayout _layout = PortableLayout.Create();
    private readonly ObservableCollection<ThemeCardModel> _themes = [];
    private readonly ObservableCollection<ThemeCardModel> _visibleThemes = [];
    private readonly StudioStateStore _stateStore;
    private readonly UiPreferencesStore _preferencesStore;
    private readonly ThemeTrustStore _trustStore;
    private readonly LoopbackCdpDiscovery _launcherDiscovery = new();
    private readonly CodexPackageLauncher _launcher;
    private readonly ThemeRuntime _runtime;
    private readonly CodexUsageReader _usageReader = new();
    private readonly HashSet<string> _favoriteThemeIds = new(StringComparer.OrdinalIgnoreCase);
    private ThemeCardModel? _selectedTheme;
    private ThemeQuickSwitchWindow? _quickSwitchWindow;
    private string? _activeThemeId;
    private string? _lastThemeId;
    private int? _activePort;
    private bool _showFavorites;
    private bool _darkMode;
    private bool? _codexDarkMode;
    private bool _updatingStartupSetting;
    private bool _startupInitialized;
    private bool _shutdownRequested;
    private bool _switchWithinFavorites;
    private bool _uiInitialized;
    private bool _mainContentLoaded;
    private RightPane _rightPane = RightPane.Themes;

    public MainWindow()
    {
        _stateStore = new StudioStateStore(_layout.DataDirectory);
        _preferencesStore = new UiPreferencesStore(_layout.DataDirectory);
        _trustStore = new ThemeTrustStore(_layout.DataDirectory);
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

        var preferences = _preferencesStore.Load();
        _darkMode = preferences.DarkMode;
        _favoriteThemeIds.UnionWith(preferences.FavoriteThemeIds.Where(id => !string.IsNullOrWhiteSpace(id)));
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
        ApplyStudioTheme(_darkMode);
        UpdateStartupButton();
    }

    internal async Task StartInQuickModeAsync()
    {
        if (_startupInitialized) return;
        _startupInitialized = true;
        await ReloadThemesAsync(loadPreviews: false);
        OpenQuickSwitchWindow();
        if (!await ApplyRandomThemeOnStartupAsync())
        {
            await TryResumeAsync();
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
        await _runtime.DisposeAsync();
        _launcherDiscovery.Dispose();
        _trustStore.Dispose();
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
                : "主题包未通过安全校验，请打开诊断查看";
        UpdateLibraryMetrics();
        RefreshQuickSwitchWindow();
    }

    private void ShowThemes(string? preferredId = null)
    {
        _showFavorites = false;
        _visibleThemes.Clear();
        foreach (var theme in _themes)
        {
            _visibleThemes.Add(theme);
        }

        CategoryTitleText.Text = "主题画廊";
        CategoryDescriptionText.Text = "沉浸式角色主题、动态组件与完整交互，都集中在这里。";
        ImportDeclarationTitleText.Text = "你的视觉工作台";
        ImportDeclarationBodyText.Text = "浏览、收藏并一键应用完整主题；导入前会检查本地源码。";
        DeclarationIconText.Text = "✦";
        ImportButton.Content = "导入主题";
        ImportButton.Visibility = Visibility.Visible;
        UpdateCategoryButtons();
        UpdateEmptyState();
        var preferred = _visibleThemes.FirstOrDefault(theme =>
            theme.CatalogItem.Package?.Manifest.Id == preferredId && theme.IsValid);
        var next = preferred ?? _visibleThemes.FirstOrDefault(theme => theme.IsValid);
        if (next is not null)
        {
            SelectTheme(next);
        }
        else
        {
            foreach (var theme in _themes) theme.IsSelected = false;
            _selectedTheme = null;
            SelectedThemeText.Text = "这里还没有可用主题";
            ActivateButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
        }
    }

    private void ShowFavorites(string? preferredId = null)
    {
        _showFavorites = true;
        _visibleThemes.Clear();
        foreach (var theme in _themes.Where(theme => theme.IsFavorite))
        {
            _visibleThemes.Add(theme);
        }

        CategoryTitleText.Text = "我的收藏";
        CategoryDescriptionText.Text = "集中查看你喜欢的角色主题，可直接选择并应用。";
        ImportDeclarationTitleText.Text = "只留下真正喜欢的主题";
        ImportDeclarationBodyText.Text = "收藏仅保存在本机，在卡片右上角即可随时加入或移出。";
        DeclarationIconText.Text = "♥";
        ImportButton.Visibility = Visibility.Collapsed;
        UpdateCategoryButtons();
        UpdateEmptyState();

        var preferred = _visibleThemes.FirstOrDefault(theme =>
            theme.CatalogItem.Package?.Manifest.Id == preferredId && theme.IsValid);
        var next = preferred ?? _visibleThemes.FirstOrDefault(theme => theme.IsValid);
        if (next is not null)
        {
            SelectTheme(next);
            return;
        }

        foreach (var theme in _themes) theme.IsSelected = false;
        _selectedTheme = null;
        SelectedThemeText.Text = "还没有收藏的主题";
        ActivateButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
    }

    private void UpdateEmptyState()
    {
        var isEmpty = _visibleThemes.Count == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        SelectionDock.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateTitleText.Text = _showFavorites ? "还没有收藏的主题" : "这里还没有主题";
        EmptyStateBodyText.Text = _showFavorites
            ? "点击任意主题卡片右上角的爱心，把它加入我的收藏。"
            : "使用上方按钮导入一个本地主题文件夹。";
    }

    private async Task TryResumeAsync()
    {
        var state = await _stateStore.LoadAsync();
        var port = state is not null && await _launcher.IsDebugPortReadyAsync(state.Port)
            ? state.Port
            : await _launcher.FindRunningDebugPortAsync();
        if (port is null)
        {
            SetEngineState("主题引擎待启动");
            return;
        }

        _activePort = port.Value;
        if (state is null)
        {
            SetEngineState($"Codex 可用 · 本机 {port.Value}");
            SetStatus("选择本地主题后即可应用");
            return;
        }

        if (!state.Enabled)
        {
            SetEngineState("Codex 默认外观");
            SetStatus("主题已停用，可随时重新应用");
            return;
        }

        var theme = _themes.FirstOrDefault(item =>
            item.CatalogItem.Package?.Manifest.Id == state.ThemeId && item.IsValid);
        if (theme?.CatalogItem.Package is null)
        {
            SetEngineState("上次主题已不在本地库");
            return;
        }

        if (_uiInitialized)
        {
            ShowThemes(state.ThemeId);
        }

        await EnsureTrustedAsync(theme.CatalogItem.Package);

        await _runtime.StartAsync(port.Value, theme.CatalogItem.Package);
        await LegacyInjectorMigrator.TryStopAsync();
        await _stateStore.SaveAsync(new StudioState
        {
            Port = port.Value,
            ThemeId = theme.CatalogItem.Package.Manifest.Id,
            UpdatedAt = DateTimeOffset.Now,
            Enabled = true,
        });
        SetEngineState($"运行中 · 本机 {port.Value}");
        _activeThemeId = theme.CatalogItem.Package.Manifest.Id;
        _lastThemeId = _activeThemeId;
        UpdateAppliedThemeState();
        SetStatus($"{theme.Name} 已恢复");
        RefreshQuickSwitchWindow();
    }

    private async Task<bool> ApplyRandomThemeOnStartupAsync()
    {
        var favorites = _themes.Where(theme => theme.IsFavorite && theme.IsValid).ToArray();
        _switchWithinFavorites = favorites.Length > 0;
        var candidates = favorites.Length > 0
            ? favorites
            : _themes.Where(theme => theme.IsValid).ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        var theme = candidates[Random.Shared.Next(candidates.Length)];
        SelectTheme(theme);
        if (_uiInitialized)
        {
            StatusText.Text = favorites.Length > 0
                ? $"已随机选择收藏主题：{theme.Name}"
                : $"收藏为空，已随机选择主题：{theme.Name}";
        }

        await ApplyThemeAsync(theme);
        return true;
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
                overwrite = MessageBox.Show(
                    $"本地库中已有“{result.Package.Manifest.Name}”。是否替换为所选版本？",
                    "替换本地主题",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes;
                if (!overwrite) return;
            }

            var imported = await new ThemeImporter(loader).ImportAsync(dialog.FolderName, destinationDirectory, overwrite);
            _showFavorites = false;
            await ReloadThemesAsync(imported.Manifest.Id);
            StatusText.Text = $"{imported.Manifest.Name} 已加入主题库";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(exception.Message, "无法导入主题", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RefreshThemes_Click(object sender, RoutedEventArgs e)
    {
        await ReloadThemesAsync();
        StatusText.Text = $"本地主题库已刷新，共 {_themes.Count} 个主题";
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
        if (MessageBox.Show(
                message,
                "删除本地主题",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
            RefreshQuickSwitchWindow();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = exception.Message;
            MessageBox.Show(exception.Message, "无法删除主题", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, null);
            IdleMemoryTrimmer.Schedule();
        }
    }

    private void ImportGuide_Click(object sender, RoutedEventArgs e) => ShowInfoPage(RightPane.ImportGuide);

    private void UsageGuide_Click(object sender, RoutedEventArgs e) => ShowInfoPage(RightPane.UsageGuide);

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _updatingStartupSetting = true;
        StartupCheckBox.IsChecked = StartupRegistration.IsEnabled();
        _updatingStartupSetting = false;
        ShowInfoPage(RightPane.Settings);
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

            MessageBox.Show(exception.Message, "无法打开主题浮窗", MessageBoxButton.OK, MessageBoxImage.Error);
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
            (_switchWithinFavorites
                ? _themes.Where(theme => theme.IsFavorite && theme.IsValid)
                : _themes.Where(theme => theme.IsValid)).ToArray());
    }

    private void StartupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var enabled = !StartupRegistration.IsEnabled();
            StartupRegistration.SetEnabled(enabled);
            UpdateStartupButton();
            StatusText.Text = enabled ? "已启用开机自动启动" : "已关闭开机自动启动";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法更新开机启动设置", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateStartupButton()
    {
        if (StartupButton is null) return;
        var enabled = StartupRegistration.IsEnabled();
        StartupButton.Tag = enabled ? "active" : "inactive";
        StartupButton.Content = enabled ? "开机启动已开启" : "开启开机启动";
        StartupButton.ToolTip = enabled ? "点击关闭登录 Windows 后自动启动" : "点击开启登录 Windows 后自动启动";
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
            StatusText.Text = enabled ? "已启用开机自动启动" : "已关闭开机自动启动";
        }
        catch (Exception exception)
        {
            _updatingStartupSetting = true;
            StartupCheckBox.IsChecked = StartupRegistration.IsEnabled();
            _updatingStartupSetting = false;
            MessageBox.Show(exception.Message, "无法更新开机启动设置", MessageBoxButton.OK, MessageBoxImage.Error);
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
        UpdateCategoryButtons();
        AnimatePage(InfoPage);
    }

    private void OpenTemplate_Click(object sender, RoutedEventArgs e)
    {
        var path = GetTemplatePath();
        if (!Directory.Exists(path))
        {
            MessageBox.Show("模板文件尚未释放，请重启应用后再试。", "找不到模板", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void CopyTemplate_Click(object sender, RoutedEventArgs e)
    {
        var source = GetTemplatePath();
        if (!Directory.Exists(source))
        {
            MessageBox.Show("模板文件尚未释放，请重启应用后再试。", "找不到模板", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new OpenFolderDialog { Title = "选择新主题的保存位置", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;

        const string name = "my-character-theme";
        var destination = Path.Combine(dialog.FolderName, name);
        if (Directory.Exists(destination))
        {
            MessageBox.Show($"目标位置已存在文件夹：\n{destination}", "无法复制模板", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CopyDirectory(source, destination);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{destination}\"") { UseShellExecute = true });
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
        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port;
        var portReady = port is not null && await _launcher.IsDebugPortReadyAsync(port.Value);
        var validThemes = _themes.Count(theme => theme.IsValid);
        var activeTheme = state is not null
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
        ShowInfoPage(RightPane.Diagnostics);
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
            if (!await EnsureTrustedAsync(package))
            {
                SetStatus("主题未获授权，Codex 未作改动");
                return false;
            }

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
                    SetStatus("正在自动关闭并重启 Codex…");
                    await CodexPackageLauncher.CloseCodexAsync();
                }

                port = CodexPackageLauncher.FindFreePort();
                SetStatus($"正在本机端口 {port} 启动 Codex…");
                await _launcher.LaunchAndWaitAsync(port);
            }

            SetStatus("正在应用本地主题…");
            await _runtime.StartAsync(port, package);
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
            RefreshQuickSwitchWindow();
            return true;
        }
        catch (Exception exception)
        {
            SetEngineState("启动失败");
            SetStatus(exception.Message);
            MessageBox.Show(exception.Message, "无法应用主题", MessageBoxButton.OK, MessageBoxImage.Error);
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
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            MessageBox.Show(exception.Message, "恢复失败", MessageBoxButton.OK, MessageBoxImage.Error);
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
            UpdateCodexModeButton();

            SetStatus(dark ? "Codex 已切换为暗色" : "Codex 已切换为亮色");
            return dark;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            MessageBox.Show(exception.Message, "无法切换 Codex 明暗色", MessageBoxButton.OK, MessageBoxImage.Error);
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
            UpdateCodexModeButton();
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
            dark ? "#0D1015" : "#F7F8FA",
            dark ? "#11151B" : "#F1F3F6");
        SetGradientBrush(
            "SidebarBackground",
            dark ? "#13161D" : "#FBFBFC",
            dark ? "#171B23" : "#F6F7F9");
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
        SetBrush("Surface", dark ? "#20242C" : "#FFFFFF");
        SetBrush("SurfaceAlt", dark ? "#282D36" : "#EFF1F4");
        SetBrush("SurfaceElevated", dark ? "#242933" : "#FFFFFF");
        SetBrush("HoverSurface", dark ? "#303641" : "#E9EBEF");
        SetBrush("InfoSurface", dark ? "#272C3D" : "#EEF2FF");
        SetBrush("InfoBorder", dark ? "#414962" : "#D9E0FA");
        SetBrush("PrimaryText", dark ? "#E8ECF2" : "#191B20");
        SetBrush("MutedText", dark ? "#B1B8C3" : "#626670");
        SetBrush("SubtleText", dark ? "#858D99" : "#9297A1");
        SetBrush("Border", dark ? "#3A414C" : "#DFE2E7");
        SetBrush("Accent", dark ? "#8493FA" : "#5365E8");
        SetBrush("AccentSoft", dark ? "#29304B" : "#E9ECFF");
        SetBrush("ActiveNav", dark ? "#292D43" : "#EEF0FF");
        SetBrush("Positive", dark ? "#52CEA0" : "#2EB47F");
        SetBrush("Danger", dark ? "#FF829E" : "#D94C70");
        SetBrush("DangerSoft", dark ? "#38232B" : "#FFF0F4");
        SetBrush("Sky", dark ? "#8F9BFF" : "#6272E8");
        SetBrush("SkySoft", dark ? "#2C324D" : "#EDF0FF");
        SetBrush("Amber", dark ? "#F1B85B" : "#D58A22");
        SetBrush("AmberSoft", dark ? "#3A3020" : "#FFF6E6");
        foreach (var theme in _themes)
        {
            theme.SetDarkMode(dark);
        }
        _quickSwitchWindow?.SetShellTheme(dark);
        SetEngineState(EngineStateText.Text);
        NativeTitleBar.Apply(this, dark);
        UpdateCategoryButtons();
        UpdateModeButtons();
        UpdateStartupButton();
        UpdateQuickSwitchButton();
        UpdateCodexModeButton();
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
        FavoriteThemeIds = _favoriteThemeIds.Order(StringComparer.OrdinalIgnoreCase).ToList(),
    });

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
        if (!_uiInitialized || CodexModeButton is null || CodexModeText is null || CodexModeIconPath is null)
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
        if (status is not null) StatusText.Text = status;
    }

    private void SetStatus(string status)
    {
        if (_uiInitialized)
        {
            StatusText.Text = status;
        }
    }

    private void SetEngineState(string status)
    {
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

    private async Task<bool> EnsureTrustedAsync(ThemePackage package)
    {
        if (await _trustStore.IsTrustedAsync(package)) return true;

        await _trustStore.TrustAsync(package);
        return true;
    }
}
