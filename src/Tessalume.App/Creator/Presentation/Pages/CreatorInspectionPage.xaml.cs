using System.Windows;
using System.Windows.Controls;

namespace Tessalume.App.Creator;

public partial class CreatorInspectionPage : UserControl
{
    public CreatorInspectionPage() => InitializeComponent();

    internal void Render(CreatorCenterViewModel viewModel)
    {
        InspectionProjectCard.Visibility = viewModel.HasSelectedProject
            ? Visibility.Visible
            : Visibility.Collapsed;
        InspectionEmptyPanel.Visibility = viewModel.HasSelectedProject
            ? Visibility.Collapsed
            : Visibility.Visible;
        RepairPromptButton.Visibility = viewModel.SelectedProject?.CanCopyRepairPrompt == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public event RoutedEventHandler? RevalidateProjectRequested;
    public event RoutedEventHandler? CopyRepairPromptRequested;
    public event RoutedEventHandler? OpenHealthPathRequested;

    private void RevalidateProject_Click(object sender, RoutedEventArgs e) => RevalidateProjectRequested?.Invoke(this, e);
    private void CopyRepairPrompt_Click(object sender, RoutedEventArgs e) => CopyRepairPromptRequested?.Invoke(this, e);
    private void OpenHealthPath_Click(object sender, RoutedEventArgs e) => OpenHealthPathRequested?.Invoke(sender, e);
}
