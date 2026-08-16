using System.Windows;
using Tessalume.App.Features.Navigation;

namespace Tessalume.App;

public partial class MainWindow
{
    private void ShowThemeLibraryPage()
    {
        _currentRoute = AppRoute.ThemeLibrary;
        SetArtworkConnectionMonitoring(false);
        PetCenterPage.SetPageActive(false);
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
        // The canvas-led artwork route needs enough width to preserve the real
        // banner/sidebar/chat proportions. Other information routes retain the
        // compact reading measure used by Tessalume 2.0.
        InfoContentHost.MaxWidth = route switch
        {
            AppRoute.ArtworkStudio => 1440,
            AppRoute.Pets => 1040,
            _ => 940,
        };
        SetArtworkConnectionMonitoring(route == AppRoute.ArtworkStudio);
        var isPersonalization = route is AppRoute.ArtworkStudio or AppRoute.DisplayPreferences;
        ThemeLibraryPage.Visibility = Visibility.Collapsed;
        InfoPage.Visibility = Visibility.Visible;
        ImportInfoPanel.Visibility = route == AppRoute.ImportTheme ? Visibility.Visible : Visibility.Collapsed;
        CreatorCenter.Visibility = route == AppRoute.CreatorCenter ? Visibility.Visible : Visibility.Collapsed;
        PetCenterPage.Visibility = route == AppRoute.Pets ? Visibility.Visible : Visibility.Collapsed;
        PetCenterPage.SetPageActive(route == AppRoute.Pets);
        PersonalizationInfoPanel.Visibility = isPersonalization ? Visibility.Visible : Visibility.Collapsed;
        SettingsInfoPanel.Visibility = route == AppRoute.ArtworkStudio ? Visibility.Visible : Visibility.Collapsed;
        DisplayPreferencesInfoPanel.Visibility = route == AppRoute.DisplayPreferences ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = route == AppRoute.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = route is AppRoute.About or AppRoute.DataAndUpdates
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (isPersonalization)
        {
            RenderPersonalizationPageHeader(route);
        }
        InfoScroll.ScrollToTop();
        UpdateCategoryButtons();
        AnimatePage(InfoPage);
    }

    private void RenderPersonalizationPageHeader(AppRoute route)
    {
        var artwork = route == AppRoute.ArtworkStudio;
        PersonalizationPageTitleText.Text = artwork ? "图像工作台" : "显示偏好";
        PersonalizationPageDescriptionText.Text = artwork
            ? "在原图上完成最终取景，再检查页面中的真实效果。"
            : "调整动效、正文字号和内容疏密，让当前主题读起来更舒服。";
        PersonalizationPageStatusText.Text = artwork ? "离线也可编辑" : "跟随主题保存";
    }
}
