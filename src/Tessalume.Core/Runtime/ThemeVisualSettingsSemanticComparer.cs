namespace Tessalume.Core.Runtime;

/// <summary>
/// Structural equality for resolved artwork state. Record equality is intentionally not
/// used because gradient, responsive, and motion collections are exposed as IReadOnlyList
/// and therefore otherwise compare by reference.
/// </summary>
public sealed class ThemeVisualSettingsSemanticComparer : IEqualityComparer<ThemeVisualSettings>
{
    public static ThemeVisualSettingsSemanticComparer Instance { get; } = new();

    private ThemeVisualSettingsSemanticComparer()
    {
    }

    public bool Equals(ThemeVisualSettings? x, ThemeVisualSettings? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        var left = x.Normalize();
        var right = y.Normalize();
        return ModeEquals(left.Light, right.Light) &&
               ModeEquals(left.Dark, right.Dark) &&
               left.Display == right.Display;
    }

    public int GetHashCode(ThemeVisualSettings obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var value = obj.Normalize();
        var hash = new HashCode();
        AddMode(ref hash, value.Light);
        AddMode(ref hash, value.Dark);
        hash.Add(value.Display);
        return hash.ToHashCode();
    }

    public static bool AdjustmentEquals(
        ThemeArtworkAdjustment? left,
        ThemeArtworkAdjustment? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        var first = left.Normalize();
        var second = right.Normalize();
        return first.CustomImagePath == second.CustomImagePath &&
               first.ThemeAssetKey == second.ThemeAssetKey &&
               first.CompositionMode == second.CompositionMode &&
               first.Placement == second.Placement &&
               first.Brightness == second.Brightness &&
               first.Contrast == second.Contrast &&
               first.Saturation == second.Saturation &&
               first.Opacity == second.Opacity &&
               first.Zoom == second.Zoom &&
               first.OffsetX == second.OffsetX &&
               first.OffsetY == second.OffsetY &&
               first.Grayscale == second.Grayscale &&
               first.HueRotation == second.HueRotation &&
               first.Blur == second.Blur &&
               first.OverlayColor == second.OverlayColor &&
               first.OverlayOpacity == second.OverlayOpacity &&
               first.GradientStrength == second.GradientStrength &&
               first.Vignette == second.Vignette &&
               first.BlendMode == second.BlendMode &&
               first.ReadabilityProtection == second.ReadabilityProtection &&
               GradientEquals(first.GradientVeil, second.GradientVeil) &&
               ReadabilityEquals(first.ReadabilityVeil, second.ReadabilityVeil) &&
               VariantsEqual(first.ResponsiveVariants, second.ResponsiveVariants) &&
               MotionEquals(first.Motion, second.Motion);
    }

    private static bool ModeEquals(ThemeVisualModeSettings left, ThemeVisualModeSettings right) =>
        AdjustmentEquals(left.Hero, right.Hero) &&
        AdjustmentEquals(left.Sidebar, right.Sidebar) &&
        AdjustmentEquals(left.Chat, right.Chat);

    private static bool GradientEquals(
        ThemeArtworkGradientVeil? left,
        ThemeArtworkGradientVeil? right)
    {
        var first = (left ?? new ThemeArtworkGradientVeil()).Normalize();
        var second = (right ?? new ThemeArtworkGradientVeil()).Normalize();
        if (first.Enabled != second.Enabled ||
            first.Strength != second.Strength ||
            first.Layers.Count != second.Layers.Count) return false;
        for (var index = 0; index < first.Layers.Count; index++)
        {
            var a = first.Layers[index];
            var b = second.Layers[index];
            if (a.DirectionDeg != b.DirectionDeg || a.Start != b.Start || a.End != b.End ||
                a.Stops.Count != b.Stops.Count) return false;
            for (var stopIndex = 0; stopIndex < a.Stops.Count; stopIndex++)
            {
                if (a.Stops[stopIndex] != b.Stops[stopIndex]) return false;
            }
        }
        return true;
    }

    private static bool ReadabilityEquals(
        ThemeArtworkReadabilityVeil? left,
        ThemeArtworkReadabilityVeil? right) =>
        (left ?? new ThemeArtworkReadabilityVeil()).Normalize() ==
        (right ?? new ThemeArtworkReadabilityVeil()).Normalize();

    private static bool VariantsEqual(
        IReadOnlyList<ThemeArtworkResponsiveVariant> left,
        IReadOnlyList<ThemeArtworkResponsiveVariant> right)
    {
        var first = (left ?? []).Select(value => value.Normalize()).ToArray();
        var second = (right ?? []).Select(value => value.Normalize()).ToArray();
        if (first.Length != second.Length) return false;
        for (var index = 0; index < first.Length; index++)
        {
            var a = first[index];
            var b = second[index];
            if (a.MinWidth != b.MinWidth || a.MaxWidth != b.MaxWidth ||
                !GradientEquals(a.GradientVeil, b.GradientVeil) ||
                !ReadabilityEquals(a.ReadabilityVeil, b.ReadabilityVeil)) return false;
        }
        return true;
    }

    private static bool MotionEquals(ThemeArtworkMotion? left, ThemeArtworkMotion? right)
    {
        var first = NormalizeActiveMotion(left);
        var second = NormalizeActiveMotion(right);
        if (first is null || second is null) return first is null && second is null;
        if (first.DurationMs != second.DurationMs ||
            first.Easing != second.Easing ||
            first.Direction != second.Direction ||
            first.Keyframes.Count != second.Keyframes.Count) return false;
        for (var index = 0; index < first.Keyframes.Count; index++)
        {
            if (first.Keyframes[index] != second.Keyframes[index]) return false;
        }
        return true;
    }

    private static ThemeArtworkMotion? NormalizeActiveMotion(ThemeArtworkMotion? motion)
    {
        var normalized = motion?.Normalize();
        return normalized is { Mode: "loop" } ? normalized : null;
    }

    private static void AddMode(ref HashCode hash, ThemeVisualModeSettings mode)
    {
        AddAdjustment(ref hash, mode.Hero);
        AddAdjustment(ref hash, mode.Sidebar);
        AddAdjustment(ref hash, mode.Chat);
    }

    private static void AddAdjustment(ref HashCode hash, ThemeArtworkAdjustment adjustment)
    {
        var value = adjustment.Normalize();
        hash.Add(value.CustomImagePath);
        hash.Add(value.ThemeAssetKey);
        hash.Add(value.CompositionMode);
        hash.Add(value.Placement);
        hash.Add(value.Brightness);
        hash.Add(value.Contrast);
        hash.Add(value.Saturation);
        hash.Add(value.Opacity);
        hash.Add(value.Zoom);
        hash.Add(value.OffsetX);
        hash.Add(value.OffsetY);
        hash.Add(value.Grayscale);
        hash.Add(value.HueRotation);
        hash.Add(value.Blur);
        hash.Add(value.OverlayColor);
        hash.Add(value.OverlayOpacity);
        hash.Add(value.GradientStrength);
        hash.Add(value.Vignette);
        hash.Add(value.BlendMode);
        hash.Add(value.ReadabilityProtection);
        AddGradient(ref hash, value.GradientVeil);
        hash.Add(value.ReadabilityVeil.Normalize());
        foreach (var variant in value.ResponsiveVariants)
        {
            var item = variant.Normalize();
            hash.Add(item.MinWidth);
            hash.Add(item.MaxWidth);
            AddGradient(ref hash, item.GradientVeil);
            hash.Add(item.ReadabilityVeil?.Normalize());
        }
        var motion = NormalizeActiveMotion(value.Motion);
        if (motion is null) return;
        hash.Add(motion.DurationMs);
        hash.Add(motion.Easing);
        hash.Add(motion.Direction);
        foreach (var frame in motion.Keyframes) hash.Add(frame);
    }

    private static void AddGradient(ref HashCode hash, ThemeArtworkGradientVeil? veil)
    {
        var value = (veil ?? new ThemeArtworkGradientVeil()).Normalize();
        hash.Add(value.Enabled);
        hash.Add(value.Strength);
        foreach (var layer in value.Layers)
        {
            hash.Add(layer.DirectionDeg);
            hash.Add(layer.Start);
            hash.Add(layer.End);
            foreach (var stop in layer.Stops) hash.Add(stop);
        }
    }
}
