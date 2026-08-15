using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Models;

internal static partial class TestSuite
{
    static Task<int> RenderArtworkSnapshotsAsync(
        string basicSnapshotPath,
        string compositionSnapshotPath,
        string effectsSnapshotPath,
        string? sidebarDarkSnapshotPath = null)
    {
        var repositoryRoot = FindRepositoryRoot();
        var data = Path.Combine(
            repositoryRoot,
            "artifacts",
            "qa",
            $".artwork-snapshot-data-{Guid.NewGuid():N}");
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
                    window = new MainWindow(new PortableLayout(
                        repositoryRoot,
                        Path.Combine(repositoryRoot, "themes"),
                        data));
                    InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                    await AttachArtworkSnapshotThemeAsync(window, repositoryRoot);
                    await InvokeMainWindowTaskAsync(
                        window,
                        "ResolveThemeArtworkDefaultsAsync",
                        CancellationToken.None);
                    InvokeMainWindowMethod(window, "NavigateTo", AppRoute.ArtworkStudio);
                    InvokeMainWindowMethod(window, "SetArtworkConnectionMonitoring", false);
                    window.ArtworkWorkbench.SetConnectionState(false);
                    InvokeMainWindowMethod(window, "UpdateArtworkWorkbenchContext");

                    await RenderArtworkTargetAsync(
                        window,
                        ArtworkRegion.Hero,
                        darkMode: false,
                        window.ArtworkWorkbench.Inspector.BasicGroupButton,
                        basicSnapshotPath,
                        inspector =>
                        {
                            inspector.BrightnessSlider.Value = 108;
                            inspector.OpacitySlider.Value = 96;
                            inspector.BasicAdvancedExpander.IsExpanded = true;
                            inspector.ContrastSlider.Value = 106;
                            inspector.SaturationSlider.Value = 112;
                        });
                    await RenderArtworkTargetAsync(
                        window,
                        ArtworkRegion.Sidebar,
                        darkMode: false,
                        window.ArtworkWorkbench.Inspector.CompositionGroupButton,
                        compositionSnapshotPath,
                        inspector =>
                        {
                            inspector.ZoomSlider.Value = 112;
                            inspector.OffsetXSlider.Value = 24;
                            inspector.OffsetYSlider.Value = -18;
                        });
                    if (!string.IsNullOrWhiteSpace(sidebarDarkSnapshotPath))
                    {
                        await RenderArtworkTargetAsync(
                            window,
                            ArtworkRegion.Sidebar,
                            darkMode: true,
                            window.ArtworkWorkbench.Inspector.CompositionGroupButton,
                            sidebarDarkSnapshotPath,
                            _ => { });
                    }
                    await RenderArtworkTargetAsync(
                        window,
                        ArtworkRegion.Chat,
                        darkMode: true,
                        window.ArtworkWorkbench.Inspector.EffectsGroupButton,
                        effectsSnapshotPath,
                        inspector =>
                        {
                            inspector.OverlayOpacitySlider.Value = 22;
                            inspector.ReadabilityCheckBox.IsChecked = true;
                            inspector.EffectsAdvancedExpander.IsExpanded = true;
                            inspector.GrayscaleSlider.Value = 5;
                            inspector.HueRotationSlider.Value = 8;
                            inspector.BlurSlider.Value = 0.5;
                            inspector.GradientStrengthSlider.Value = 36;
                            inspector.VignetteSlider.Value = 24;
                            inspector.BlendModeComboBox.SelectedIndex = 4;
                        });
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
            Console.WriteLine($"Artwork hero/light snapshot: {Path.GetFullPath(basicSnapshotPath)}");
            Console.WriteLine(
                $"Artwork sidebar/light snapshot: {Path.GetFullPath(compositionSnapshotPath)}");
            if (!string.IsNullOrWhiteSpace(sidebarDarkSnapshotPath))
            {
                Console.WriteLine(
                    $"Artwork sidebar/dark snapshot: {Path.GetFullPath(sidebarDarkSnapshotPath)}");
            }
            Console.WriteLine($"Artwork chat/dark snapshot: {Path.GetFullPath(effectsSnapshotPath)}");
            return Task.FromResult(0);
        }
        finally
        {
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }

    private static async Task AttachArtworkSnapshotThemeAsync(
        MainWindow window,
        string repositoryRoot)
    {
        var themeRoot = Path.Combine(repositoryRoot, "themes", "cartethyia.gale-tide-crown");
        var loaded = await new ThemePackageLoader().LoadAsync(themeRoot);
        Ensure(loaded.Validation.IsValid, FormatIssues(loaded.Validation));
        var package = loaded.Package
            ?? throw new InvalidOperationException("The flagship theme package did not load.");
        var model = new ThemeCardModel(
            new ThemeCatalogItem(themeRoot, package, loaded.Validation),
            loadPreview: false)
        {
            // Screenshot fixtures exercise the fully editable local-preview path
            // with the critical 355% Cartethyia sidebar recommendation.
            // Keeping the theme selected but unapplied makes the result independent
            // of any Codex instance that happens to be running on the developer PC.
            IsApplied = false,
            IsSelected = true,
        };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var themes = (ObservableCollection<ThemeCardModel>)(
            typeof(MainWindow).GetField("_themes", flags)?.GetValue(window)
            ?? throw new MissingFieldException(nameof(MainWindow), "_themes"));
        themes.Add(model);
        typeof(MainWindow).GetField("_selectedTheme", flags)?.SetValue(window, model);
    }

    private static async Task InvokeMainWindowTaskAsync(
        MainWindow window,
        string name,
        params object[] arguments)
    {
        var method = typeof(MainWindow).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), name);
        var task = method.Invoke(window, arguments) as Task
            ?? throw new InvalidOperationException($"MainWindow.{name} did not return a Task.");
        await task;
    }

    private static async Task RenderArtworkTargetAsync(
        MainWindow window,
        ArtworkRegion region,
        bool darkMode,
        Button groupButton,
        string snapshotPath,
        Action<Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation.ArtworkInspectorView>
            configure)
    {
        InvokeMainWindowMethod(window, "ApplyStudioTheme", darkMode);
        var workbench = window.ArtworkWorkbench;
        var modeButton = darkMode ? workbench.DarkModeButton : workbench.LightModeButton;
        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var regionButton = region switch
        {
            ArtworkRegion.Sidebar => workbench.SidebarRegionButton,
            ArtworkRegion.Chat => workbench.ChatRegionButton,
            _ => workbench.HeroRegionButton,
        };
        regionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        groupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        configure(workbench.Inspector);

        ArrangeMainSurface(window, new Size(1600, 900));
        window.InfoScroll.ScrollToTop();
        ArrangeMainSurface(window, new Size(1600, 900));
        await WaitForArtworkPreviewAsync(window);
        ArrangeMainSurface(window, new Size(1600, 900));
        Ensure(workbench.PreviewCanvas.ArtworkImage.Source is not null,
            $"The real {region}/{(darkMode ? "dark" : "light")} theme image did not load.");
        Ensure(workbench.PreviewCanvas.LoadingOverlay.Visibility != Visibility.Visible,
            "The screenshot must not capture a pending preview load.");
        foreach (var action in new[]
                 {
                     workbench.CompareButton,
                     workbench.Inspector.ResetParameterButton,
                     workbench.Inspector.ResetGroupButton,
                     workbench.Inspector.ResetRegionButton,
                     workbench.Inspector.ChooseImageButton,
                     workbench.Inspector.ClearImageButton,
                 })
        {
            EnsureButtonContentFits(action, 30, "Artwork Workbench 3.0");
        }
        SaveWindowContent(window, snapshotPath);
    }

    private static async Task WaitForArtworkPreviewAsync(MainWindow window)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (window.ArtworkWorkbench.PreviewCanvas.ArtworkImage.Source is not null &&
                window.ArtworkWorkbench.PreviewCanvas.LoadingOverlay.Visibility != Visibility.Visible)
            {
                // Let the latest pixel-effect revision and layout debounce settle.
                await Task.Delay(320);
                await Dispatcher.Yield(DispatcherPriority.Background);
                var status = window.ArtworkWorkbench.SyncStatusText.Text;
                if (status is not ("正在加载" or "正在保存" or "等待应用" or "正在应用"))
                {
                    return;
                }
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("The Artwork Workbench preview did not become ready in 30 seconds.");
    }
}
