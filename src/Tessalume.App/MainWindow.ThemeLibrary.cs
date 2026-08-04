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

public partial class MainWindow
{
    private async Task ReloadThemesAsync(string? preferredId = null, bool? loadPreviews = null)
    {
        if (_uiInitialized)
        {
            StatusText.Text = "正在验证本地主题包…";
        }

        preferredId ??= _selectedTheme?.CatalogItem.Package?.Manifest.Id;
        var shouldLoadPreviews = loadPreviews ?? _uiInitialized;
        var favoriteIds = new HashSet<string>(_favoriteThemeIds, StringComparer.OrdinalIgnoreCase);
        var darkMode = _darkMode;
        var activeThemeId = _activeThemeId;
        // Package validation and especially BitmapImage decoding can take noticeable
        // time on a cold disk. Build only frozen, cross-thread-safe models here, then
        // publish the completed collection on the dispatcher below.
        var loadedThemes = await Task.Run(async () =>
        {
            var catalog = await new ThemeCatalog(new ThemePackageLoader())
                .ScanAsync(_layout.ThemesDirectory)
                .ConfigureAwait(false);
            return catalog.Select(item =>
            {
                var themeId = item.Package?.Manifest.Id;
                var theme = new ThemeCardModel(
                    item,
                    themeId is not null && favoriteIds.Contains(themeId),
                    shouldLoadPreviews);
                theme.SetDarkMode(darkMode);
                theme.IsApplied = string.Equals(
                    themeId,
                    activeThemeId,
                    StringComparison.OrdinalIgnoreCase);
                return theme;
            }).ToArray();
        });

        _themes.Clear();
        foreach (var theme in loadedThemes)
        {
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
        source = OrderThemeSource(source);

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
        ThemeDetailsButton.IsEnabled = false;
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
        ThemeDetailsButton.IsEnabled = true;
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
            await ImportThemeSourceAsync(dialog.FolderName, "文件夹");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Theme import failed.", exception);
            ShowProductMessage("无法导入主题", exception.Message, ProductDialogKind.Error);
        }
    }

    private async void ImportArchive_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Tessalume ZIP 主题包",
            Filter = "Tessalume 主题包 (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await ImportArchivePathAsync(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("ZIP theme import failed.", exception);
            ShowProductMessage("无法导入 ZIP 主题包", exception.Message, ProductDialogKind.Error);
        }
    }

    private async Task ImportThemeSourceAsync(string sourceDirectory, string sourceKind)
    {
        var loader = new ThemePackageLoader();
        var result = await loader.LoadAsync(sourceDirectory);
        if (result.Package is null)
        {
            throw new InvalidDataException(string.Join(
                Environment.NewLine,
                result.Validation.Issues.Select(issue => $"• {issue.Message}")));
        }

        if (!result.Package.IsAdvanced)
        {
            throw new InvalidDataException("这个主题包不是受支持的沉浸式主题；主题必须包含 theme.js。");
        }

        var destinationDirectory = _layout.ThemesDirectory;
        var destination = Path.Combine(destinationDirectory, result.Package.Manifest.Id);
        var overwrite = false;
        if (Directory.Exists(destination) &&
            !string.Equals(Path.GetFullPath(sourceDirectory), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            overwrite = await ConfirmThemeOverwriteAsync(loader, destination, result.Package);
            if (!overwrite) return;
        }

        var imported = await new ThemeImporter(loader).ImportAsync(sourceDirectory, destinationDirectory, overwrite);
        _showFavorites = false;
        await ReloadThemesAsync(imported.Manifest.Id);
        StatusText.Text = $"{imported.Manifest.Name} 已加入主题库 · 来源：{sourceKind}";
        ShowToast($"{imported.Manifest.Name} 已加入主题库");
        LocalLog.Write($"Imported theme {imported.Manifest.Id} from {sourceKind}.");
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
            if (themeId is not null &&
                (_favoriteThemeIds.Remove(themeId) | _themeUsage.Remove(themeId)))
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

}
