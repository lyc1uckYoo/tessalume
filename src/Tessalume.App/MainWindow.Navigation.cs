using System.Diagnostics;
using System.IO;
using System.Windows;
using Tessalume.App.Features.About;
using Tessalume.App.Features.Navigation;

namespace Tessalume.App;

public partial class MainWindow
{
    private void ImportGuide_Click(object sender, RoutedEventArgs e) => NavigateTo(AppRoute.ImportTheme);

    private void About_Click(object sender, RoutedEventArgs e)
    {
        RenderAboutOverview();
        AboutPage.ShowSection(AboutSection.Product);
        NavigateTo(AppRoute.About);
    }

    private async void Data_Click(object sender, RoutedEventArgs e)
    {
        RenderAboutOverview();
        AboutPage.ShowSection(AboutSection.DataAndUpdates);
        NavigateTo(AppRoute.DataAndUpdates);
        await RefreshRollbackAvailabilityAsync();
    }

    private void RenderAboutOverview()
    {
        var validCount = _themes.Count(theme => theme.IsValid);
        var favoriteCount = _themes.Count(theme => theme.IsFavorite);
        AboutPage.RenderOverview(new AboutOverview(
            _layout.RootDirectory,
            _layout.DataDirectory,
            _themes.Count,
            validCount,
            favoriteCount));
    }

    private void AboutPage_OpenRootDirectoryRequested(object? sender, EventArgs e) =>
        OpenDirectory(_layout.RootDirectory);

    private void AboutPage_OpenDataDirectoryRequested(object? sender, EventArgs e) =>
        OpenDirectory(_layout.DataDirectory);

    private void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        ShowToast("已在文件资源管理器中打开目录");
    }
}
