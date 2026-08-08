using System.Windows;
using System.Windows.Controls;

namespace Tessalume.App.Creator;

public partial class CreatorPromptView : UserControl
{
    public CreatorPromptView() => InitializeComponent();

    public event RoutedEventHandler? ToggleEditorRequested;
    public event RoutedEventHandler? CopyRequested;
    public event RoutedEventHandler? ResetRequested;
    public event RoutedEventHandler? PromptChanged;

    private void ToggleEditor_Click(object sender, RoutedEventArgs e) => ToggleEditorRequested?.Invoke(this, e);
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, e);
    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke(this, e);
    private void PromptField_Changed(object sender, TextChangedEventArgs e) => PromptChanged?.Invoke(this, e);
    private void PromptToggle_Changed(object sender, RoutedEventArgs e) => PromptChanged?.Invoke(this, e);
}
