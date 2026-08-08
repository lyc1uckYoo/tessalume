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
    public event RoutedEventHandler? PreviousThemeRequested;
    public event RoutedEventHandler? NextThemeRequested;
    public event RoutedEventHandler? ColorModeRequested;
    public event RoutedEventHandler? ArtworkStudioRequested;

    public DisplayPreferencesView DisplayPreferencesPage => DisplayPreferencesControl;

    public ExperienceProfilesView ExperienceProfilesPage => ExperienceProfilesControl;

    public void RenderContext(
        string themeName,
        string themeState,
        string mode,
        bool themeNavigationEnabled)
    {
        CurrentThemeNameText.Text = themeName;
        ThemeStateText.Text = themeState;
        ModeText.Text = mode;
        PreviousThemeButton.IsEnabled = themeNavigationEnabled;
        NextThemeButton.IsEnabled = themeNavigationEnabled;
    }

    private void DisplayPreferences_Changed(
        object? sender,
        DisplayPreferencesChangedEventArgs e) => PreferencesChanged?.Invoke(this, e);

    private void ExperienceProfiles_SaveRequested(object? sender, EventArgs e) =>
        SaveProfileRequested?.Invoke(this, e);

    private void ExperienceProfiles_ApplyRequested(object? sender, EventArgs e) =>
        ApplyProfileRequested?.Invoke(this, e);

    private void ExperienceProfiles_DeleteRequested(object? sender, EventArgs e) =>
        DeleteProfileRequested?.Invoke(this, e);

    private void PreviousTheme_Click(object sender, RoutedEventArgs e) =>
        PreviousThemeRequested?.Invoke(this, e);

    private void NextTheme_Click(object sender, RoutedEventArgs e) =>
        NextThemeRequested?.Invoke(this, e);

    private void ColorMode_Click(object sender, RoutedEventArgs e) =>
        ColorModeRequested?.Invoke(this, e);

    private void ArtworkStudio_Click(object sender, RoutedEventArgs e) =>
        ArtworkStudioRequested?.Invoke(this, e);
}
