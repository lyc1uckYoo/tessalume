using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkCanvasControl
{
    private readonly ScaleTransform _motionScale = new(1d, 1d);
    private readonly TranslateTransform _motionTranslation = new();
    private bool _motionPreviewEnabled;
    private double _motionAmplitude = 1d;
    private double _motionDurationScale = 1d;

    internal void SetMotionPreview(bool enabled, bool reducedMotion = false)
    {
        _motionPreviewEnabled = enabled;
        _motionAmplitude = reducedMotion ? .35d : 1d;
        // Reduced mode preserves the authored rhythm and only lowers amplitude.
        _motionDurationScale = 1d;
        UpdateMotionPreview();
    }

    private void UpdateMotionPreview()
    {
        StopMotionPreview();
        var motion = _adjustment.Motion?.Normalize();
        if (!_motionPreviewEnabled ||
            _viewMode != ArtworkCanvasViewMode.Result ||
            _showOriginal ||
            motion is not { Mode: "loop", Keyframes.Count: > 0 } ||
            ViewportGrid.ActualWidth <= 0d ||
            ViewportGrid.ActualHeight <= 0d)
        {
            return;
        }

        var transform = new TransformGroup();
        transform.Children.Add(_motionScale);
        transform.Children.Add(_motionTranslation);
        ArtworkImageCanvas.RenderTransformOrigin = new Point(.5d, .5d);
        ArtworkImageCanvas.RenderTransform = transform;

        var frames = ResolveMotionFrames(motion);
        var duration = TimeSpan.FromMilliseconds(motion.DurationMs * _motionDurationScale);
        var autoReverse = motion.Direction is "alternate" or "alternate-reverse";
        BeginMotionAnimation(
            _motionTranslation,
            TranslateTransform.XProperty,
            frames.Select(frame => (
                frame.At,
                ResolveMotionDelta(frame.TranslateX, true) * _motionAmplitude)),
            duration,
            autoReverse,
            motion.Easing);
        BeginMotionAnimation(
            _motionTranslation,
            TranslateTransform.YProperty,
            frames.Select(frame => (
                frame.At,
                ResolveMotionDelta(frame.TranslateY, false) * _motionAmplitude)),
            duration,
            autoReverse,
            motion.Easing);
        BeginMotionAnimation(
            _motionScale,
            ScaleTransform.ScaleXProperty,
            frames.Select(frame => (
                frame.At,
                Math.Max(.1d, 1d + frame.ScaleDelta * _motionAmplitude))),
            duration,
            autoReverse,
            motion.Easing);
        BeginMotionAnimation(
            _motionScale,
            ScaleTransform.ScaleYProperty,
            frames.Select(frame => (
                frame.At,
                Math.Max(.1d, 1d + frame.ScaleDelta * _motionAmplitude))),
            duration,
            autoReverse,
            motion.Easing);
        BeginMotionAnimation(
            ArtworkImageCanvas,
            UIElement.OpacityProperty,
            frames.Select(frame => (
                frame.At,
                Math.Clamp(
                    1d + frame.OpacityDelta / 100d * _motionAmplitude,
                    0d,
                    1d))),
            duration,
            autoReverse,
            motion.Easing);
    }

    private void StopMotionPreview()
    {
        _motionTranslation.BeginAnimation(TranslateTransform.XProperty, null);
        _motionTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        _motionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _motionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ArtworkImageCanvas.BeginAnimation(UIElement.OpacityProperty, null);
        _motionTranslation.X = 0d;
        _motionTranslation.Y = 0d;
        _motionScale.ScaleX = 1d;
        _motionScale.ScaleY = 1d;
        ArtworkImageCanvas.Opacity = 1d;
        ArtworkImageCanvas.RenderTransform = Transform.Identity;
    }

    private static IReadOnlyList<ThemeArtworkMotionKeyframe> ResolveMotionFrames(
        ThemeArtworkMotion motion)
    {
        if (motion.Direction is not ("reverse" or "alternate-reverse"))
        {
            return motion.Keyframes;
        }
        return motion.Keyframes
            .Select(frame => frame with { At = 100d - frame.At })
            .OrderBy(frame => frame.At)
            .ToArray();
    }

    private double ResolveMotionDelta(string token, bool horizontal)
    {
        var value = (token ?? string.Empty).Trim().ToLowerInvariant();
        var isPercent = value.EndsWith('%');
        var numeric = value.EndsWith("px", StringComparison.Ordinal)
            ? value[..^2]
            : isPercent
                ? value[..^1]
                : value;
        if (!double.TryParse(
                numeric,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            return 0d;
        }
        var targetLength = horizontal ? _targetViewport.Width : _targetViewport.Height;
        var previewLength = horizontal ? ViewportGrid.ActualWidth : ViewportGrid.ActualHeight;
        var cssPixels = isPercent ? targetLength * amount / 100d : amount;
        return cssPixels * previewLength / Math.Max(1d, targetLength);
    }

    private static void BeginMotionAnimation(
        IAnimatable target,
        DependencyProperty property,
        IEnumerable<(double At, double Value)> values,
        TimeSpan duration,
        bool autoReverse,
        string easing)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(duration),
            AutoReverse = autoReverse,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        foreach (var (at, value) in values)
        {
            var keyTime = KeyTime.FromTimeSpan(TimeSpan.FromTicks(
                (long)Math.Round(duration.Ticks * Math.Clamp(at, 0d, 100d) / 100d)));
            animation.KeyFrames.Add(CreateMotionKeyFrame(value, keyTime, easing));
        }
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleKeyFrame CreateMotionKeyFrame(
        double value,
        KeyTime keyTime,
        string easing) => easing switch
        {
            "linear" => new LinearDoubleKeyFrame(value, keyTime),
            "ease-in" => new EasingDoubleKeyFrame(
                value,
                keyTime,
                new CubicEase { EasingMode = EasingMode.EaseIn }),
            "ease-out" => new EasingDoubleKeyFrame(
                value,
                keyTime,
                new CubicEase { EasingMode = EasingMode.EaseOut }),
            _ => new EasingDoubleKeyFrame(
                value,
                keyTime,
                new CubicEase { EasingMode = EasingMode.EaseInOut }),
        };
}
