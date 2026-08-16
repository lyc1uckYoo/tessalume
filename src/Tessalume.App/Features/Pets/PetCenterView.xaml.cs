using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tessalume.App.Features.Pets;

public partial class PetCenterView : UserControl, IDisposable
{
    private readonly PetPreviewPlayer _previewPlayer;
    private PetCenterPresentationState _state = new();
    private bool _disposed;

    public PetCenterView()
    {
        InitializeComponent();
        _previewPlayer = new PetPreviewPlayer(PetPreviewImage, PreviewStateText);
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

    internal void Render(PetCenterPresentationState state)
    {
        _state = state;
        HeaderStatusText.Text = state.StatusTitle;
        InstallationStatusTitle.Text = state.StatusTitle;
        InstallationStatusDetail.Text = state.StatusDetail;
        ProductVersionText.Text = state.ProductVersion;
        ProtocolText.Text = state.ProtocolSummary;
        AuthorLicenseText.Text = $"{state.Author} · {state.LicenseSummary}";
        InstallLocationText.Text = state.InstallLocation;
        InstallLocationText.ToolTip = state.InstallLocation;
        PrimaryActionButton.Content = state.PrimaryActionText;
        PrimaryActionButton.IsEnabled = state.PrimaryActionEnabled && !state.IsBusy;
        UninstallButton.Visibility = state.CanUninstall ? Visibility.Visible : Visibility.Collapsed;
        AcknowledgeSelectionButton.Visibility = state.CanAcknowledgeSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        RestoreBackupButton.Visibility = state.CanRestoreBackup
            ? Visibility.Visible
            : Visibility.Collapsed;
        RestoreBackupButton.ToolTip = state.LatestBackupLabel;
        _previewPlayer.Configure(state.PreviewFrames);
        PreviewFallbackText.Visibility = state.PreviewFrames.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RenderStatusPalette(state.Status);
    }

    internal void SetPageActive(bool active) => _previewPlayer.SetActive(active && IsVisible);

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
        HeaderStatusDot.Fill = brush;
        HeaderStatusText.Foreground = brush;
        InstallationStatusDot.Fill = brush;
        InstallationStatusHalo.Fill = brush;
    }

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
        if (sender is Button { Tag: string key })
        {
            _previewPlayer.Select(key);
        }
    }

    private void PetCenterView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _previewPlayer.SetActive(IsVisible);

    private void PetCenterView_Unloaded(object sender, RoutedEventArgs e) =>
        _previewPlayer.SetActive(false);

    private void PetCenterView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width > 0 && e.NewSize.Width < 720;
        PreviewColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(324);
        HeroGapColumn.Width = compact ? new GridLength(0) : new GridLength(22);
        DetailsColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        CompactGapRow.Height = compact ? new GridLength(16) : new GridLength(0);
        Grid.SetColumn(PreviewCard, 0);
        Grid.SetRow(PreviewCard, compact ? 2 : 0);
        Grid.SetColumn(DetailsPanel, compact ? 0 : 2);
        Grid.SetRow(DetailsPanel, 0);
        Grid.SetColumn(MetadataPanel, compact ? 0 : 2);
        Grid.SetRow(MetadataPanel, compact ? 3 : 2);
        MetadataPanel.Margin = compact ? new Thickness(0, 13, 0, 0) : new Thickness(0, 13, 0, 0);
        PreviewCard.MinHeight = compact ? 322 : 346;

        var lowerCompact = e.NewSize.Width > 0 && e.NewSize.Width < 760;
        if (lowerCompact)
        {
            Grid.SetColumn(InstallGuideCard, 0);
            Grid.SetRow(InstallGuideCard, 0);
            Grid.SetColumnSpan(InstallGuideCard, 3);
            Grid.SetColumn(CompanionThemeCard, 0);
            Grid.SetRow(CompanionThemeCard, 1);
            Grid.SetColumnSpan(CompanionThemeCard, 3);
            CompanionThemeCard.Margin = new Thickness(0, 12, 0, 0);
            if (LowerLayout.RowDefinitions.Count == 0)
            {
                LowerLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                LowerLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
        }
        else
        {
            Grid.SetColumn(InstallGuideCard, 0);
            Grid.SetRow(InstallGuideCard, 0);
            Grid.SetColumnSpan(InstallGuideCard, 1);
            Grid.SetColumn(CompanionThemeCard, 2);
            Grid.SetRow(CompanionThemeCard, 0);
            Grid.SetColumnSpan(CompanionThemeCard, 1);
            CompanionThemeCard.Margin = new Thickness(0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _previewPlayer.Dispose();
        GC.SuppressFinalize(this);
    }
}
