using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;
using Tessalume.Core.Runtime;

internal static partial class TestSuite
{
    static Task ArtworkWorkbenchViewLoadsAndAdaptsAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-artwork-workbench-wpf-{Guid.NewGuid():N}");
        var themesDirectory = Path.Combine(root, "themes");
        var dataDirectory = Path.Combine(root, "data");
        Directory.CreateDirectory(themesDirectory);
        Directory.CreateDirectory(dataDirectory);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            ArtworkWorkbenchView? view = null;
            try
            {
                window = new MainWindow(new PortableLayout(root, themesDirectory, dataDirectory));
                InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                view = window.ArtworkWorkbench
                    ?? throw new InvalidOperationException(
                        "The artwork workbench view must load from compiled WPF markup.");
                var workbenchParent = VisualTreeHelper.GetParent(view) as Panel
                    ?? throw new InvalidOperationException(
                        "The workbench must remain hosted by a detachable panel in the personalization page.");
                workbenchParent.Children.Remove(view);

                const string themeId = "workbench.wpf.probe";
                var session = new ArtworkWorkbenchSession();
                var settings = session.Mutate(
                    themeId,
                    new ThemeVisualSettings(),
                    current => current with
                    {
                        Light = current.Light with
                        {
                            Hero = current.Light.Hero with
                            {
                                Brightness = 112,
                                Zoom = 118,
                                OffsetX = 24,
                                OffsetY = -12,
                            },
                        },
                    });
                var package = new ThemePackage(
                    root,
                    Path.Combine(root, ThemePackageLoader.ManifestFileName),
                    new ThemeManifest
                    {
                        Id = themeId,
                        Name = "工作台布局探针",
                        Version = "1.0.0",
                        Author = "Tessalume Tests",
                        Capabilities = new ThemeCapabilities { Light = true, Dark = true },
                    },
                    null,
                    null,
                    new Dictionary<string, string>(),
                    null,
                    null);
                view.SetContext(new ArtworkWorkbenchContext(
                    themeId,
                    "工作台布局探针",
                    package,
                    settings,
                    ArtworkColorMode.Light,
                    IsApplied: true,
                    IsCodexConnected: false));

                Ensure(view.SyncStatusText.Text == "未连接" &&
                       view.HeroRegionButton.IsEnabled &&
                       view.DarkModeButton.IsEnabled,
                    "A disconnected workbench must keep its editing controls enabled without duplicating the shared theme header.");
                Ensure(view.CurrentSettings.Light.Hero is
                { Brightness: 112, Zoom: 118, OffsetX: 24, OffsetY: -12 },
                    "The view must render the normalized settings produced by its application session.");

                view.SidebarRegionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                view.SetConnectionState(true);
                view.SetSurfaceMetrics(
                    ArtworkRegion.Sidebar,
                    new ArtworkSurfacePreviewMetrics(
                        1d,
                        1d,
                        1d,
                        IsLive: false,
                        "theme-not-committed-yet"));
                Ensure(view.SurfaceMetricsText.Text.Contains(
                           "在线待校准 · 标准预览 260×800",
                           StringComparison.Ordinal) &&
                       view.PreviewCanvas.TargetSize == new ArtworkSize(260d, 800d),
                    "An early connected probe miss must stay explicitly labeled as a standard preview.");
                view.SetSurfaceMetrics(
                    ArtworkRegion.Sidebar,
                    new ArtworkSurfacePreviewMetrics(
                        275d,
                        998d,
                        1.25d,
                        IsLive: true,
                        "current Codex task surface"));
                Ensure(view.SurfaceMetricsText.Text == "在线实测 275×998 · DPR 1.25" &&
                       view.PreviewCanvas.TargetSize == new ArtworkSize(275d, 998d),
                    "The next successful probe must replace standard geometry in both the badge and primary canvas without another user action.");
                view.HeroRegionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var typedInspector = new ArtworkInspectorView();
                var cartethyiaPlacement = new ThemeArtworkPlacementSpec
                {
                    SizeMode = ThemeArtworkSizeMode.Explicit,
                    Width = ThemeArtworkLength.Percent(355d),
                    Height = ThemeArtworkLength.Auto,
                    PositionX = ThemeArtworkPositionValue.Percent(52d),
                    PositionY = ThemeArtworkPositionValue.Pixels(-200d),
                    Geometry = new ThemeArtworkGeometry
                    {
                        Scale = 1.012d,
                        OriginX = ThemeArtworkPositionValue.Percent(73d),
                        OriginY = ThemeArtworkPositionValue.Percent(40d),
                    },
                };
                ThemeArtworkPlacementSpec? committedPlacement = null;
                typedInspector.PlacementChanged += (_, args) =>
                    committedPlacement = args.Placement;
                typedInspector.SetPlacement(
                    cartethyiaPlacement,
                    ThemeArtworkCompositionMode.Theme);
                typedInspector.PositionXValue.Text = "60%";
                var commitPlacement = typeof(ArtworkInspectorView).GetMethod(
                    "CommitPlacementEditors",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        nameof(ArtworkInspectorView),
                        "CommitPlacementEditors");
                commitPlacement.Invoke(typedInspector, null);
                Ensure(committedPlacement is not null &&
                       committedPlacement.Width == ThemeArtworkLength.Percent(355d) &&
                       committedPlacement.Height == ThemeArtworkLength.Auto &&
                       committedPlacement.PositionX == ThemeArtworkPositionValue.Percent(60d) &&
                       committedPlacement.PositionY == ThemeArtworkPositionValue.Pixels(-200d) &&
                       committedPlacement.Geometry == cartethyiaPlacement.Geometry,
                    "Changing one typed Cartethyia token must preserve its %, px, auto, and geometry siblings.");

                var host = new ScrollViewer
                {
                    Content = view,
                    Background = Brushes.Transparent,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    PanningMode = PanningMode.VerticalOnly,
                };

                ArrangeArtworkWorkbench(host, 1260, 820);
                Ensure(Grid.GetRow(view.InspectorScroller) == 0 &&
                       Grid.GetColumn(view.InspectorScroller) == 2,
                    "The wide workbench must keep the canvas and inspector side by side.");
                var canvasShare = view.CanvasColumn.ActualWidth /
                    (view.CanvasColumn.ActualWidth + view.InspectorColumn.ActualWidth);
                Ensure(canvasShare is >= 0.58 and <= 0.72 &&
                       view.PreviewCanvas.ActualWidth > 400 &&
                       view.PreviewCanvas.ActualHeight >= 330 &&
                       view.PreviewCanvas.FullSourceStage.Visibility == Visibility.Visible &&
                       view.PreviewCanvas.PreviewStage.Visibility == Visibility.Collapsed,
                    "The wide workbench must give the primary canvas roughly two thirds of usable space.");
                view.Inspector.BasicGroupButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Ensure(view.PreviewCanvas.ViewMode == ArtworkCanvasViewMode.Result &&
                       view.PreviewCanvas.PreviewStage.Visibility == Visibility.Visible &&
                       view.PreviewCanvas.FullSourceStage.Visibility == Visibility.Collapsed,
                    "Non-composition parameters must switch to their final-surface preview without exposing a separate result-view control.");
                view.Inspector.CompositionGroupButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Ensure(view.PreviewCanvas.ViewMode == ArtworkCanvasViewMode.FullSource,
                    "Composition parameters must return to full-source framing automatically.");
                view.SidebarRegionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Ensure(view.PreviewCanvas.MinHeight == 360,
                    "Switching to Sidebar must retain a compact but useful framing canvas.");
                view.ChatRegionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Ensure(view.PreviewCanvas.MinHeight == 340,
                    "Switching back to Chat must release the extra Sidebar canvas height.");
                Ensure(double.IsPositiveInfinity(view.InspectorScroller.MaxHeight),
                    "The wide inspector must remain reachable through the page scroll instead of a fixed-height nested viewport.");
                view.HeroRegionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                EnsureButtonCenterIsHit(host, view.HeroRegionButton, "wide region selector");
                var offCenterPlacement = new ThemeArtworkPlacementSpec
                {
                    SizeMode = ThemeArtworkSizeMode.Cover,
                    Geometry = new ThemeArtworkGeometry
                    {
                        Scale = 1.2d,
                        OriginX = ThemeArtworkPositionValue.Percent(75d),
                        OriginY = ThemeArtworkPositionValue.Percent(40d),
                    },
                };
                var sourceProbe = new WriteableBitmap(
                    1024,
                    1536,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null);
                sourceProbe.Freeze();
                var processedProbe = new WriteableBitmap(
                    1024,
                    1536,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null);
                processedProbe.Freeze();
                var themeOriginalProbe = new WriteableBitmap(
                    800,
                    1200,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null);
                themeOriginalProbe.Freeze();
                view.PreviewCanvas.SetSources(
                    sourceProbe,
                    processedProbe,
                    themeOriginalProbe);
                Ensure(ReferenceEquals(view.PreviewCanvas.FullSourceImage.Source, sourceProbe) &&
                       ReferenceEquals(view.PreviewCanvas.ArtworkImage.Source, processedProbe),
                    "Full-source framing must stay raw while the result view uses processed pixels.");
                view.PreviewCanvas.SetShowOriginal(true);
                Ensure(ReferenceEquals(
                           view.PreviewCanvas.FullSourceImage.Source,
                           themeOriginalProbe) &&
                       view.PreviewCanvas.CropFrame.Visibility == Visibility.Collapsed,
                    "Hold-to-compare must show the separate theme-original bitmap without crop adorners.");
                view.PreviewCanvas.SetShowOriginal(false);
                view.PreviewCanvas.SetComposition(
                    new ThemeArtworkAdjustment
                    {
                        CompositionMode = ThemeArtworkCompositionMode.Theme,
                        Placement = offCenterPlacement,
                    },
                    offCenterPlacement);
                view.PreviewCanvas.SetViewMode(ArtworkCanvasViewMode.Result);
                host.UpdateLayout();
                Ensure(view.PreviewCanvas.PlacementProjection is
                { RenderedImage.Width: > 0d, RenderedImage.Height: > 0d } &&
                       view.PreviewCanvas.ArtworkLayer.RenderTransform.Value.IsIdentity,
                    "The arranged canvas must fold theme geometry into one final placement without a second layer transform.");
                view.PreviewCanvas.SetComposition(
                    new ThemeArtworkAdjustment
                    {
                        CompositionMode = ThemeArtworkCompositionMode.Custom,
                        Placement = ArtworkPlacementMapper.Fill(
                            new ArtworkSize(1024d, 1536d),
                            new ArtworkSize(1440d, 420d)),
                        Motion = new ThemeArtworkMotion
                        {
                            Mode = "loop",
                            Keyframes =
                            [
                                new ThemeArtworkMotionKeyframe { At = 0d },
                                new ThemeArtworkMotionKeyframe
                                {
                                    At = 100d,
                                    TranslateX = "2%",
                                    ScaleDelta = .02d,
                                },
                            ],
                        },
                    },
                    offCenterPlacement);
                view.PreviewCanvas.SetViewMode(ArtworkCanvasViewMode.Result);
                view.PreviewCanvas.SetMotionPreview(true);
                Ensure(view.PreviewCanvas.ArtworkImageCanvas.RenderTransform is TransformGroup,
                    "The result canvas must be able to preview transient artwork motion.");
                view.PreviewCanvas.SetMotionPreview(false);
                Ensure(view.PreviewCanvas.ArtworkImageCanvas.RenderTransform.Value.IsIdentity,
                    "Pausing motion must restore an identity transient transform.");
                view.PreviewCanvas.SetViewMode(ArtworkCanvasViewMode.FullSource);

                ArrangeArtworkWorkbench(host, 1080, 720);
                Ensure(Grid.GetRow(view.InspectorScroller) == 0 &&
                       Grid.GetColumn(view.InspectorScroller) == 2 &&
                       view.InspectorScroller.ActualWidth >= 286 &&
                       view.PreviewCanvas.FullSourceStage.Visibility == Visibility.Visible &&
                       view.PreviewCanvas.ActualWidth > 0 &&
                       view.PreviewCanvas.ActualHeight > 0,
                    "The 1080-wide workbench must retain a valid side-by-side canvas and inspector layout.");
                EnsureButtonCenterIsHit(host, view.DarkModeButton, "1080-wide mode selector");

                ArrangeArtworkWorkbench(host, 680, 720);
                Ensure(Grid.GetRow(view.InspectorScroller) == 2 &&
                       Grid.GetColumn(view.InspectorScroller) == 0 &&
                       view.WorkspaceGapRow.ActualHeight >= 11,
                    "The narrow workbench must stack its inspector below the canvas with visible spacing.");
                Ensure(host.ScrollableHeight > 0 &&
                       view.ActualWidth <= host.ViewportWidth + 0.5,
                    "The narrow workbench must scroll vertically without expanding beyond its viewport.");
                EnsureButtonCenterIsHit(
                    host,
                    view.Inspector.ChooseImageButton,
                    "narrow local-image action");
            }
            catch (Exception exception)
            {
                failure = exception is System.Reflection.TargetInvocationException invocation
                    ? invocation.InnerException ?? invocation
                    : exception;
            }
            finally
            {
                view?.Dispose();
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
                    "The artwork workbench WPF layout and hit-test smoke check failed.",
                    failure);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static void ArrangeArtworkWorkbench(
        ScrollViewer host,
        double width,
        double height)
    {
        host.Width = width;
        host.Height = height;
        var size = new Size(width, height);
        for (var pass = 0; pass < 2; pass++)
        {
            host.Measure(size);
            host.Arrange(new Rect(size));
            host.UpdateLayout();
        }
        host.ScrollToTop();
        host.UpdateLayout();
    }

    private static void EnsureButtonCenterIsHit(
        ScrollViewer host,
        Button button,
        string description)
    {
        button.BringIntoView();
        host.UpdateLayout();
        Ensure(button.Visibility == Visibility.Visible &&
               button.ActualWidth > 0 &&
               button.ActualHeight > 0,
            $"The {description} must have a visible, non-empty layout slot.");

        var center = button.TranslatePoint(
            new Point(button.ActualWidth / 2, button.ActualHeight / 2),
            host);
        Ensure(center.X >= 0 && center.X <= host.ViewportWidth &&
               center.Y >= 0 && center.Y <= host.ViewportHeight,
            $"The {description} center must be reachable inside the scroll viewport.");

        var hit = VisualTreeHelper.HitTest(host, center)?.VisualHit;
        Ensure(IsVisualDescendantOrSelf(hit, button),
            $"The {description} center must resolve to the intended button visual " +
            $"(hit: {hit?.GetType().FullName ?? "none"}, center: {center}).");
    }

    private static bool IsVisualDescendantOrSelf(
        DependencyObject? candidate,
        DependencyObject expectedAncestor)
    {
        for (var current = candidate; current is not null; current = GetVisualParent(current))
        {
            if (ReferenceEquals(current, expectedAncestor)) return true;
        }
        return false;
    }

    private static DependencyObject? GetVisualParent(DependencyObject child) =>
        child is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);
}
