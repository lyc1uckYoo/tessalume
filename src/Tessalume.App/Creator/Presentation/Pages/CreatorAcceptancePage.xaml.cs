using System.Windows;
using System.Windows.Controls;

namespace Tessalume.App.Creator;

public partial class CreatorAcceptancePage : UserControl
{
    public CreatorAcceptancePage() => InitializeComponent();

    internal void Render(CreatorCenterViewModel viewModel)
    {
        AcceptanceProjectCard.Visibility = viewModel.HasSelectedProject
            ? Visibility.Visible
            : Visibility.Collapsed;
        AcceptanceEmptyPanel.Visibility = viewModel.HasSelectedProject
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public event RoutedEventHandler? RunAcceptanceRequested;

    private void RunAcceptance_Click(object sender, RoutedEventArgs e) =>
        RunAcceptanceRequested?.Invoke(this, e);
}
