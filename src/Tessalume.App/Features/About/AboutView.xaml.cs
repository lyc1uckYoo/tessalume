using System.Windows;
using System.Windows.Controls;

namespace Tessalume.App.Features.About;

internal enum AboutSection
{
    Product,
    DataAndUpdates,
}

public partial class AboutView : UserControl
{
    private bool _synchronizingSettings;

    public AboutView()
    {
        InitializeComponent();
        ShowSection(AboutSection.Product);
    }

    public event EventHandler? OpenRootDirectoryRequested;
    public event EventHandler? OpenDataDirectoryRequested;
    public event EventHandler? BackupRequested;
    public event EventHandler? RestoreRequested;
    public event EventHandler<AboutBooleanSettingChangedEventArgs>? StartupSettingChanged;
    public event EventHandler<AboutBooleanSettingChangedEventArgs>? AutomaticUpdateSettingChanged;
    public event EventHandler? CheckForUpdatesRequested;
    public event EventHandler? RollbackRequested;

    public bool IncludeImportedThemes => IncludeImportedThemesCheckBox.IsChecked == true;

    internal void ShowSection(AboutSection section)
    {
        var showProduct = section == AboutSection.Product;
        PageTitleText.Text = showProduct ? "关于 Tessalume" : "更新与数据";
        PageDescriptionText.Text = showProduct
            ? "查看版本信息、产品定位与本次版本能力。"
            : "管理更新、启动行为、便携数据与本机目录。";
        IdentityCard.Visibility = showProduct ? Visibility.Visible : Visibility.Collapsed;
        ReleaseHighlightsLabel.Visibility = showProduct ? Visibility.Visible : Visibility.Collapsed;
        ReleaseHighlightsGrid.Visibility = showProduct ? Visibility.Visible : Visibility.Collapsed;
        DataManagementCard.Visibility = showProduct ? Visibility.Collapsed : Visibility.Visible;
        ApplicationBehaviorLabel.Visibility = showProduct ? Visibility.Collapsed : Visibility.Visible;
        ApplicationBehaviorCard.Visibility = showProduct ? Visibility.Collapsed : Visibility.Visible;
    }

    public void RenderOverview(AboutOverview overview)
    {
        RootDirectoryText.Text = overview.RootDirectory;
        DataDirectoryText.Text = overview.DataDirectory;
        LibrarySummaryText.Text =
            $"本地库共 {overview.ThemeCount} 个主题 · " +
            $"{overview.ValidThemeCount} 个通过校验 · " +
            $"{overview.FavoriteThemeCount} 个收藏";
    }

    public void SetStartupEnabled(bool enabled)
    {
        _synchronizingSettings = true;
        StartupCheckBox.IsChecked = enabled;
        _synchronizingSettings = false;
    }

    public void RenderUpdateState(AboutUpdateState state)
    {
        _synchronizingSettings = true;
        AutomaticUpdatesCheckBox.IsChecked = state.AutomaticChecksEnabled;
        _synchronizingSettings = false;
        CheckForUpdatesButton.IsEnabled = !state.IsChecking;
        UpdateStatusText.Text = state.Status;
        RollbackStatusText.Text = state.Rollback.Status;
        RollbackVersionButton.ToolTip = state.Rollback.ToolTip;
        RollbackVersionButton.IsEnabled =
            state.Rollback.IsAvailable && !state.IsChecking && !state.Rollback.IsBusy;
        if (!state.IsChecking && UpdateProgressBar.Value <= 0)
        {
            SetUpdateProgress(0, string.Empty, visible: false);
        }
    }

    public void SetUpdateProgress(double value, string text, bool visible = true)
    {
        UpdateProgressBar.Value = Math.Clamp(value, 0, 100);
        UpdateProgressBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgressText.Text = text;
        UpdateProgressText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetBackupBusy(bool busy, string? status)
    {
        BackupUserDataButton.IsEnabled = !busy;
        RestoreUserDataButton.IsEnabled = !busy;
        IncludeImportedThemesCheckBox.IsEnabled = !busy;
        if (status is not null)
        {
            BackupStatusText.Text = status;
        }
    }

    public void SetBackupStatus(string status) => BackupStatusText.Text = status;

    private void OpenRootDirectory_Click(object sender, RoutedEventArgs e) =>
        OpenRootDirectoryRequested?.Invoke(this, EventArgs.Empty);

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e) =>
        OpenDataDirectoryRequested?.Invoke(this, EventArgs.Empty);

    private void BackupUserData_Click(object sender, RoutedEventArgs e) =>
        BackupRequested?.Invoke(this, EventArgs.Empty);

    private void RestoreUserData_Click(object sender, RoutedEventArgs e) =>
        RestoreRequested?.Invoke(this, EventArgs.Empty);

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_synchronizingSettings) return;
        StartupSettingChanged?.Invoke(
            this,
            new AboutBooleanSettingChangedEventArgs(StartupCheckBox.IsChecked == true));
    }

    private void AutomaticUpdatesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_synchronizingSettings) return;
        AutomaticUpdateSettingChanged?.Invoke(
            this,
            new AboutBooleanSettingChangedEventArgs(AutomaticUpdatesCheckBox.IsChecked == true));
    }

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e) =>
        CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

    private void RollbackVersion_Click(object sender, RoutedEventArgs e) =>
        RollbackRequested?.Invoke(this, EventArgs.Empty);
}
