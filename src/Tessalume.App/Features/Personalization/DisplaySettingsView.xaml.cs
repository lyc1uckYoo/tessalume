using System.Windows;
using System.Windows.Controls;

namespace Tessalume.App.Features.Personalization;

public partial class DisplaySettingsView : UserControl
{
    public DisplaySettingsView()
    {
        InitializeComponent();
    }

    public event EventHandler<DisplayPreferencesChangedEventArgs>? PreferencesChanged;
    public event RoutedEventHandler? ArtworkStudioRequested;

    public DisplayPreferencesView DisplayPreferencesPage => DisplayPreferencesControl;

    private void DisplayPreferences_Changed(
        object? sender,
        DisplayPreferencesChangedEventArgs e) => PreferencesChanged?.Invoke(this, e);

    private void ArtworkStudio_Click(object sender, RoutedEventArgs e) =>
        ArtworkStudioRequested?.Invoke(this, e);
}
