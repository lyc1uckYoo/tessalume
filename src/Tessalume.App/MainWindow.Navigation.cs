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

}
