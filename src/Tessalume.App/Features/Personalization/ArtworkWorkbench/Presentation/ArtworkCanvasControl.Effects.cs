using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkCanvasControl
{
    private void UpdateEffects()
    {
        var effective = _showOriginal ? new ThemeArtworkAdjustment() : _adjustment;
        ArtworkLayer.Opacity = effective.Opacity / 100d;
        ArtworkImage.Effect = null;
        ArtworkLayer.Effect = effective.Blur > 0.01
            ? new BlurEffect { Radius = Math.Min(20, effective.Blur) }
            : null;

        var color = ParseColor(effective.OverlayColor);
        TintOverlay.Background = new SolidColorBrush(color);
        TintOverlay.Opacity = Math.Min(0.86, effective.OverlayOpacity / 100d * 0.86);

        var gradientVeil = effective.GradientVeil;
        var readabilityVeil = effective.ReadabilityVeil;
        foreach (var variant in effective.ResponsiveVariants)
        {
            if (variant.MinWidth is { } minimum && _targetViewport.Width < minimum ||
                variant.MaxWidth is { } maximum && _targetViewport.Width > maximum)
            {
                continue;
            }
            gradientVeil = variant.GradientVeil ?? gradientVeil;
            readabilityVeil = variant.ReadabilityVeil ?? readabilityVeil;
        }
        RenderGradientVeils(
            gradientVeil,
            effective.GradientStrength,
            color);
        RenderImageReadabilityVeil(readabilityVeil);

        VignetteOverlay.Background = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.72,
            RadiusY = 0.72,
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0.45),
                new GradientStop(Colors.Black, 1),
            },
        };
        VignetteOverlay.Opacity = Math.Min(0.78, effective.Vignette / 100d * 0.78);

        RenderReadabilityProtection(effective.ReadabilityProtection);
    }

    private void RenderGradientVeils(
        ThemeArtworkGradientVeil gradientVeil,
        double legacyStrength,
        Color fallbackColor)
    {
        GradientOverlay.Children.Clear();
        var brushes = new List<Brush>();
        var normalized = (gradientVeil ?? new ThemeArtworkGradientVeil()).Normalize();
        if (normalized.Enabled)
        {
            var strength = normalized.Strength / 100d;
            foreach (var layer in normalized.Layers)
            {
                if (layer.Stops.Count == 0) continue;
                var brush = CreateLinearGradientBrush(layer.DirectionDeg);
                foreach (var stop in layer.Stops)
                {
                    var stopColor = ParseColor(stop.Color);
                    brush.GradientStops.Add(new GradientStop(
                        WithOpacity(stopColor, stop.Opacity / 100d * strength),
                        stop.Position / 100d));
                }
                brushes.Add(brush);
            }
            if (normalized.Layers.Count == 0 && strength > 0d)
            {
                brushes.Add(CreateFallbackVeil(fallbackColor, strength));
            }
        }
        var legacy = Math.Clamp(legacyStrength / 100d, 0d, 1d);
        if (legacy > 0d)
        {
            brushes.Add(CreateFallbackVeil(fallbackColor, legacy));
        }

        // CSS paints the first listed background on top. WPF renders later
        // children on top, so add the equivalent brushes in reverse order.
        for (var index = brushes.Count - 1; index >= 0; index--)
        {
            GradientOverlay.Children.Add(new Border { Background = brushes[index] });
        }
    }

    private void RenderImageReadabilityVeil(ThemeArtworkReadabilityVeil veil)
    {
        ImageReadabilityVeilOverlay.Children.Clear();
        var normalized = (veil ?? new ThemeArtworkReadabilityVeil()).Normalize();
        if (!normalized.Enabled) return;
        var brush = CreateLinearGradientBrush(normalized.DirectionDeg);
        var color = ParseColor(normalized.Color);
        brush.GradientStops.Add(new GradientStop(
            WithOpacity(color, normalized.Opacity / 100d),
            normalized.RangeStart / 100d));
        brush.GradientStops.Add(new GradientStop(
            WithOpacity(color, 0d),
            normalized.RangeEnd / 100d));
        ImageReadabilityVeilOverlay.Children.Add(new Border { Background = brush });
    }

    private static LinearGradientBrush CreateFallbackVeil(Color color, double strength)
    {
        var brush = CreateLinearGradientBrush(90d);
        brush.GradientStops.Add(new GradientStop(
            WithOpacity(color, Math.Min(.82d, strength * .82d)),
            0d));
        brush.GradientStops.Add(new GradientStop(WithOpacity(color, 0d), .72d));
        return brush;
    }

    private static LinearGradientBrush CreateLinearGradientBrush(double directionDegrees)
    {
        var radians = directionDegrees * Math.PI / 180d;
        var x = Math.Sin(radians);
        var y = -Math.Cos(radians);
        return new LinearGradientBrush
        {
            StartPoint = new Point(.5d - (x / 2d), .5d - (y / 2d)),
            EndPoint = new Point(.5d + (x / 2d), .5d + (y / 2d)),
        };
    }

    private static Color WithOpacity(Color color, double opacity) => Color.FromArgb(
        (byte)Math.Round(Math.Clamp(opacity, 0d, 1d) * byte.MaxValue),
        color.R,
        color.G,
        color.B);

    private void RenderReadabilityProtection(bool enabled)
    {
        ReadabilityOverlay.Visibility = enabled && _region == ArtworkRegion.Chat
            ? Visibility.Visible
            : Visibility.Collapsed;
        var maskColor = _mode == ArtworkColorMode.Dark
            ? Color.FromRgb(8, 12, 20)
            : Color.FromRgb(250, 252, 255);
        ReadabilityOverlay.Background = new LinearGradientBrush(
            Color.FromArgb(105, maskColor.R, maskColor.G, maskColor.B),
            Color.FromArgb(20, maskColor.R, maskColor.G, maskColor.B),
            new Point(0, 0.5),
            new Point(1, 0.5));
        RuntimeMockLayer.Effect = enabled && _region == ArtworkRegion.Hero
            ? new DropShadowEffect
            {
                BlurRadius = 12,
                Color = Colors.Black,
                Opacity = 0.72,
                ShadowDepth = 1,
            }
            : null;
        if (_region == ArtworkRegion.Sidebar)
        {
            SidebarMock.Opacity = enabled ? 1d : 0.82d;
        }
    }

    private void UpdateGuides()
    {
        if (!_showGuides || ViewportGrid.ActualWidth <= 0 || ViewportGrid.ActualHeight <= 0) return;
        var cropBoundary = new Rect(0.006, 0.006, 0.988, 0.988);
        PositionGuide(SafeAreaRectangle, cropBoundary);
        SafeAreaLabel.Child = CreateGuideLabel("实际裁切边界", Brushes.White);
        Canvas.SetLeft(SafeAreaLabel, 10);
        Canvas.SetTop(SafeAreaLabel, 36);

        var contextualGuide = _region switch
        {
            ArtworkRegion.Sidebar => new Rect(25d / 260d, 0.02, 227d / 260d, 0.96),
            ArtworkRegion.Hero => new Rect(76d / 1440d, 0.10, 0.46, 0.88),
            _ => Rect.Empty,
        };
        SubjectAreaRectangle.Visibility = contextualGuide.IsEmpty
            ? Visibility.Collapsed
            : Visibility.Visible;
        SubjectAreaLabel.Visibility = contextualGuide.IsEmpty
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (contextualGuide.IsEmpty) return;
        PositionGuide(SubjectAreaRectangle, contextualGuide);
        SubjectAreaLabel.Child = CreateGuideLabel(
            _region == ArtworkRegion.Hero ? "模板文字起始与最大宽度" : "模板线程列表覆盖区",
            new SolidColorBrush(Color.FromRgb(240, 195, 106)));
        Canvas.SetLeft(SubjectAreaLabel, contextualGuide.X * ViewportGrid.ActualWidth + 8);
        Canvas.SetTop(SubjectAreaLabel, contextualGuide.Y * ViewportGrid.ActualHeight + 8);
    }

    private static TextBlock CreateGuideLabel(string text, Brush foreground) => new()
    {
        Text = text,
        Foreground = foreground,
        FontSize = 9.5,
        FontWeight = FontWeights.SemiBold,
    };

    private void UpdateMockPalette()
    {
        var dark = _mode == ArtworkColorMode.Dark;
        RuntimeTopChrome.Background = new SolidColorBrush(dark
            ? Color.FromArgb(58, 16, 24, 39)
            : Color.FromArgb(126, 250, 252, 255));
        RuntimeTopChrome.BorderBrush = new SolidColorBrush(dark
            ? Color.FromArgb(56, 255, 255, 255)
            : Color.FromArgb(64, 24, 32, 45));
        SetPlaceholderPalette(
            HeroMock,
            dark ? Color.FromRgb(247, 248, 252) : Color.FromRgb(22, 30, 43));
        SetPlaceholderPalette(
            ChatMock,
            dark ? Color.FromRgb(247, 248, 252) : Color.FromRgb(22, 30, 43));
    }

    private static void SetPlaceholderPalette(DependencyObject root, Color color)
    {
        var borderIndex = 0;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Border border)
            {
                var alpha = (byte)Math.Max(72, 214 - borderIndex * 22);
                border.Background = new SolidColorBrush(
                    Color.FromArgb(alpha, color.R, color.G, color.B));
                borderIndex++;
            }
            SetPlaceholderPalette(child, color);
        }
    }

    private void PositionGuide(System.Windows.Shapes.Rectangle rectangle, Rect normalized)
    {
        Canvas.SetLeft(rectangle, normalized.X * ViewportGrid.ActualWidth);
        Canvas.SetTop(rectangle, normalized.Y * ViewportGrid.ActualHeight);
        rectangle.Width = normalized.Width * ViewportGrid.ActualWidth;
        rectangle.Height = normalized.Height * ViewportGrid.ActualHeight;
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            return Colors.Black;
        }
    }
}
