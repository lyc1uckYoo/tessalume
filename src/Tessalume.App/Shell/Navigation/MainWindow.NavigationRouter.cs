using System.Windows;
using Tessalume.App.Features.Navigation;

namespace Tessalume.App;

public partial class MainWindow
{
    private void ShowThemeLibraryPage()
    {
        _currentRoute = AppRoute.ThemeLibrary;
        ThemeLibraryPage.Visibility = Visibility.Visible;
        InfoPage.Visibility = Visibility.Collapsed;
        UpdateCategoryButtons();
        AnimatePage(ThemeLibraryPage);
    }

    private void NavigateTo(AppRoute route)
    {
        CloseThemeDetailPanel();
        ThemeDropOverlay.Visibility = Visibility.Collapsed;
        _currentRoute = route;
        ThemeLibraryPage.Visibility = Visibility.Collapsed;
        InfoPage.Visibility = Visibility.Visible;
        ImportInfoPanel.Visibility = route == AppRoute.ImportTheme ? Visibility.Visible : Visibility.Collapsed;
        CreatorCenter.Visibility = route == AppRoute.CreatorCenter ? Visibility.Visible : Visibility.Collapsed;
        SettingsInfoPanel.Visibility = route == AppRoute.ArtworkStudio ? Visibility.Visible : Visibility.Collapsed;
        ExperienceInfoPanel.Visibility = route == AppRoute.ExperienceProfiles ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = route == AppRoute.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = route is AppRoute.About or AppRoute.DataAndUpdates
            ? Visibility.Visible
            : Visibility.Collapsed;
        InfoScroll.ScrollToTop();
        UpdateCategoryButtons();
        AnimatePage(InfoPage);
    }
}
