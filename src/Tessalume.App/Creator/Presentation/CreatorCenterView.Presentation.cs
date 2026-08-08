using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Tessalume.App.Creator;

public partial class CreatorCenterView
{
    internal TextBlock CreatorPromptStatusText => PromptView.CreatorPromptStatusText;
    internal TextBlock CreatorPromptText => PromptView.CreatorPromptText;
    internal Button TogglePromptEditorButton => PromptView.TogglePromptEditorButton;
    internal Button CopyPromptButton => PromptView.CopyPromptButton;
    internal Border CreatorPromptEditor => PromptView.CreatorPromptEditor;
    internal TextBox PromptWorkNameBox => PromptView.PromptWorkNameBox;
    internal TextBox PromptCharacterNameBox => PromptView.PromptCharacterNameBox;
    internal TextBox PromptVisualDirectionBox => PromptView.PromptVisualDirectionBox;
    internal TextBox PromptSpecialRequirementsBox => PromptView.PromptSpecialRequirementsBox;
    internal ToggleButton PromptReferenceCheckBox => PromptView.PromptReferenceCheckBox;
    internal Border ProjectDetailCard => WorkflowPage.ProjectDetailCard;

    private void WorkspaceRoute_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(CreatorCenterRoute.Workspace);

    private void WorkflowRoute_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(CreatorCenterRoute.Workflow);

    private void InspectionRoute_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(CreatorCenterRoute.Inspection);

    private void AcceptanceRoute_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(CreatorCenterRoute.Acceptance);

    private void ReleaseRoute_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(CreatorCenterRoute.Release);

    internal void NavigateTo(CreatorCenterRoute route)
    {
        _currentRoute = route;
        WorkspacePage.Visibility = route == CreatorCenterRoute.Workspace
            ? Visibility.Visible
            : Visibility.Collapsed;
        WorkflowPage.Visibility = route == CreatorCenterRoute.Workflow
            ? Visibility.Visible
            : Visibility.Collapsed;
        InspectionPage.Visibility = route == CreatorCenterRoute.Inspection
            ? Visibility.Visible
            : Visibility.Collapsed;
        AcceptancePage.Visibility = route == CreatorCenterRoute.Acceptance
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReleasePage.Visibility = route == CreatorCenterRoute.Release
            ? Visibility.Visible
            : Visibility.Collapsed;

        WorkspaceRouteButton.Tag = route == CreatorCenterRoute.Workspace ? "active" : null;
        WorkflowRouteButton.Tag = route == CreatorCenterRoute.Workflow ? "active" : null;
        InspectionRouteButton.Tag = route == CreatorCenterRoute.Inspection ? "active" : null;
        AcceptanceRouteButton.Tag = route == CreatorCenterRoute.Acceptance ? "active" : null;
        ReleaseRouteButton.Tag = route == CreatorCenterRoute.Release ? "active" : null;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RenderState();

    private void RenderState()
    {
        if (_viewModel is null) return;
        WorkspacePage.Render(_viewModel);
        WorkflowPage.Render(_viewModel);
        InspectionPage.Render(_viewModel);
        AcceptancePage.Render(_viewModel);
        ReleasePage.Render(_viewModel);
        NavigateTo(_currentRoute);
    }

    private async Task RunOperationAsync(Func<Task> operation, string errorTitle)
    {
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            Win32Exception)
        {
            ShowMessage(errorTitle, exception.Message, ProductDialogKind.Error);
        }
        finally
        {
            RenderState();
        }
    }

    private async void RunAcceptance_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await RunOperationAsync(
            () => _viewModel.RunAcceptanceAsync(),
            "无法完成运行验收");
    }

    private static void OpenDirectory(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    private void TryOpenDirectory(string path)
    {
        try
        {
            OpenDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            ShowMessage("无法打开目录", exception.Message, ProductDialogKind.Error);
        }
    }

    private void ShowMessage(string title, string message, ProductDialogKind kind) =>
        ProductDialogWindow.ShowMessage(GetOwner(), title, message, kind, IsDarkMode());

    private Window GetOwner() => Window.GetWindow(this)
        ?? throw new InvalidOperationException("创作项目中心尚未连接到主窗口。");

    private bool IsDarkMode() => _isDarkMode?.Invoke() == true;

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GiB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MiB",
        >= 1024L => $"{bytes / 1024d:0.0} KiB",
        _ => $"{bytes} B",
    };
}
