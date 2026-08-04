using System.Reflection;
using System.Windows.Threading;

internal static partial class TestSuite
{
    static Task<int> RenderThemeLibrarySnapshotsAsync(
        string librarySnapshotPath,
        string detailSnapshotPath,
        string detailDarkSnapshotPath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var data = Path.Combine(
            repositoryRoot,
            "docs",
            "qa",
            $".theme-library-snapshot-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(data);
        Exception? failure = null;

        using (var preferences = new UiPreferencesStore(data))
        {
            preferences.SaveAsync(new UiPreferences
            {
                OnboardingCompleted = true,
                ThemeLibrarySort = ThemeLibraryState.DefaultSort,
            }).GetAwaiter().GetResult();
        }

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MainWindow? window = null;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    window = new MainWindow(new PortableLayout(
                        repositoryRoot,
                        Path.Combine(repositoryRoot, "themes"),
                        data));
                    InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                    var reload = typeof(MainWindow).GetMethod(
                        "ReloadThemesAsync",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(nameof(MainWindow), "ReloadThemesAsync");
                    await ((Task?)reload.Invoke(window, new object?[] { null, true })
                        ?? throw new InvalidOperationException("Theme library reload did not return a task."));

                    ArrangeMainSurface(window);
                    Ensure(window.ThemeItems.ActualHeight > 0 && window.ThemeSortComboBox.ActualWidth > 0,
                        "The theme library cards and sort control must be visible in the product layout.");
                    SaveWindowContent(window, librarySnapshotPath);

                    window.ThemeDetailsButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    ArrangeMainSurface(window);
                    Ensure(window.ThemeDetailPanel.Visibility == Visibility.Visible &&
                           window.ThemeDetailPanel.ActualWidth > 0 &&
                           window.ThemeDetailPanel.LightPreviewImage.ActualWidth > 0 &&
                           window.ThemeDetailPanel.DarkPreviewImage.ActualWidth > 0,
                        "The large light/dark theme detail preview must occupy a visible surface.");
                    SaveWindowContent(window, detailSnapshotPath);

                    InvokeMainWindowMethod(window, "ApplyStudioTheme", true);
                    ArrangeMainSurface(window);
                    SaveWindowContent(window, detailDarkSnapshotPath);
                }
                catch (Exception exception)
                {
                    failure = exception is TargetInvocationException invocation
                        ? invocation.InnerException ?? invocation
                        : exception;
                }
                finally
                {
                    if (window is not null) await window.DisposeAsync();
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
            Console.WriteLine($"Theme library snapshot: {Path.GetFullPath(librarySnapshotPath)}");
            Console.WriteLine($"Theme detail snapshot: {Path.GetFullPath(detailSnapshotPath)}");
            Console.WriteLine($"Theme detail dark snapshot: {Path.GetFullPath(detailDarkSnapshotPath)}");
            return Task.FromResult(0);
        }
        finally
        {
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }
}
