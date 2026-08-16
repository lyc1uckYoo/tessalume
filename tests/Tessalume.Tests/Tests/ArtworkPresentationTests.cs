using System.Reflection;
using System.Windows.Controls;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

internal static partial class TestSuite
{
    static Task ArtworkPresentationFormattingIsLosslessAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Ensure(ArtworkPresentationFormatter.CssToken("281.93049881117156%") == "281.93%" &&
                       ArtworkPresentationFormatter.CssToken("52.47729949938649%") == "52.48%" &&
                       ArtworkPresentationFormatter.CssToken("-121.6463036525212px") == "-121.65px" &&
                       ArtworkPresentationFormatter.CssToken("100.0000001%") == "100%" &&
                       ArtworkPresentationFormatter.CssValue("107.66724286000954% 100%") ==
                       "107.67% 100%" &&
                       ArtworkPresentationFormatter.CssValue("cover auto") == "cover auto" &&
                       ArtworkPresentationFormatter.CssValue("contain center") == "contain center",
                    "Visible artwork values must use compact numeric formatting while preserving CSS keywords and units.");

                var inspector = new ArtworkInspectorView();
                var placement = new ThemeArtworkPlacementSpec
                {
                    SizeMode = ThemeArtworkSizeMode.Explicit,
                    Width = ThemeArtworkLength.Percent(281.93049881117156d),
                    Height = ThemeArtworkLength.Auto,
                    PositionX = ThemeArtworkPositionValue.Percent(52.47729949938649d),
                    PositionY = ThemeArtworkPositionValue.Pixels(-121.6463036525212d),
                };
                var placementChanges = 0;
                ThemeArtworkPlacementSpec? committedPlacement = null;
                inspector.PlacementChanged += (_, args) =>
                {
                    placementChanges++;
                    committedPlacement = args.Placement;
                };
                inspector.SetPlacement(placement, ThemeArtworkCompositionMode.Custom);
                Ensure(inspector.SizeWidthValue.Text == "281.93%" &&
                       inspector.PositionXValue.Text == "52.48%" &&
                       inspector.PositionYValue.Text == "-121.65px" &&
                       inspector.PlacementSummaryText.Text.Contains(
                           "size 281.93% auto · position 52.48% -121.65px",
                           StringComparison.Ordinal),
                    "The placement editors and summary must display the compact presentation values.");

                InvokePrivate(inspector, "CommitPlacementEditors");
                Ensure(placementChanges == 0,
                    "Confirming untouched compact placement text must not round or rewrite the exact model.");

                inspector.PositionXValue.SelectedItem = null;
                inspector.PositionXValue.Text = "61.123456789%";
                InvokePrivate(inspector, "CommitPlacementEditors");
                Ensure(placementChanges == 1 &&
                       committedPlacement is not null &&
                       committedPlacement.Width == placement.Width &&
                       committedPlacement.Height == ThemeArtworkLength.Auto &&
                       committedPlacement.PositionX == ThemeArtworkPositionValue.Percent(61.123456789d) &&
                       committedPlacement.PositionY == placement.PositionY &&
                       inspector.PositionXValue.Text == "61.12%",
                    "A high-precision placement edit must commit exactly once, preserve its siblings, and only round its display.");
                InvokePrivate(inspector, "CommitPlacementEditors");
                Ensure(placementChanges == 1,
                    "Reconfirming the formatted placement display must retain its exact committed token without drift.");

                double? committedBrightness = null;
                var numericChanges = 0;
                inspector.NumericValueChanged += (_, args) =>
                {
                    if (args.Parameter != ArtworkParameter.Brightness) return;
                    numericChanges++;
                    committedBrightness = args.Value;
                };
                inspector.SetAdjustment(new ThemeArtworkAdjustment
                {
                    Brightness = 108.123456789d,
                });
                Ensure(inspector.BrightnessValue.Text == "108.12%",
                    "Visible slider editors must use the same compact two-decimal presentation.");
                InvokePrivate(inspector, "CommitValueEditor", inspector.BrightnessValue);
                Ensure(numericChanges == 0,
                    "Confirming an untouched compact slider value must preserve its exact backing value.");

                InvokePrivate(inspector, "BeginInteraction", ArtworkParameter.Brightness);
                inspector.BrightnessValue.Text = "109.87654321%";
                inspector.SetAdjustment(new ThemeArtworkAdjustment { Brightness = 130d });
                Ensure(inspector.BrightnessValue.Text == "109.87654321%",
                    "An active numeric edit must not be overwritten by a concurrent presentation refresh.");
                InvokePrivate(inspector, "CommitValueEditor", inspector.BrightnessValue);
                InvokePrivate(inspector, "EndActiveInteraction");
                Ensure(numericChanges == 1 &&
                       committedBrightness == 109.87654321d &&
                       inspector.BrightnessValue.Text == "109.88%",
                    "A high-precision slider edit must retain its exact value and normalize only after confirmation.");
                InvokePrivate(inspector, "CommitValueEditor", inspector.BrightnessValue);
                Ensure(numericChanges == 1 && inspector.BrightnessSlider.Value == 109.87654321d,
                    "Reconfirming a formatted slider display must not create round-trip drift.");
                var preciseBrightness = committedBrightness
                    ?? throw new InvalidOperationException("The exact brightness edit was not committed.");

                var persisted = new ThemeVisualSettings
                {
                    Light = new ThemeVisualModeSettings
                    {
                        Sidebar = new ThemeArtworkAdjustment
                        {
                            CompositionMode = ThemeArtworkCompositionMode.Custom,
                            Placement = committedPlacement,
                            Brightness = preciseBrightness,
                        },
                    },
                };
                var json = JsonSerializer.Serialize(persisted);
                var restored = JsonSerializer.Deserialize<ThemeVisualSettings>(json)
                    ?? throw new InvalidOperationException("The precision fixture did not deserialize.");
                Ensure(json.Contains("61.123456789", StringComparison.Ordinal) &&
                       json.Contains("109.87654321", StringComparison.Ordinal) &&
                       restored.Light.Sidebar.Placement?.PositionX ==
                       ThemeArtworkPositionValue.Percent(61.123456789d) &&
                       restored.Light.Sidebar.Brightness == 109.87654321d,
                    "Persistence and exported JSON must retain full precision even when the UI displays compact values.");
            }
            catch (Exception exception)
            {
                failure = exception is TargetInvocationException invocation
                    ? invocation.InnerException ?? invocation
                    : exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException(
                "The artwork presentation precision checks failed.",
                failure);
        }
        return Task.CompletedTask;
    }

    static Task ArtworkSidebarReviewLayoutPreservesAspectAsync()
    {
        var target = new Size(260d, 800d);
        foreach (var (name, host) in new[]
                 {
                     ("1920×1080", new Size(760d, 440d)),
                     ("1366×768", new Size(540d, 340d)),
                     ("200% DPI", new Size(320d, 260d)),
                 })
        {
            var layout = ArtworkCanvasControl.CalculatePresentationLayout(
                ArtworkRegion.Sidebar,
                host,
                target);
            var availableWidth = Math.Max(1d, host.Width - 22d);
            var fullFitWidth = Math.Max(1d, host.Height - 22d) * target.Width / target.Height;
            Ensure(layout.Width <= availableWidth + .001d &&
                   Math.Abs(layout.Width / layout.Height - target.Width / target.Height) < .000001d &&
                   layout.Width >= Math.Min(availableWidth, fullFitWidth * 1.6d) &&
                   layout.ScrollVertically,
                $"The {name} Sidebar review must stay proportional, fit horizontally, and use vertical scrolling instead of a tiny full-height strip.");
        }

        var hero = ArtworkCanvasControl.CalculatePresentationLayout(
            ArtworkRegion.Hero,
            new Size(760d, 440d),
            new Size(1440d, 420d));
        var chat = ArtworkCanvasControl.CalculatePresentationLayout(
            ArtworkRegion.Chat,
            new Size(760d, 440d),
            new Size(1440d, 900d));
        Ensure(!hero.ScrollVertically && !chat.ScrollVertically &&
               hero.Width <= 738d && hero.Height <= 418d &&
               chat.Width <= 738d && chat.Height <= 418d,
            "Hero and chat must retain their existing fit-to-canvas presentation without Sidebar scrolling.");
        return Task.CompletedTask;
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, methodName);
        method.Invoke(target, arguments);
    }
}
