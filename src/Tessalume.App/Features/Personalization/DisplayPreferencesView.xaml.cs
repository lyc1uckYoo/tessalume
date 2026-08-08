using System.Windows.Controls;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization;

public sealed class DisplayPreferencesChangedEventArgs(ThemeDisplayPreferences preferences) : EventArgs
{
    public ThemeDisplayPreferences Preferences { get; } = preferences.Normalize();
}

public partial class DisplayPreferencesView : UserControl
{
    private bool _updating;

    public DisplayPreferencesView()
    {
        InitializeComponent();
    }

    public event EventHandler<DisplayPreferencesChangedEventArgs>? PreferencesChanged;

    public void Render(ThemeDisplayPreferences preferences, bool enabled)
    {
        var normalized = preferences.Normalize();
        _updating = true;
        MotionComboBox.SelectedValue = normalized.MotionIntensity;
        TextScaleComboBox.SelectedValue = normalized.TextScale;
        DensityComboBox.SelectedValue = normalized.Density;
        IsEnabled = enabled;
        _updating = false;
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating ||
            MotionComboBox.SelectedValue is not string motion ||
            TextScaleComboBox.SelectedValue is not string textScale ||
            DensityComboBox.SelectedValue is not string density) return;
        PreferencesChanged?.Invoke(
            this,
            new DisplayPreferencesChangedEventArgs(new ThemeDisplayPreferences
            {
                MotionIntensity = motion,
                TextScale = textScale,
                Density = density,
            }));
    }
}
