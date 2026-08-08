using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessalume.App.Creator;

internal static partial class TestSuite
{
    static Task<int> RenderCreatorCenterSnapshotsAsync(
        string workspacePath,
        string lightSnapshotPath,
        string detailSnapshotPath,
        string darkSnapshotPath,
        string? promptSnapshotPath = null,
        string? releaseSnapshotPath = null,
        string? acceptanceSnapshotPath = null)
    {
        var portableRoot = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-creator-snapshot-{Guid.NewGuid():N}");
        var themes = Path.Combine(portableRoot, "themes");
        var data = Path.Combine(portableRoot, "data");
        Directory.CreateDirectory(themes);
        Directory.CreateDirectory(data);
        Exception? failure = null;

        using (var preferences = new UiPreferencesStore(data))
        {
            preferences.SaveAsync(new UiPreferences
            {
                OnboardingCompleted = true,
                RecentCreatorWorkspaces =
                [
                    new CreatorWorkspaceRecord
                    {
                        DirectoryPath = Path.GetFullPath(workspacePath),
                        DisplayName = "视觉验收工作区",
                        LastOpenedAt = DateTimeOffset.UtcNow,
                    },
                ],
            }).GetAwaiter().GetResult();
        }

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MainWindow? window = null;

            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    window = new MainWindow(new PortableLayout(portableRoot, themes, data));
                    var initialize = typeof(MainWindow).GetMethod(
                        "EnsureMainUiInitialized",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(nameof(MainWindow), "EnsureMainUiInitialized");
                    initialize.Invoke(window, null);

                    window.ThemeLibraryPage.Visibility = Visibility.Collapsed;
                    window.InfoPage.Visibility = Visibility.Visible;
                    window.ImportInfoPanel.Visibility = Visibility.Collapsed;
                    window.SettingsInfoPanel.Visibility = Visibility.Collapsed;
                    window.DiagnosticsPage.Visibility = Visibility.Collapsed;
                    window.AboutPage.Visibility = Visibility.Collapsed;
                    window.CreatorCenter.Visibility = Visibility.Visible;
                    if (window.CreatorCenter.DataContext is not CreatorCenterViewModel viewModel)
                    {
                        throw new InvalidOperationException(
                            "Creator Center view model is unavailable for visual verification.");
                    }
                    await viewModel.ActivateAsync();

                    if (
                        viewModel.Projects.Count == 0 ||
                        viewModel.SelectedProject is null)
                    {
                        throw new InvalidOperationException(
                            "Creator Center did not load a project for visual verification.");
                    }

                    ArrangeMainSurface(window);
                    if (window.InfoScroll.ScrollableHeight <= 0)
                    {
                        throw new InvalidOperationException(
                            "Creator Center content must remain vertically scrollable.");
                    }
                    if (window.CreatorCenter.ProjectDetailCard.Visibility != Visibility.Visible)
                    {
                        throw new InvalidOperationException(
                            "Creator Center project details were not rendered.");
                    }

                    ResetCreatorScroll(window);
                    SaveWindowContent(window, lightSnapshotPath);

                    if (!string.IsNullOrWhiteSpace(promptSnapshotPath))
                    {
                        window.CreatorCenter.TogglePromptEditorButton.RaiseEvent(
                            new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        ResetCreatorScroll(window);
                        if (window.CreatorCenter.CreatorPromptEditor.Visibility != Visibility.Visible ||
                            window.CreatorCenter.PromptCharacterNameBox.ActualWidth <= 0)
                        {
                            throw new InvalidOperationException(
                                "The creator prompt editor must expand into a visible input surface.");
                        }
                        SaveWindowContent(window, promptSnapshotPath);
                        window.CreatorCenter.TogglePromptEditorButton.RaiseEvent(
                            new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    }

                    window.CreatorCenter.NavigateTo(CreatorCenterRoute.Workflow);
                    ResetCreatorScroll(window);
                    SaveWindowContent(window, detailSnapshotPath);

                    var applyTheme = typeof(MainWindow).GetMethod(
                        "ApplyStudioTheme",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(nameof(MainWindow), "ApplyStudioTheme");
                    applyTheme.Invoke(window, [true]);
                    window.CreatorCenter.NavigateTo(CreatorCenterRoute.Inspection);
                    ResetCreatorScroll(window);
                    SaveWindowContent(window, darkSnapshotPath);

                    if (!string.IsNullOrWhiteSpace(releaseSnapshotPath))
                    {
                        if (!string.IsNullOrWhiteSpace(acceptanceSnapshotPath))
                        {
                            window.CreatorCenter.NavigateTo(CreatorCenterRoute.Acceptance);
                            ResetCreatorScroll(window);
                            SaveWindowContent(window, acceptanceSnapshotPath);
                        }
                        window.CreatorCenter.NavigateTo(CreatorCenterRoute.Release);
                        ResetCreatorScroll(window);
                        SaveWindowContent(window, releaseSnapshotPath);
                    }
                }
                catch (Exception exception)
                {
                    failure = exception is System.Reflection.TargetInvocationException invocation
                        ? invocation.InnerException ?? invocation
                        : exception;
                }
                finally
                {
                    if (window is not null)
                    {
                        await window.DisposeAsync();
                    }
                    application.Shutdown();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            if (failure is not null)
            {
                Console.Error.WriteLine(failure);
                return Task.FromResult(1);
            }
            Console.WriteLine($"Creator Center light snapshot: {Path.GetFullPath(lightSnapshotPath)}");
            Console.WriteLine($"Creator Center detail snapshot: {Path.GetFullPath(detailSnapshotPath)}");
            Console.WriteLine($"Creator Center dark snapshot: {Path.GetFullPath(darkSnapshotPath)}");
            if (!string.IsNullOrWhiteSpace(promptSnapshotPath))
            {
                Console.WriteLine($"Creator prompt snapshot: {Path.GetFullPath(promptSnapshotPath)}");
            }
            if (!string.IsNullOrWhiteSpace(releaseSnapshotPath))
            {
                Console.WriteLine($"Creator release snapshot: {Path.GetFullPath(releaseSnapshotPath)}");
            }
            if (!string.IsNullOrWhiteSpace(acceptanceSnapshotPath))
            {
                Console.WriteLine($"Creator acceptance snapshot: {Path.GetFullPath(acceptanceSnapshotPath)}");
            }
            return Task.FromResult(0);
        }
        finally
        {
            if (Directory.Exists(portableRoot)) Directory.Delete(portableRoot, recursive: true);
        }
    }

    private static void ArrangeMainSurface(MainWindow window, Size? requestedSize = null)
    {
        var surface = window.Content as FrameworkElement
            ?? throw new InvalidOperationException("Main window content is unavailable.");
        var size = requestedSize ?? new Size(1280, 820);
        surface.Measure(size);
        surface.Arrange(new Rect(size));
        surface.UpdateLayout();
    }

    private static void ResetCreatorScroll(MainWindow window)
    {
        window.InfoScroll.ScrollToVerticalOffset(0);
        ArrangeMainSurface(window);
        window.InfoScroll.UpdateLayout();
        window.InfoScroll.ScrollToVerticalOffset(0);
        ArrangeMainSurface(window);
    }

    private static void SaveWindowContent(MainWindow window, string path)
    {
        var surface = window.Content as FrameworkElement
            ?? throw new InvalidOperationException("Main window content is unavailable.");
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var width = Math.Max(1, (int)Math.Ceiling(surface.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(surface.ActualHeight));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(fullPath);
        encoder.Save(stream);
    }
}
