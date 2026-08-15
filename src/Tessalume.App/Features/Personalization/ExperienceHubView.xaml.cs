using System.Windows;
using System.Windows.Controls;

namespace Tessalume.App.Features.Personalization;

public partial class ExperienceHubView : UserControl
{
    public ExperienceHubView()
    {
        InitializeComponent();
    }

    public event EventHandler<DisplayPreferencesChangedEventArgs>? PreferencesChanged;
    public event EventHandler? SaveProfileRequested;
    public event EventHandler? ApplyProfileRequested;
    public event EventHandler? DeleteProfileRequested;
    public event RoutedEventHandler? ArtworkStudioRequested;

    public DisplayPreferencesView DisplayPreferencesPage => DisplayPreferencesControl;

    public ExperienceProfilesView ExperienceProfilesPage => ExperienceProfilesControl;

    private void DisplayPreferences_Changed(
        object? sender,
        DisplayPreferencesChangedEventArgs e) => PreferencesChanged?.Invoke(this, e);

    private void ExperienceProfiles_SaveRequested(object? sender, EventArgs e) =>
        SaveProfileRequested?.Invoke(this, e);

    private void ExperienceProfiles_ApplyRequested(object? sender, EventArgs e) =>
        ApplyProfileRequested?.Invoke(this, e);

    private void ExperienceProfiles_DeleteRequested(object? sender, EventArgs e) =>
        DeleteProfileRequested?.Invoke(this, e);

    private void ArtworkStudio_Click(object sender, RoutedEventArgs e) =>
        ArtworkStudioRequested?.Invoke(this, e);
}
