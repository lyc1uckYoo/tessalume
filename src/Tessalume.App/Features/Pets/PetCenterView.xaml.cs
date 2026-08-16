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
        var compact = e.NewSize.Width > 0 && e.NewSize.Width < 900;
        if (compact)
        {
            WorkspaceLeftColumn.Width = new GridLength(1, GridUnitType.Star);
            WorkspaceGapColumn.Width = new GridLength(0);
            WorkspaceRightColumn.Width = new GridLength(0);
            Grid.SetColumn(PreviewStage, 0);
            Grid.SetRow(PreviewStage, 0);
            Grid.SetColumn(ControlPanelHost, 0);
            Grid.SetRow(ControlPanelHost, 1);
            Grid.SetRowSpan(ControlPanelHost, 1);
            ControlPanelHost.Margin = new Thickness(0, 12, 0, 0);
            ControlPanelHost.Padding = new Thickness(0, 12, 0, 0);
            ControlPanelHost.BorderThickness = new Thickness(0, 1, 0, 0);
            ControlPanelHost.MinHeight = 0;
            ActionSelector.Margin = new Thickness(20, 15, 0, 0);
            WorkspaceSurface.MinHeight = 0;
            WorkspaceSurface.Margin = new Thickness(0, 12, 0, 0);
            WorkspaceSurface.Padding = new Thickness(12);
            PreviewStage.MinHeight = 300;
            PreviewStage.Padding = new Thickness(14);
            PreviewImageHost.MinHeight = 0;
            PreviewImageHost.Height = 232;
            PetPreviewImage.MaxWidth = 360;
            PetPreviewImage.MaxHeight = 220;
            _previewPlayer.SetDisplayBounds(Math.Max(240, e.NewSize.Width - 72), 220);
        }
        else
        {
            WorkspaceLeftColumn.Width = new GridLength(3, GridUnitType.Star);
            WorkspaceGapColumn.Width = new GridLength(24);
            WorkspaceRightColumn.Width = new GridLength(2, GridUnitType.Star);
            Grid.SetColumn(PreviewStage, 0);
            Grid.SetRow(PreviewStage, 0);
            Grid.SetColumn(ControlPanelHost, 2);
            Grid.SetRow(ControlPanelHost, 0);
            Grid.SetRowSpan(ControlPanelHost, 1);
            ControlPanelHost.Margin = new Thickness(0);
            ControlPanelHost.Padding = new Thickness(22, 0, 0, 0);
            ControlPanelHost.BorderThickness = new Thickness(1, 0, 0, 0);
            ControlPanelHost.MinHeight = 0;
            ActionSelector.Margin = new Thickness(20, 17, 0, 0);
            WorkspaceSurface.MinHeight = 680;
            WorkspaceSurface.Margin = new Thickness(0, 18, 0, 0);
            WorkspaceSurface.Padding = new Thickness(22);
            PreviewStage.MinHeight = 650;
            PreviewStage.Padding = new Thickness(18);
            PreviewImageHost.MinHeight = 520;
            PreviewImageHost.Height = double.NaN;
            PetPreviewImage.MaxWidth = 620;
            PetPreviewImage.MaxHeight = 560;
            _previewPlayer.SetDisplayBounds(Math.Max(420, e.NewSize.Width * 0.58 - 80), 560);
        }
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
