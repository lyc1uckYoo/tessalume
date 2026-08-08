using System.Collections;
using System.Windows;
using System.Windows.Controls;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization;

public partial class ExperienceProfilesView : UserControl
{
    public ExperienceProfilesView()
    {
        InitializeComponent();
        UpdateActions();
    }

    public event EventHandler? SaveRequested;
    public event EventHandler? ApplyRequested;
    public event EventHandler? DeleteRequested;

    public ThemeExperiencePreset? SelectedProfile =>
        ProfileComboBox.SelectedItem as ThemeExperiencePreset;

    public string RequestedName => ProfileNameBox.Text.Trim();

    public void Bind(IEnumerable profiles) => ProfileComboBox.ItemsSource = profiles;

    public void Select(ThemeExperiencePreset? profile)
    {
        ProfileComboBox.SelectedItem = profile;
        ProfileNameBox.Clear();
        UpdateActions();
    }

    public void SetSaveEnabled(bool enabled) => SaveButton.IsEnabled = enabled;

    private void Save_Click(object sender, RoutedEventArgs e) =>
        SaveRequested?.Invoke(this, EventArgs.Empty);

    private void Apply_Click(object sender, RoutedEventArgs e) =>
        ApplyRequested?.Invoke(this, EventArgs.Empty);

    private void Delete_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(this, EventArgs.Empty);

    private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();

    private void UpdateActions()
    {
        var selected = SelectedProfile;
        ApplyButton.IsEnabled = selected is not null;
        DeleteButton.IsEnabled = selected is not null;
        ProfileSummaryText.Text = selected is null
            ? "选择方案后显示包含的主题与模式"
            : $"主题 {selected.ThemeId} · {(selected.DarkMode ? "暗色" : "亮色")} · 包含两套图片和显示偏好";
    }
}
