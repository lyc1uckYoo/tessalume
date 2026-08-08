using System.Windows;
using System.Windows.Controls;

namespace Tessalume.App.Creator;

public partial class CreatorReleasePage : UserControl
{
    public CreatorReleasePage() => InitializeComponent();

    internal void Render(CreatorCenterViewModel viewModel)
    {
        ReleaseProjectCard.Visibility = viewModel.HasSelectedProject
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReleaseEmptyPanel.Visibility = viewModel.HasSelectedProject
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public event RoutedEventHandler? OpenProjectFolderRequested;
    public event RoutedEventHandler? ExportProjectRequested;

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e) => OpenProjectFolderRequested?.Invoke(this, e);
    private void ExportProject_Click(object sender, RoutedEventArgs e) => ExportProjectRequested?.Invoke(this, e);
}
