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
        _previewPlayer = new PetPreviewPlayer(PetPreviewImage, PreviewStateText);
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
        DetailPageSubtitleText.Text = state.IsDevelopmentPreview
            ? "检查候选动作、角色资料与项目状态。"
            : "检查完整动作、角色资料与安装状态。";
        PetDisplayNameText.Text = state.DisplayName;
        PetDescriptionText.Text = state.Description;
        PetSourceBadgeText.Text = state.SourceBadge;
        var sourceBrushKey = state.IsDevelopmentPreview ? "Accent" : "Teal";
        var sourceSurfaceKey = state.IsDevelopmentPreview ? "AccentSoft" : "TealSoft";
        PetSourceBadge.Background = (Brush)FindResource(sourceSurfaceKey);
        PetSourceBadgeText.Foreground = (Brush)FindResource(sourceBrushKey);
        InstallationStatusTitle.Text = state.StatusTitle;
        InstallationStatusDetail.Text = state.StatusDetail;
        ProductVersionText.Text = state.ProductVersion;
        ProtocolText.Text = state.ProtocolSummary;
        AuthorLicenseText.Text = $"{state.Author} · {FormatLicenseSummary(state.LicenseSummary)}";
        InstallLocationText.Text = FormatLocation(
            state.InstallLocation,
            state.IsDevelopmentPreview);
        InstallLocationText.ToolTip = state.IsDevelopmentPreview
            ? state.InstallLocation
            : $"仅管理 {state.InstallLocation}";
        LocationLabelText.Text = state.LocationLabel;
        PrimaryActionButton.Content = state.PrimaryActionText;
        PrimaryActionButton.IsEnabled = state.PrimaryActionEnabled && !state.IsBusy;
        PrimaryActionGroup.Visibility = state.ShowPrimaryAction
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshButton.IsEnabled = !state.IsBusy;
        RefreshButton.Content = state.IsDevelopmentPreview ? "↻  立即刷新" : "↻  重新检查";
        UninstallButton.IsEnabled = state.CanUninstall && !state.IsBusy;
        UninstallButton.Visibility = state.ShowInstallationManagement
            ? Visibility.Visible
            : Visibility.Collapsed;
        RestoreBackupButton.IsEnabled = state.CanRestoreBackup && !state.IsBusy;
        RestoreBackupButton.Visibility = state.ShowInstallationManagement
            ? Visibility.Visible
            : Visibility.Collapsed;
        ManagementSectionTitle.Text = state.IsDevelopmentPreview ? "开发预览" : "管理工具";
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
        _previewPlayer.Configure(state.PreviewFrames);
        _previewPlayer.SetActive(_pageActive && IsVisible && DetailPanel.IsVisible);
        UpdatePreviewButtons();
        UpdatePlaybackPresentation();
        RenderStatusPalette(state.Status);
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

    private static string FormatLocation(string location, bool developmentPreview)
    {
        const int maximumVisibleCharacters = 34;
        var fullText = developmentPreview ? location : $"仅管理 {location}";
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
            PetCenterStatus.DevelopmentPreview => "Accent",
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
        PreviewFallbackText.Visibility = PetPreviewImage.Source is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomationProperties.SetHelpText(
            PetPreviewImage,
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
            !double.IsFinite(e.NewSize.Width) || e.NewSize.Width <= 0)
        {
            return;
        }

        var narrow = e.NewSize.Width < 720;
        var medium = !narrow && e.NewSize.Width < 1100;
        WorkspaceLeftColumn.Width = narrow
            ? new GridLength(2, GridUnitType.Star)
            : medium
                ? new GridLength(1.05, GridUnitType.Star)
                : new GridLength(1.15, GridUnitType.Star);
        WorkspaceGapColumn.Width = new GridLength(narrow ? 12 : medium ? 14 : 18);
        WorkspaceRightColumn.Width = narrow
            ? new GridLength(3, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(PreviewStage, 0);
        Grid.SetRow(PreviewStage, 0);
        Grid.SetColumn(ControlPanelHost, 2);
        Grid.SetRow(ControlPanelHost, 0);
        Grid.SetRowSpan(ControlPanelHost, 1);
        ControlPanelHost.Margin = new Thickness(0);
        ControlPanelHost.Padding = new Thickness(narrow ? 9 : medium ? 10 : 12);
        ControlPanelHost.BorderThickness = new Thickness(1);
        ControlPanelHost.MinHeight = 0;
        WorkspaceSurface.MinHeight = 0;
        WorkspaceSurface.MaxHeight = 650;
        WorkspaceSurface.Margin = new Thickness(0, 10, 0, 0);
        WorkspaceSurface.Padding = new Thickness(0);
        PreviewStage.MinHeight = 0;
        PreviewStage.Padding = new Thickness(narrow ? 9 : medium ? 10 : 12);
        UpdatePreviewStageBounds();
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
        var hostHeight = Math.Clamp(Math.Min(heightBudget, hostWidth * 1.08), 190, 580);
        PreviewImageHost.MinHeight = 0;
        PreviewImageHost.Height = hostHeight;
        PetPreviewImage.MaxWidth = Math.Max(150, hostWidth - 8);
        PetPreviewImage.MaxHeight = Math.Max(170, hostHeight - 8);
        _previewPlayer.SetDisplayBounds(
            Math.Max(150, hostWidth - 8),
            Math.Max(170, hostHeight - 8));
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
