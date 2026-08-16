using System.Reflection;
using System.Windows.Threading;

internal static partial class TestSuite
{
    static Task<int> RenderPersonalizationSnapshotsAsync(
        string lightSnapshotPath,
        string darkSnapshotPath,
        string? compactSnapshotPath = null)
    {
        var portableRoot = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-personalization-snapshot-{Guid.NewGuid():N}");
        var themes = Path.Combine(portableRoot, "themes");
        var data = Path.Combine(portableRoot, "data");
        Directory.CreateDirectory(themes);
        Directory.CreateDirectory(data);
        Exception? failure = null;

        using (var preferences = new UiPreferencesStore(data))
        {
            preferences.SaveAsync(new UiPreferences { OnboardingCompleted = true })
                .GetAwaiter()
                .GetResult();
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
                    InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                    window.DisplayPreferencesButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    await Task.Delay(240);
                    CompleteInfoPageTransition(window);
                    window.DisplayPreferencesPage.Render(new ThemeDisplayPreferences
                    {
                        MotionIntensity = "reduced",
                        TextScale = "large",
                        Density = "spacious",
                    }, enabled: true);
                    RenderPersonalizationProfile(window, lightSnapshotPath, darkMode: false);
                    RenderPersonalizationProfile(window, darkSnapshotPath, darkMode: true);
                    if (!string.IsNullOrWhiteSpace(compactSnapshotPath))
                    {
                        RenderPersonalizationProfile(
                            window,
                            compactSnapshotPath,
                            darkMode: false,
                            new Size(1080, 720));
                    }
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
            Console.WriteLine($"Personalization light snapshot: {Path.GetFullPath(lightSnapshotPath)}");
            Console.WriteLine($"Personalization dark snapshot: {Path.GetFullPath(darkSnapshotPath)}");
            if (!string.IsNullOrWhiteSpace(compactSnapshotPath))
            {
                Console.WriteLine(
                    $"Personalization compact snapshot: {Path.GetFullPath(compactSnapshotPath)}");
            }
            return Task.FromResult(0);
        }
        finally
        {
            if (Directory.Exists(portableRoot)) Directory.Delete(portableRoot, recursive: true);
        }
    }

    private static void RenderPersonalizationProfile(
        MainWindow window,
        string snapshotPath,
        bool darkMode,
        Size? size = null)
    {
        InvokeMainWindowMethod(window, "ApplyStudioTheme", darkMode);
        ArrangeMainSurface(window, size);
        window.InfoScroll.ScrollToTop();
        ArrangeMainSurface(window, size);
        SaveWindowContent(window, snapshotPath);
    }
}
