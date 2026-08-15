using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Personalization;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;
using Tessalume.Core.Runtime;

internal static partial class TestSuite
{
    static async Task ArtworkThemeDefaultsProjectPublishedPlacementsAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var loader = new ThemePackageLoader();
        var store = new ArtworkThemeDefaultsStore();
        var projectedSlots = 0;
        foreach (var themeId in new[]
                 {
                     "xin.moonfox-sovereign",
                     "mornye.first-star-observatory",
                     "cartethyia.gale-tide-crown",
                 })
        {
            var loaded = await loader.LoadAsync(Path.Combine(repositoryRoot, "themes", themeId));
            Ensure(loaded.Validation.IsValid, FormatIssues(loaded.Validation));
            var package = loaded.Package
                ?? throw new InvalidOperationException($"Theme package did not load: {themeId}.");
            var defaults = await store.LoadAsync(package);
            Ensure(defaults.IsExact, defaults.Diagnostic ?? $"Defaults were not exact: {themeId}.");
            var resolution = ThemeArtworkSettingsResolver.Resolve(defaults.Defaults, null);
            foreach (var (region, _, asset, slot, resolved) in EnumerateArtworkSlots(
                         defaults.Defaults,
                         resolution))
            {
                var target = region switch
                {
                    "sidebar" => new ArtworkSize(260d, 800d),
                    "chat" => new ArtworkSize(1440d, 900d),
                    _ => new ArtworkSize(1440d, 420d),
                };
                var path = package.AssetPaths[asset];
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    new Uri(path),
                    System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                var spec = ThemeArtworkPlacementParser.Parse(slot.Placement);
                var projection = ArtworkPlacementMapper.Project(
                    spec,
                    new ArtworkSize(frame.PixelWidth, frame.PixelHeight),
                    target);
                Ensure(projection.RenderedImage.IsValid &&
                       projection.SizeCss == spec.SizeCss &&
                       projection.PositionCss == spec.PositionCss &&
                       resolved.Adjustment.CompositionMode == ThemeArtworkCompositionMode.Theme &&
                       resolved.Adjustment.Zoom == 100d &&
                       resolved.Adjustment.OffsetX == 0d &&
                       resolved.Adjustment.OffsetY == 0d,
                    $"{themeId} {region}/{asset} must project its final defaults without a second transform.");
                projectedSlots++;
            }
        }
        Ensure(projectedSlots == 18,
            $"Flagship, Mornye, and Cartethyia must project all 18 light/dark region slots; found {projectedSlots}.");

        var metric = new ThemeArtworkSurfaceMetric(
            false,
            "hero",
            "::before",
            null,
            null,
            "not-on-route");
        var snapshot = new ThemeArtworkSurfaceMetricsSnapshot(
            "cartethyia.gale-tide-crown",
            true,
            "task",
            1.5d,
            1366d,
            768d,
            metric,
            metric with { Region = "sidebar", Pseudo = "::after" },
            metric with { Region = "chat" })
        {
            ArtworkCompositionProtocolVersion =
                ArtworkSurfaceMetricsProbeGate.SupportedCompositionProtocolVersion,
        };
        Ensure(ArtworkSurfaceMetricsProbeGate.Evaluate(
                   snapshot,
                   12,
                   12,
                   snapshot.ThemeId,
                   snapshot.ThemeId,
                   editingDarkMode: true) == ArtworkSurfaceMetricsProbeDisposition.Apply &&
               ArtworkSurfaceMetricsProbeGate.Evaluate(
                   snapshot,
                   11,
                   12,
                   snapshot.ThemeId,
                   snapshot.ThemeId,
                   editingDarkMode: true) == ArtworkSurfaceMetricsProbeDisposition.IgnoreStale &&
               ArtworkSurfaceMetricsProbeGate.Evaluate(
                   snapshot,
                   12,
                   12,
                   snapshot.ThemeId,
                   snapshot.ThemeId,
                   editingDarkMode: false) == ArtworkSurfaceMetricsProbeDisposition.ClearCurrent,
            "Live metrics must apply only to the current probe/theme/mode, while stale results can never clear newer geometry.");
        Ensure(ArtworkSurfaceMetricsProbeGate.Evaluate(
                   snapshot with { ArtworkCompositionProtocolVersion = 0 },
                   12,
                   12,
                   snapshot.ThemeId,
                   snapshot.ThemeId,
                   editingDarkMode: true) == ArtworkSurfaceMetricsProbeDisposition.ClearCurrent,
            "A previous renderer composition protocol must never be presented as exact live artwork geometry.");
    }

    static Task ArtworkWorkbenchUndoPreservesExternalDisplayAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-workbench-display-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            ArtworkWorkbenchView? view = null;
            try
            {
                view = new ArtworkWorkbenchView();
                view.Configure(new PersonalImageStore(root), Array.Empty<ThemeArtworkPreset>());
                const string themeId = "workbench.display-history";
                var package = CreateWorkbenchProbePackage(root, themeId);
                var initial = new ThemeVisualSettings
                {
                    Display = new ThemeDisplayPreferences
                    {
                        MotionIntensity = "full",
                        TextScale = "standard",
                        Density = "comfortable",
                    },
                }.Normalize();
                view.SetContext(new ArtworkWorkbenchContext(
                    themeId,
                    "Display 历史隔离探针",
                    package,
                    initial,
                    ArtworkColorMode.Light,
                    IsApplied: false,
                    IsCodexConnected: false));

                const string localImage = "personalization/images/history-probe.png";
                Ensure(view.TrySetCustomImagePath(
                           themeId,
                           ArtworkColorMode.Light,
                           ArtworkRegion.Hero,
                           localImage) &&
                       view.CurrentSettings.Light.Hero.CustomImagePath == localImage &&
                       view.UndoButton.IsEnabled,
                    "A view-level artwork edit must create an undoable history entry.");

                var externalDisplay = new ThemeDisplayPreferences
                {
                    MotionIntensity = "off",
                    TextScale = "large",
                    Density = "spacious",
                }.Normalize();
                var externallyUpdated = view.CurrentSettings with { Display = externalDisplay };
                view.SetContext(new ArtworkWorkbenchContext(
                    themeId,
                    "Display 历史隔离探针",
                    package,
                    externallyUpdated,
                    ArtworkColorMode.Light,
                    IsApplied: false,
                    IsCodexConnected: false));

                view.Undo();
                Ensure(view.CurrentSettings.Light.Hero.CustomImagePath is null,
                    "Undo must restore the artwork state from before the local image edit.");
                Ensure(view.CurrentSettings.Display == externalDisplay,
                    "Undo must not roll back Display preferences changed by another personalization surface.");

                view.Redo();
                Ensure(view.CurrentSettings.Light.Hero.CustomImagePath == localImage,
                    "Redo must restore the artwork edit after a Display-only external update.");
                Ensure(view.CurrentSettings.Display == externalDisplay,
                    "Redo must retain the latest external Display preferences.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                view?.Dispose();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            if (failure is not null)
            {
                throw new InvalidOperationException(
                    "The workbench view rolled an external Display update into artwork history.",
                    failure);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    static Task ArtworkStudioRouteLayoutsStayReachableAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-artwork-route-layout-{Guid.NewGuid():N}");
        var themes = Path.Combine(root, "themes");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(themes);
        Directory.CreateDirectory(data);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(new PortableLayout(root, themes, data));
                InvokePrivateMainWindowMethod(window, "EnsureMainUiInitialized");
                InvokePrivateMainWindowMethod(window, "NavigateTo", AppRoute.ArtworkStudio);
                window.ArtworkWorkbench.SetContext(new ArtworkWorkbenchContext(
                    "workbench.route-layout",
                    "完整路由布局探针",
                    CreateWorkbenchProbePackage(root, "workbench.route-layout"),
                    new ThemeVisualSettings(),
                    ArtworkColorMode.Light,
                    IsApplied: false,
                    IsCodexConnected: false));

                Ensure(window.SettingsInfoPanel.Visibility == Visibility.Visible &&
                       window.InfoPage.Visibility == Visibility.Visible &&
                       window.InfoContentHost.MaxWidth == 1440,
                    "ArtworkStudio must route to its dedicated page with the 1440-DIP canvas measure.");
                Ensure(window.InfoScroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
                       window.InfoScroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                    "The artwork route must use vertical scrolling without a horizontal escape hatch.");

                var cases = new[]
                {
                    new ArtworkRouteLayoutCase("1920×1080", 1920d, 1080d, false),
                    new ArtworkRouteLayoutCase("1366×768", 1366d, 768d, true),
                    new ArtworkRouteLayoutCase("1080 logical width", 1080d, 720d, true),
                    // 1366×768 at roughly 200% scaling exposes about 683×384 DIPs.
                    new ArtworkRouteLayoutCase("1366×768 at 200%", 683d, 384d, true),
                };
                foreach (var layoutCase in cases)
                {
                    ArrangeArtworkStudioRoute(window, layoutCase.Width, layoutCase.Height);
                    AssertArtworkStudioGeometry(window, layoutCase);
                    EnsureRouteButtonIsHit(
                        window,
                        window.ArtworkWorkbench.HeroRegionButton,
                        $"{layoutCase.Name} region selector");
                    EnsureRouteButtonIsHit(
                        window,
                        window.ArtworkWorkbench.DarkModeButton,
                        $"{layoutCase.Name} mode selector");
                    EnsureRouteButtonIsHit(
                        window,
                        window.ArtworkWorkbench.Inspector.ChooseImageButton,
                        $"{layoutCase.Name} local image action");
                    EnsureRouteButtonIsHit(
                        window,
                        window.ArtworkWorkbench.CopyModeButton,
                        $"{layoutCase.Name} mode transfer action");
                }
            }
            catch (TargetInvocationException exception)
            {
                failure = exception.InnerException ?? exception;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (window is not null)
                {
                    window.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            if (failure is not null)
            {
                throw new InvalidOperationException(
                    "The full ArtworkStudio route clipped or lost an interactive control.",
                    failure);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static ThemePackage CreateWorkbenchProbePackage(string root, string themeId) => new(
        root,
        Path.Combine(root, ThemePackageLoader.ManifestFileName),
        new ThemeManifest
        {
            Id = themeId,
            Name = "Artwork Workbench Probe",
            Version = "1.0.0",
            Author = "Tessalume Tests",
            Capabilities = new ThemeCapabilities { Light = true, Dark = true },
        },
        null,
        null,
        new Dictionary<string, string>(),
        null,
        null);

    private static void InvokePrivateMainWindowMethod(
        MainWindow window,
        string name,
        params object[] arguments)
    {
        var method = typeof(MainWindow).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), name);
        method.Invoke(window, arguments);
    }

    private static void ArrangeArtworkStudioRoute(
        MainWindow window,
        double width,
        double height)
    {
        var size = new Size(width, height);
        for (var pass = 0; pass < 3; pass++)
        {
            window.AdaptiveViewport.Measure(size);
            window.AdaptiveViewport.Arrange(new Rect(size));
            window.AdaptiveViewport.UpdateLayout();
        }
        window.InfoScroll.ScrollToTop();
        window.AdaptiveViewport.UpdateLayout();
    }

    private static void AssertArtworkStudioGeometry(
        MainWindow window,
        ArtworkRouteLayoutCase layoutCase)
    {
        var viewport = window.AdaptiveViewport;
        var scroll = window.InfoScroll;
        var workbench = window.ArtworkWorkbench;
        EnsureAlmostEqual(window.AdaptiveScale.ScaleX, 1, $"{layoutCase.Name} native X scale");
        EnsureAlmostEqual(window.AdaptiveScale.ScaleY, 1, $"{layoutCase.Name} native Y scale");
        Ensure(viewport.ActualWidth <= layoutCase.Width + 0.5 &&
               viewport.ActualHeight <= layoutCase.Height + 0.5 &&
               scroll.ViewportWidth > 0 && scroll.ViewportHeight > 0,
            $"The {layoutCase.Name} route must fit its logical viewport.");
        Ensure(workbench.ActualWidth <= scroll.ViewportWidth + 0.5,
            $"The {layoutCase.Name} workbench expanded beyond the scroll viewport.");
        Ensure(scroll.ExtentWidth <= scroll.ViewportWidth + 0.5 &&
               Math.Abs(scroll.HorizontalOffset) <= 0.001,
            $"The {layoutCase.Name} route has right-side or horizontal clipping.");

        var workbenchRight = workbench.TranslatePoint(
            new Point(workbench.ActualWidth, 0),
            viewport).X;
        var scrollRight = scroll.TranslatePoint(
            new Point(scroll.ActualWidth, 0),
            viewport).X;
        Ensure(workbenchRight <= scrollRight + 0.5,
            $"The {layoutCase.Name} workbench right edge escaped its routed page.");
        if (layoutCase.MustScroll)
        {
            Ensure(scroll.ScrollableHeight > 0,
                $"The {layoutCase.Name} route must scroll vertically instead of clipping its lower actions.");
        }

        if (layoutCase.Width < 900)
        {
            EnsureAlmostEqual(
                window.ShellSidebarColumn.ActualWidth,
                184,
                $"{layoutCase.Name} compact sidebar");
            Ensure(Grid.GetRow(workbench.InspectorScroller) == 2 &&
                   Grid.GetColumn(workbench.InspectorScroller) == 0,
                $"The {layoutCase.Name} workbench must stack the inspector at high DPI logical width.");
        }
    }

    private static void EnsureRouteButtonIsHit(
        MainWindow window,
        Button button,
        string scenario)
    {
        button.BringIntoView();
        window.AdaptiveViewport.UpdateLayout();
        Ensure(button.Visibility == Visibility.Visible &&
               button.IsEnabled &&
               button.ActualWidth > 0 &&
               button.ActualHeight > 0,
            $"The {scenario} must remain visible, enabled, and measurable.");

        var topLeft = button.TranslatePoint(new Point(0, 0), window.AdaptiveViewport);
        var bottomRight = button.TranslatePoint(
            new Point(button.ActualWidth, button.ActualHeight),
            window.AdaptiveViewport);
        Ensure(topLeft.X >= -0.5 &&
               bottomRight.X <= window.AdaptiveViewport.ActualWidth + 0.5 &&
               topLeft.Y >= -0.5 &&
               bottomRight.Y <= window.AdaptiveViewport.ActualHeight + 0.5,
            $"The {scenario} must be fully reachable without right-side or vertical clipping.");

        var center = new Point(
            (topLeft.X + bottomRight.X) / 2,
            (topLeft.Y + bottomRight.Y) / 2);
        var hit = VisualTreeHelper.HitTest(window.AdaptiveViewport, center)?.VisualHit;
        Ensure(IsVisualDescendantOrSelf(hit, button),
            $"The {scenario} center must hit its intended button " +
            $"(hit: {hit?.GetType().FullName ?? "none"}, center: {center}).");
    }

    private readonly record struct ArtworkRouteLayoutCase(
        string Name,
        double Width,
        double Height,
        bool MustScroll);
}
