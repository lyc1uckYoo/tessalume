using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Tessalume.App.Features.Pets;

public partial class PetCenterView : UserControl, IDisposable
{
    private readonly PetPreviewPlayer _previewPlayer;
    private PetCenterPresentationState _state = new();
    private bool _pageActive;
    private bool _disposed;

    public PetCenterView()
    {
        InitializeComponent();
        _previewPlayer = new PetPreviewPlayer(
            PetPreviewImage,
            PreviewStateText,
            runtimeTarget: PetRuntimePreview);
        _previewPlayer.PlaybackStateChanged += PreviewPlayer_PlaybackStateChanged;
        _previewPlayer.SelectionChanged += PreviewPlayer_SelectionChanged;
        GalleryPanel.EntryRequested += GalleryPanel_EntryRequested;
        GalleryPanel.RefreshRequested += GalleryPanel_RefreshRequested;
    }

    internal event EventHandler<PetGalleryEntry>? PetRequested;

    internal event EventHandler? GalleryRefreshRequested;

    internal event EventHandler? BackToGalleryRequested;

    internal event EventHandler? RefreshRequested;

    internal event EventHandler<PetCenterAction>? PrimaryActionRequested;

    internal event EventHandler? OpenCodexRequested;

    internal event EventHandler? RecommendedThemeRequested;

    internal event EventHandler? ApplyRecommendedThemeRequested;

    internal event EventHandler? UninstallRequested;

    internal event EventHandler? SelectionAcknowledgementRequested;

    internal event EventHandler? RestoreBackupRequested;

    internal PetPreviewPlayer PreviewPlayer => _previewPlayer;

    internal bool IsShowingGallery => GalleryPanel.Visibility == Visibility.Visible;

    internal void ShowGalleryLoading()
    {
        GalleryPanel.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Collapsed;
        GalleryPanel.ShowLoading();
        _previewPlayer.SetActive(false);
    }

    internal void RenderGallery(PetGallerySnapshot snapshot)
    {
        GalleryPanel.Render(snapshot);
        ShowGallery();
    }

    internal void UpdateGalleryData(PetGallerySnapshot snapshot) =>
        GalleryPanel.Render(snapshot);

    internal void ShowGallery()
    {
        GalleryPanel.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Collapsed;
        _previewPlayer.SetActive(false);
    }

    internal void Render(PetCenterPresentationState state)
    {
        _state = state;
        GalleryPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        DetailPageTitleText.Text = state.DisplayName;
        DetailPageSubtitleText.Text = "查看真实动作、角色资料和本地安装状态。";
        PetDisplayNameText.Text = state.DisplayName;
        PetDescriptionText.Text = state.Description;
        PetSourceBadgeText.Text = state.SourceBadge;
        PetSourceBadge.Background = (Brush)FindResource("TealSoft");
        PetSourceBadgeText.Foreground = (Brush)FindResource("Teal");
        InstallationStatusTitle.Text = state.StatusTitle;
        InstallationStatusDetail.Text = state.StatusDetail;
        ProductVersionText.Text = state.ProductVersion;
        ProtocolText.Text = state.ProtocolSummary;
        AuthorLicenseText.Text = $"{state.Author} · {FormatLicenseSummary(state.LicenseSummary)}";
        InstallLocationText.Text = FormatLocation(state.InstallLocation);
        InstallLocationText.ToolTip = $"仅管理 {state.InstallLocation}";
        LocationLabelText.Text = "管理范围";
        PrimaryActionButton.Content = state.PrimaryActionText;
        PrimaryActionButton.IsEnabled = state.PrimaryActionEnabled && !state.IsBusy;
        PrimaryActionGroup.Visibility = Visibility.Visible;
        RefreshButton.IsEnabled = !state.IsBusy;
        RefreshButton.Content = "↻  刷新资源";
        UninstallButton.IsEnabled = state.CanUninstall && !state.IsBusy;
        UninstallButton.Visibility = Visibility.Visible;
        RestoreBackupButton.IsEnabled = state.CanRestoreBackup && !state.IsBusy;
        RestoreBackupButton.Visibility = Visibility.Visible;
        ManagementSectionTitle.Text = "管理工具";
        AcknowledgeSelectionButton.Visibility = state.CanAcknowledgeSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        AcknowledgeSelectionButton.IsEnabled = !state.IsBusy;
        ActivationGuidePanel.Visibility = state.Status == PetCenterStatus.AwaitingCodexSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        ActivationGuideText.Text =
            $"Settings → Pets → Refresh → 选择{state.DisplayName} → 输入 /pet";
        RecommendedThemeNameText.Text = state.RecommendedThemeName;
        RecommendedThemeSection.Visibility = state.HasRecommendedTheme
            ? Visibility.Visible
            : Visibility.Collapsed;
        RestoreBackupButton.ToolTip = state.LatestBackupLabel ?? "当前没有可恢复备份";
        AutomationProperties.SetName(PetPreviewImage, $"{state.DisplayName}动态动作预览");
        AutomationProperties.SetName(PetRuntimePreview, $"{state.DisplayName}实时图集动作预览");
        _previewPlayer.Configure(state.PreviewFrames);
        _previewPlayer.SetActive(_pageActive && IsVisible && DetailPanel.IsVisible);
        UpdatePreviewButtons();
        UpdatePlaybackPresentation();
        RenderStatusPalette(state.Status);
        ApplyDetailLayout(ActualWidth, ActualHeight);
    }

    private static string FormatLicenseSummary(string licenseSummary) =>
        licenseSummary.Trim() switch
        {
            var value when value.Equals("All rights reserved", StringComparison.OrdinalIgnoreCase) =>
                "保留所有权利",
            var value when value.Equals("LicenseRef-All-Rights-Reserved", StringComparison.OrdinalIgnoreCase) =>
                "保留所有权利",
            var value => value,
        };

    private static string FormatLocation(string location)
    {
        const int maximumVisibleCharacters = 34;
        var fullText = $"仅管理 {location}";
        if (fullText.Length <= maximumVisibleCharacters)
        {
            return fullText;
        }

        const int visiblePrefixLength = 17;
        const int visibleSuffixLength = 14;
        return $"{fullText[..visiblePrefixLength]}…{fullText[^visibleSuffixLength..]}";
    }

    internal void SetPageActive(bool active)
    {
        _pageActive = active;
        _previewPlayer.SetActive(active && IsVisible && DetailPanel.IsVisible);
    }

    private void RenderStatusPalette(PetCenterStatus status)
    {
        var resourceKey = status switch
        {
            PetCenterStatus.Installed => "Positive",
            PetCenterStatus.AwaitingCodexSelection => "Sky",
            PetCenterStatus.UpdateAvailable => "Amber",
            PetCenterStatus.UnknownModification => "Amber",
            PetCenterStatus.Damaged => "Danger",
            PetCenterStatus.DuplicateIdConflict => "Danger",
            PetCenterStatus.Error => "Danger",
            _ => "Accent",
        };
        var brush = (Brush)FindResource(resourceKey);
        InstallationStatusDot.Fill = brush;
        InstallationStatusHalo.Fill = brush;
    }

    private void UpdatePlaybackPresentation()
    {
        PlaybackStatusText.Text = _previewPlayer.PlaybackDescription;
        PreviewFallbackText.Visibility = !_previewPlayer.HasVisual
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomationProperties.SetHelpText(
            PetPreviewImage,
            $"{PreviewStateText.Text}；{_previewPlayer.PlaybackDescription}");
        AutomationProperties.SetHelpText(
            PetRuntimePreview,
            $"{PreviewStateText.Text}；{_previewPlayer.PlaybackDescription}");
    }

    private void UpdatePreviewButtons()
    {
        var availableKeys = _state.PreviewFrames
            .Select(preview => preview.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var button in GetPreviewButtons())
        {
            var key = button.Tag as string;
            button.IsEnabled = key is not null && availableKeys.Contains(key);
            button.IsChecked = key is not null &&
                string.Equals(key, _previewPlayer.CurrentKey, StringComparison.OrdinalIgnoreCase);
        }
    }

    private IEnumerable<ToggleButton> GetPreviewButtons() =>
        DailyActionsPanel.Children.OfType<ToggleButton>()
            .Concat(TaskActionsPanel.Children.OfType<ToggleButton>())
            .Concat(ViewActionsPanel.Children.OfType<ToggleButton>());

    private void PreviewPlayer_PlaybackStateChanged(object? sender, EventArgs e) =>
        UpdatePlaybackPresentation();

    private void PreviewPlayer_SelectionChanged(object? sender, EventArgs e) =>
        UpdatePreviewButtons();

    private void PrimaryAction_Click(object sender, RoutedEventArgs e) =>
        PrimaryActionRequested?.Invoke(this, _state.PrimaryAction);

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void BackToGallery_Click(object sender, RoutedEventArgs e)
    {
        ShowGallery();
        BackToGalleryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void GalleryPanel_EntryRequested(object? sender, PetGalleryEntry entry) =>
        PetRequested?.Invoke(this, entry);

    private void GalleryPanel_RefreshRequested(object? sender, EventArgs e) =>
        GalleryRefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OpenCodex_Click(object sender, RoutedEventArgs e) =>
        OpenCodexRequested?.Invoke(this, EventArgs.Empty);

    private void OpenTheme_Click(object sender, RoutedEventArgs e) =>
        RecommendedThemeRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyTheme_Click(object sender, RoutedEventArgs e) =>
        ApplyRecommendedThemeRequested?.Invoke(this, EventArgs.Empty);

    private void Uninstall_Click(object sender, RoutedEventArgs e) =>
        UninstallRequested?.Invoke(this, EventArgs.Empty);

    private void AcknowledgeSelection_Click(object sender, RoutedEventArgs e) =>
        SelectionAcknowledgementRequested?.Invoke(this, EventArgs.Empty);

    private void RestoreBackup_Click(object sender, RoutedEventArgs e) =>
        RestoreBackupRequested?.Invoke(this, EventArgs.Empty);

    private void PreviewState_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string key })
        {
            _previewPlayer.Select(key);
            UpdatePreviewButtons();
        }
    }

    private void PetCenterView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _previewPlayer.SetActive(_pageActive && IsVisible && DetailPanel.IsVisible);

    private void PetCenterView_Unloaded(object sender, RoutedEventArgs e) =>
        _previewPlayer.SetActive(false);

    private void PetCenterView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DetailPanel.Visibility != Visibility.Visible ||
            !double.IsFinite(e.NewSize.Width) || e.NewSize.Width <= 0 ||
            !double.IsFinite(e.NewSize.Height) || e.NewSize.Height <= 0)
        {
            return;
        }

        ApplyDetailLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void ApplyDetailLayout(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0 ||
            !double.IsFinite(height) || height <= 0)
        {
            return;
        }

        var viewportHeight = FindViewportHeight();
        if (viewportHeight > 0)
        {
            height = Math.Min(height, viewportHeight);
        }

        var narrow = width < 720;
        var medium = !narrow && width < 1100;
        WorkspaceLeftColumn.Width = narrow
            ? new GridLength(2, GridUnitType.Star)
            : medium
                ? new GridLength(1.02, GridUnitType.Star)
                : new GridLength(1.10, GridUnitType.Star);
        WorkspaceGapColumn.Width = new GridLength(narrow ? 12 : medium ? 14 : 16);
        WorkspaceRightColumn.Width = narrow
            ? new GridLength(3, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(PreviewStage, 0);
        Grid.SetRow(PreviewStage, 0);
        Grid.SetColumn(ControlPanelHost, 2);
        Grid.SetRow(ControlPanelHost, 0);
        Grid.SetRowSpan(ControlPanelHost, 1);
        ControlPanelHost.Margin = new Thickness(0);
        ControlPanelHost.Padding = new Thickness(narrow ? 10 : medium ? 12 : 16);
        ControlPanelHost.BorderThickness = new Thickness(1);
        ControlPanelHost.MinHeight = 0;
        WorkspaceSurface.MinHeight = 0;
        WorkspaceSurface.MaxHeight = 650;
        WorkspaceSurface.Height = Math.Clamp(
            height - Math.Max(64, PageHeader.ActualHeight) - 14,
            narrow ? 500 : 540,
            650);
        WorkspaceSurface.Margin = new Thickness(0, 14, 0, 0);
        WorkspaceSurface.Padding = new Thickness(0);
        PreviewStage.MinHeight = 0;
        PreviewStage.Padding = new Thickness(narrow ? 10 : medium ? 12 : 14);
        UpdatePreviewStageBounds();
    }

    private double FindViewportHeight()
    {
        DependencyObject? current = this;
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
        {
            if (current is ScrollViewer { ViewportHeight: > 0 } scrollViewer)
            {
                return scrollViewer.ViewportHeight;
            }
        }

        return 0;
    }

    private void WorkspaceSurface_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdatePreviewStageBounds();

    private void UpdatePreviewStageBounds()
    {
        if (_disposed)
        {
            return;
        }

        var stageWidth = PreviewStage.ActualWidth > 0
            ? PreviewStage.ActualWidth
            : Math.Max(200, WorkspaceSurface.ActualWidth * 0.45);
        var surfaceHeight = WorkspaceSurface.ActualHeight > 0
            ? WorkspaceSurface.ActualHeight
            : 600;
        var hostWidth = Math.Max(170, stageWidth - PreviewStage.Padding.Left -
            PreviewStage.Padding.Right - 8);
        var heightBudget = Math.Max(200, surfaceHeight - WorkspaceSurface.Padding.Top -
            WorkspaceSurface.Padding.Bottom - PreviewStage.Padding.Top -
            PreviewStage.Padding.Bottom - 42);
        var hostHeight = stageWidth < 300
            ? Math.Clamp(Math.Min(heightBudget, hostWidth * 1.05), 190, 270)
            : Math.Clamp(heightBudget, 240, 560);
        var displayWidth = Math.Max(150, Math.Min(400, hostWidth - 20));
        var displayHeight = Math.Max(170, Math.Min(400, hostHeight - 20));
        PreviewImageHost.MinHeight = 0;
        PreviewImageHost.Height = hostHeight;
        PetPreviewImage.MaxWidth = displayWidth;
        PetPreviewImage.MaxHeight = displayHeight;
        PetRuntimePreview.Width = displayWidth;
        PetRuntimePreview.Height = displayHeight;
        PetRuntimePreview.MaxWidth = displayWidth;
        PetRuntimePreview.MaxHeight = displayHeight;
        _previewPlayer.SetDisplayBounds(displayWidth, displayHeight);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _previewPlayer.PlaybackStateChanged -= PreviewPlayer_PlaybackStateChanged;
        _previewPlayer.SelectionChanged -= PreviewPlayer_SelectionChanged;
        GalleryPanel.EntryRequested -= GalleryPanel_EntryRequested;
        GalleryPanel.RefreshRequested -= GalleryPanel_RefreshRequested;
        _previewPlayer.Dispose();
        GC.SuppressFinalize(this);
    }
}
