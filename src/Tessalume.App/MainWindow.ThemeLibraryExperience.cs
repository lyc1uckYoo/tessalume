using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Themes;

namespace Tessalume.App;

public partial class MainWindow
{
    private IEnumerable<ThemeCardModel> OrderThemeSource(IEnumerable<ThemeCardModel> source) =>
        _themeLibrarySort switch
        {
            ThemeLibraryState.RecentSort => source
                .OrderBy(theme => theme.ThemeId is not null && _themeUsage.ContainsKey(theme.ThemeId) ? 0 : 1)
                .ThenByDescending(theme => theme.ThemeId is not null && _themeUsage.TryGetValue(theme.ThemeId, out var usage)
                    ? usage.LastUsedAt
                    : DateTimeOffset.MinValue)
                .ThenBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase),
            ThemeLibraryState.NameSort => source
                .OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(theme => theme.Author, StringComparer.CurrentCultureIgnoreCase),
            ThemeLibraryState.AuthorSort => source
                .OrderBy(theme => theme.Author, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => source,
        };

    private async void ThemeSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized || ThemeSortComboBox.SelectedValue is not string selected) return;
        var normalized = ThemeLibraryState.NormalizeSort(selected);
        if (string.Equals(_themeLibrarySort, normalized, StringComparison.OrdinalIgnoreCase)) return;

        _themeLibrarySort = normalized;
        ApplyThemeLibraryFilter();
        await SavePreferencesAsync();
    }

    private async Task RecordThemeUsageAsync(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId)) return;
        var normalizedId = themeId.Trim();
        var previousCount = _themeUsage.TryGetValue(normalizedId, out var current)
            ? current.UseCount
            : 0;
        _themeUsage[normalizedId] = new ThemeUsageRecord
        {
            ThemeId = normalizedId,
            LastUsedAt = DateTimeOffset.Now,
            UseCount = previousCount == int.MaxValue ? int.MaxValue : previousCount + 1,
        };

        foreach (var stale in _themeUsage.Values
                     .OrderByDescending(record => record.LastUsedAt)
                     .Skip(100)
                     .Select(record => record.ThemeId)
                     .ToArray())
        {
            _themeUsage.Remove(stale);
        }

        await SavePreferencesAsync();
        if (_uiInitialized &&
            _currentRoute == Features.Navigation.AppRoute.ThemeLibrary &&
            string.Equals(_themeLibrarySort, ThemeLibraryState.RecentSort, StringComparison.OrdinalIgnoreCase))
        {
            ApplyThemeLibraryFilter(themeId);
        }
    }

    private void ThemeDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTheme is null) return;
        ThemeDetailPanel.Present(_selectedTheme);
        ThemeDetailPanel.Visibility = Visibility.Visible;
    }

    private void ThemeDetailPanel_CloseRequested(object? sender, EventArgs e) => CloseThemeDetailPanel();

    private async void ThemeDetailPanel_ApplyRequested(object? sender, EventArgs e)
    {
        if (ThemeDetailPanel.Theme is not { } theme) return;
        if (await ApplyThemeAsync(theme))
        {
            ThemeDetailPanel.Present(theme);
        }
    }

    private void ThemeDetailPanel_OpenFolderRequested(object? sender, EventArgs e)
    {
        var directory = ThemeDetailPanel.Theme?.DirectoryPath;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            ShowProductMessage("无法打开主题目录", "这个主题的本地目录已经不存在，请刷新主题库后重试。", ProductDialogKind.Warning);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(directory);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowProductMessage("无法打开主题目录", exception.Message, ProductDialogKind.Error);
        }
    }

    private async void ThemeDetailPanel_CompanionPetRequested(object? sender, EventArgs e)
    {
        CloseThemeDetailPanel();
        NavigateTo(Features.Navigation.AppRoute.Pets);
        await RefreshPetCenterAsync();
    }

    private void CloseThemeDetailPanel()
    {
        if (!_uiInitialized || ThemeDetailPanel.Visibility != Visibility.Visible) return;
        ThemeDetailPanel.Visibility = Visibility.Collapsed;
        ThemeDetailsButton.Focus();
    }

    private void ThemeLibraryPage_DragOver(object sender, DragEventArgs e)
    {
        var valid = TryGetSingleDropPath(e.Data, out _);
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        ThemeDropOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void ThemeLibraryPage_DragLeave(object sender, DragEventArgs e)
    {
        ThemeDropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private async void ThemeLibraryPage_Drop(object sender, DragEventArgs e)
    {
        ThemeDropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (!TryGetSingleDropPath(e.Data, out var path))
        {
            ShowProductMessage(
                "无法识别拖入内容",
                "请一次拖入一个主题文件夹或 ZIP 压缩包。",
                ProductDialogKind.Warning);
            return;
        }

        try
        {
            switch (ThemeLibraryState.ClassifyImportSource(path))
            {
                case ThemeImportSourceKind.Directory:
                    await ImportThemeSourceAsync(path, "拖放文件夹");
                    break;
                case ThemeImportSourceKind.ZipArchive:
                    await ImportArchivePathAsync(path, "拖放 ZIP");
                    break;
                default:
                    ShowProductMessage(
                        "不支持这个文件",
                        "主题库只接受包含 manifest.json 的主题文件夹或 .zip 主题包。",
                        ProductDialogKind.Warning);
                    break;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Dropped theme import failed.", exception);
            ShowProductMessage("无法导入拖入的主题", exception.Message, ProductDialogKind.Error);
        }
    }

    private static bool TryGetSingleDropPath(IDataObject data, out string path)
    {
        path = string.Empty;
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } paths ||
            string.IsNullOrWhiteSpace(paths[0]))
        {
            return false;
        }

        path = paths[0];
        return ThemeLibraryState.ClassifyImportSource(path) != ThemeImportSourceKind.Unsupported;
    }

    private async Task ImportArchivePathAsync(string archivePath, string sourceKind = "ZIP")
    {
        using var extraction = await ThemeArchiveExtractor.ExtractAsync(archivePath);
        await ImportThemeSourceAsync(extraction.ThemeDirectory, sourceKind);
    }

    private async Task<bool> ConfirmThemeOverwriteAsync(
        ThemePackageLoader loader,
        string destination,
        ThemePackage incoming)
    {
        var currentResult = await loader.LoadAsync(destination);
        var current = currentResult.Package?.Manifest;
        var relation = ThemeLibraryState.CompareVersions(current?.Version, incoming.Manifest.Version);
        var (title, action, conclusion, dangerous) = relation switch
        {
            ThemeVersionRelation.Newer => ("更新本地主题", "更新主题", "导入包版本更高，将更新现有主题。", false),
            ThemeVersionRelation.Same => ("覆盖同版本主题", "覆盖主题", "两个主题版本相同，适合替换修改后的同版本内容。", false),
            ThemeVersionRelation.Older => ("确认降级主题", "仍要降级", "导入包版本更低，覆盖后将回到旧版本。", true),
            _ => ("替换本地主题", "替换主题", "版本格式无法自动比较，请确认来源后再覆盖。", false),
        };
        var currentName = current?.Name ?? Path.GetFileName(destination);
        var currentVersion = string.IsNullOrWhiteSpace(current?.Version) ? "未知" : current.Version;
        var currentAuthor = string.IsNullOrWhiteSpace(current?.Author) ? "未知作者" : current.Author;
        var incomingAuthor = string.IsNullOrWhiteSpace(incoming.Manifest.Author)
            ? "未知作者"
            : incoming.Manifest.Author;
        var message =
            $"{conclusion}\n\n" +
            $"本地现有：{currentName}  v{currentVersion}\n" +
            $"作者：{currentAuthor}\n\n" +
            $"准备导入：{incoming.Manifest.Name}  v{incoming.Manifest.Version}\n" +
            $"作者：{incomingAuthor}\n\n" +
            "覆盖只替换这个主题的文件；收藏、图像调节和其他本地配置会继续保留。";
        return ShowProductConfirmation(title, message, action, dangerous);
    }
}
