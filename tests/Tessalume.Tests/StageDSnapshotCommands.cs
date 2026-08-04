using System.Reflection;
using System.Windows.Threading;

internal static partial class TestSuite
{
    static Task<int> RenderStageDSnapshotsAsync(
        string aboutSnapshotPath,
        string diagnosticsSnapshotPath,
        string diagnosticsDarkSnapshotPath)
    {
        var portableRoot = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-stage-d-snapshot-{Guid.NewGuid():N}");
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
        new StudioStateStore(data).SaveAsync(new StudioState
        {
            Port = 9340,
            Enabled = false,
            RuntimeContractVersion = ThemeRuntime.ContractVersion,
            LastSuccessfulApplyAt = DateTimeOffset.Now.AddMinutes(-12),
            LastFailureStage = ThemeRuntimeFailureStage.ThemeScriptFailed,
            LastFailureMessage = "示例主题脚本未完成挂载；Codex 页面已经统一恢复默认外观。",
            LastFailureAt = DateTimeOffset.Now.AddMinutes(-3),
        }).GetAwaiter().GetResult();

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
                    window.ThemeLibraryPage.Visibility = Visibility.Collapsed;
                    window.InfoPage.Visibility = Visibility.Visible;
                    ShowOnlyInfoPanel(window, window.AboutInfoPanel);
                    ArrangeMainSurface(window);
                    window.InfoScroll.ScrollToEnd();
                    ArrangeMainSurface(window);
                    SaveWindowContent(window, aboutSnapshotPath);

                    ShowOnlyInfoPanel(window, window.DiagnosticsInfoPanel);
                    var refresh = typeof(MainWindow).GetMethod(
                        "RefreshDiagnosticsAsync",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(nameof(MainWindow), "RefreshDiagnosticsAsync");
                    await ((Task?)refresh.Invoke(window, null)
                        ?? throw new InvalidOperationException("Diagnostics refresh did not return a task."));
                    window.InfoScroll.ScrollToTop();
                    ArrangeMainSurface(window);
                    Ensure(window.InfoScroll.ScrollableHeight > 0,
                        "The compatibility diagnostics page must remain vertically scrollable.");
                    SaveWindowContent(window, diagnosticsSnapshotPath);

                    InvokeMainWindowMethod(window, "ApplyStudioTheme", true);
                    window.InfoScroll.ScrollToTop();
                    ArrangeMainSurface(window);
                    SaveWindowContent(window, diagnosticsDarkSnapshotPath);
                }
                catch (Exception exception)
                {
                    failure = exception is TargetInvocationException invocation
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
            Console.WriteLine($"About and Data snapshot: {Path.GetFullPath(aboutSnapshotPath)}");
            Console.WriteLine($"Compatibility diagnostics snapshot: {Path.GetFullPath(diagnosticsSnapshotPath)}");
            Console.WriteLine($"Compatibility diagnostics dark snapshot: {Path.GetFullPath(diagnosticsDarkSnapshotPath)}");
            return Task.FromResult(0);
        }
        finally
        {
            if (Directory.Exists(portableRoot)) Directory.Delete(portableRoot, recursive: true);
        }
    }

    private static void ShowOnlyInfoPanel(MainWindow window, FrameworkElement visiblePanel)
    {
        foreach (var panel in new FrameworkElement[]
                 {
                     window.ImportInfoPanel,
                     window.SettingsInfoPanel,
                     window.DiagnosticsInfoPanel,
                     window.AboutInfoPanel,
                     window.CreatorCenter,
                 })
        {
            panel.Visibility = ReferenceEquals(panel, visiblePanel)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private static void InvokeMainWindowMethod(MainWindow window, string name, params object[] arguments)
    {
        var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), name);
        method.Invoke(window, arguments);
    }
}
