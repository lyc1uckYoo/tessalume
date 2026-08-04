using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;

internal static partial class TestSuite
{
    static Task<int> RenderArtworkSnapshotsAsync(
        string basicSnapshotPath,
        string compositionSnapshotPath,
        string effectsSnapshotPath)
    {
        var portableRoot = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-artwork-snapshot-{Guid.NewGuid():N}");
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
                    window.ThemeLibraryPage.Visibility = Visibility.Collapsed;
                    window.InfoPage.Visibility = Visibility.Visible;
                    ShowOnlyInfoPanel(window, window.SettingsInfoPanel);
                    window.VisualAdjustmentEditor.IsEnabled = true;
                    window.VisualThemeNameText.Text = "示例主题 · 当前修改会立即显示在 Codex 中";
                    window.VisualPresetNameBox.Text = "柔和背景";

                    RenderGroup(window, window.VisualBasicGroupButton, basicSnapshotPath, darkMode: false);
                    RenderGroup(window, window.VisualCompositionGroupButton, compositionSnapshotPath, darkMode: false);
                    RenderGroup(window, window.VisualEffectsGroupButton, effectsSnapshotPath, darkMode: true);
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
            Console.WriteLine($"Artwork basic snapshot: {Path.GetFullPath(basicSnapshotPath)}");
            Console.WriteLine($"Artwork composition snapshot: {Path.GetFullPath(compositionSnapshotPath)}");
            Console.WriteLine($"Artwork effects snapshot: {Path.GetFullPath(effectsSnapshotPath)}");
            return Task.FromResult(0);
        }
        finally
        {
            if (Directory.Exists(portableRoot)) Directory.Delete(portableRoot, recursive: true);
        }
    }

    private static void RenderGroup(
        MainWindow window,
        Button groupButton,
        string snapshotPath,
        bool darkMode)
    {
        InvokeMainWindowMethod(window, "ApplyStudioTheme", darkMode);
        window.VisualUndoButton.IsEnabled = true;
        window.VisualRedoButton.IsEnabled = true;
        window.VisualOriginalPreviewButton.IsEnabled = true;
        window.CopyVisualModeButton.IsEnabled = true;
        window.SaveVisualPresetButton.IsEnabled = true;
        groupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ArrangeMainSurface(window);
        window.InfoScroll.ScrollToVerticalOffset(126);
        ArrangeMainSurface(window);
        SaveWindowContent(window, snapshotPath);
    }
}
