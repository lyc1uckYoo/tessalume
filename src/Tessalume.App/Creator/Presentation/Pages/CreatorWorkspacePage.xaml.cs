using System.Windows;
using System.Windows.Controls;

namespace Tessalume.App.Creator;

public partial class CreatorWorkspacePage : UserControl
{
    private bool _isSynchronizingSelection;

    public CreatorWorkspacePage() => InitializeComponent();

    internal CreatorWorkspaceItemViewModel? SelectedWorkspace =>
        WorkspaceList.SelectedItem as CreatorWorkspaceItemViewModel;

    internal ThemeProjectItemViewModel? SelectedProject =>
        ProjectList.SelectedItem as ThemeProjectItemViewModel;

    internal void Render(CreatorCenterViewModel viewModel)
    {
        _isSynchronizingSelection = true;
        try
        {
            var hasWorkspaces = viewModel.Workspaces.Count > 0;
            WorkspaceEmptyPanel.Visibility = hasWorkspaces ? Visibility.Collapsed : Visibility.Visible;
            WorkspaceList.Visibility = hasWorkspaces ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceList.SelectedItem = viewModel.SelectedWorkspace;
            WorkspaceVersionPanel.Visibility = viewModel.HasSelectedWorkspace
                ? Visibility.Visible
                : Visibility.Collapsed;

            ProjectLoadingPanel.Visibility = viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            ProjectList.Visibility = !viewModel.IsBusy && viewModel.HasProjects
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProjectStatePanel.Visibility = !viewModel.IsBusy && !viewModel.HasProjects
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProjectList.SelectedItem = viewModel.SelectedProject;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    public event RoutedEventHandler? CreateWorkspaceRequested;
    public event RoutedEventHandler? OpenWorkspaceRequested;
    public event RoutedEventHandler? CopyTemplateRequested;
    public event RoutedEventHandler? RelocateWorkspaceRequested;
    public event RoutedEventHandler? RemoveWorkspaceRequested;
    public event RoutedEventHandler? RefreshWorkspaceRequested;
    public event RoutedEventHandler? OpenWorkspaceFolderRequested;
    public event RoutedEventHandler? UpgradeWorkspaceRequested;
    public event SelectionChangedEventHandler? WorkspaceSelectionChanged;
    public event SelectionChangedEventHandler? ProjectSelectionChanged;
    public event RoutedEventHandler? TogglePromptEditorRequested;
    public event RoutedEventHandler? CopyPromptRequested;
    public event RoutedEventHandler? ResetPromptRequested;
    public event RoutedEventHandler? PromptChanged;

    private void CreateWorkspace_Click(object sender, RoutedEventArgs e) => CreateWorkspaceRequested?.Invoke(this, e);
    private void OpenWorkspace_Click(object sender, RoutedEventArgs e) => OpenWorkspaceRequested?.Invoke(this, e);
    private void CopyTemplate_Click(object sender, RoutedEventArgs e) => CopyTemplateRequested?.Invoke(this, e);
    private void RelocateWorkspace_Click(object sender, RoutedEventArgs e) => RelocateWorkspaceRequested?.Invoke(this, e);
    private void RemoveWorkspace_Click(object sender, RoutedEventArgs e) => RemoveWorkspaceRequested?.Invoke(this, e);
    private void RefreshWorkspace_Click(object sender, RoutedEventArgs e) => RefreshWorkspaceRequested?.Invoke(this, e);
    private void OpenWorkspaceFolder_Click(object sender, RoutedEventArgs e) => OpenWorkspaceFolderRequested?.Invoke(this, e);
    private void UpgradeWorkspace_Click(object sender, RoutedEventArgs e) => UpgradeWorkspaceRequested?.Invoke(this, e);
    private void WorkspaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingSelection) WorkspaceSelectionChanged?.Invoke(this, e);
    }

    private void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingSelection) ProjectSelectionChanged?.Invoke(this, e);
    }
    private void Prompt_ToggleEditorRequested(object sender, RoutedEventArgs e) => TogglePromptEditorRequested?.Invoke(this, e);
    private void Prompt_CopyRequested(object sender, RoutedEventArgs e) => CopyPromptRequested?.Invoke(this, e);
    private void Prompt_ResetRequested(object sender, RoutedEventArgs e) => ResetPromptRequested?.Invoke(this, e);
    private void Prompt_Changed(object sender, RoutedEventArgs e) => PromptChanged?.Invoke(this, e);
}
