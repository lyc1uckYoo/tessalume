using System.Windows;
using System.Windows.Controls;

namespace Tessalume.App.Creator;

public partial class CreatorWorkflowPage : UserControl
{
    public CreatorWorkflowPage() => InitializeComponent();

    internal void Render(CreatorCenterViewModel viewModel)
    {
        ProjectDetailCard.Visibility = viewModel.HasSelectedProject
            ? Visibility.Visible
            : Visibility.Collapsed;
        WorkflowEmptyPanel.Visibility = viewModel.HasSelectedProject
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public event RoutedEventHandler? OpenProjectFolderRequested;
    public event RoutedEventHandler? RevalidateProjectRequested;
    public event RoutedEventHandler? ApplyProjectRequested;
    public event RoutedEventHandler? ToggleCodexModeRequested;

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e) => OpenProjectFolderRequested?.Invoke(this, e);
    private void RevalidateProject_Click(object sender, RoutedEventArgs e) => RevalidateProjectRequested?.Invoke(this, e);
    private void ApplyProject_Click(object sender, RoutedEventArgs e) => ApplyProjectRequested?.Invoke(this, e);
    private void ToggleCodexMode_Click(object sender, RoutedEventArgs e) => ToggleCodexModeRequested?.Invoke(this, e);
}
