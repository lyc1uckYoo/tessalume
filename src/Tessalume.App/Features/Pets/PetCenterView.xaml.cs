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
    }

    internal event EventHandler? RefreshRequested;

    internal event EventHandler<PetCenterAction>? PrimaryActionRequested;

    internal event EventHandler? CopyCommandRequested;

    internal event EventHandler? OpenCodexRequested;

    internal event EventHandler? RecommendedThemeRequested;

    internal event EventHandler? ApplyRecommendedThemeRequested;

    internal event EventHandler? UninstallRequested;

    internal event EventHandler? SelectionAcknowledgementRequested;

    internal event EventHandler? RestoreBackupRequested;

    internal PetPreviewPlayer PreviewPlayer => _previewPlayer;

    internal void Render(PetCenterPresentationState state)
    {
        _state = state;
        InstallationStatusTitle.Text = state.StatusTitle;
        InstallationStatusDetail.Text = state.StatusDetail;
        ProductVersionText.Text = state.ProductVersion;
        ProtocolText.Text = state.ProtocolSummary;
        AuthorLicenseText.Text = $"{state.Author} · {FormatLicenseSummary(state.LicenseSummary)}";
        InstallLocationText.Text = $"仅管理 {state.InstallLocation}";
        InstallLocationText.ToolTip = state.InstallLocation;
        PrimaryActionButton.Content = state.PrimaryActionText;
        PrimaryActionButton.IsEnabled = state.PrimaryActionEnabled && !state.IsBusy;
        CopyCommandButton.IsEnabled = !state.IsBusy;
        RefreshButton.IsEnabled = !state.IsBusy;
        UninstallButton.IsEnabled = state.CanUninstall && !state.IsBusy;
        RestoreBackupButton.IsEnabled = state.CanRestoreBackup && !state.IsBusy;
        AcknowledgeSelectionButton.Visibility = state.CanAcknowledgeSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        AcknowledgeSelectionButton.IsEnabled = !state.IsBusy;
        ActivationGuidePanel.Visibility = state.Status == PetCenterStatus.AwaitingCodexSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        RestoreBackupButton.ToolTip = state.LatestBackupLabel ?? "当前没有可恢复备份";
        _previewPlayer.Configure(state.PreviewFrames);
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

    internal void SetPageActive(bool active)
    {
        _pageActive = active;
        _previewPlayer.SetActive(active && IsVisible);
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

    private void CopyCommand_Click(object sender, RoutedEventArgs e) =>
        CopyCommandRequested?.Invoke(this, EventArgs.Empty);

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
        _previewPlayer.SetActive(_pageActive && IsVisible);

    private void PetCenterView_Unloaded(object sender, RoutedEventArgs e) =>
        _previewPlayer.SetActive(false);

    private void PetCenterView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!double.IsFinite(e.NewSize.Width) || e.NewSize.Width <= 0)
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
        ControlPanelHost.Padding = new Thickness(narrow ? 10 : medium ? 12 : 14, 0, 0, 0);
        ControlPanelHost.BorderThickness = new Thickness(1, 0, 0, 0);
        ControlPanelHost.MinHeight = 0;
        WorkspaceSurface.MinHeight = 0;
        WorkspaceSurface.MaxHeight = 690;
        WorkspaceSurface.Margin = new Thickness(0, 8, 0, 0);
        WorkspaceSurface.Padding = new Thickness(narrow ? 9 : medium ? 10 : 12);
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
        _previewPlayer.Dispose();
        GC.SuppressFinalize(this);
    }
}
