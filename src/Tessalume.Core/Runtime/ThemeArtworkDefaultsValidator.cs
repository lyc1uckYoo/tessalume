using System.IO;

namespace Tessalume.Core.Runtime;

/// <summary>
/// Strict validation for theme-authored defaults. Unlike Normalize, this never repairs
/// authored data: a package with an invalid token must fall back visibly instead of being
/// reported as an exact preview.
/// </summary>
public static class ThemeArtworkDefaultsValidator
{
    public static void Validate(ThemeArtworkDefaultsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var slots = document.Slots ??
            throw Invalid("The artwork defaults slot map is required.");
        ValidateModes(slots.Hero, "hero");
        ValidateModes(slots.Sidebar, "sidebar");
        ValidateModes(slots.Chat, "chat");
    }

    public static void ValidateAdjustment(ThemeArtworkAdjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        if (!Enum.IsDefined(adjustment.CompositionMode))
            throw Invalid("The artwork composition mode is invalid.");
        RequireRange(adjustment.Brightness, 20d, 180d, "brightness");
        RequireRange(adjustment.Contrast, 20d, 180d, "contrast");
        RequireRange(adjustment.Saturation, 0d, 200d, "saturation");
        RequireRange(adjustment.Opacity, 0d, 100d, "opacity");
        RequireRange(adjustment.Zoom, 70d, 200d, "legacy zoom");
        RequireRange(adjustment.OffsetX, -200d, 200d, "legacy X offset");
        RequireRange(adjustment.OffsetY, -200d, 200d, "legacy Y offset");
        RequireRange(adjustment.Grayscale, 0d, 100d, "grayscale");
        RequireRange(adjustment.HueRotation, -180d, 180d, "hue rotation");
        RequireRange(adjustment.Blur, 0d, 20d, "blur");
        RequireColor(adjustment.OverlayColor, "overlay color");
        RequireRange(adjustment.OverlayOpacity, 0d, 100d, "overlay opacity");
        RequireRange(adjustment.GradientStrength, 0d, 100d, "legacy gradient");
        RequireRange(adjustment.Vignette, 0d, 100d, "vignette");
        if (!ThemeArtworkAdjustment.IsSupportedBlendMode(adjustment.BlendMode))
        {
            throw Invalid("The artwork blend mode is unsupported.");
        }
        ValidatePlacementSpec(adjustment.Placement ??
            throw Invalid("The artwork placement is required."));
        ValidateGradient(adjustment.GradientVeil, "gradient veil", 8, 16);
        ValidateReadability(adjustment.ReadabilityVeil, "readability veil");
        ValidateVariants(adjustment.ResponsiveVariants, 16, 8, 16);
        if (adjustment.Motion is not null) ValidateMotion(adjustment.Motion);
    }

    private static void ValidateModes(ThemeArtworkDefaultSlotModes? modes, string region)
    {
        if (modes is null) throw Invalid($"The {region} mode pair is required.");
        ValidateSlot(modes.Light, $"{region}/light");
        ValidateSlot(modes.Dark, $"{region}/dark");
    }

    private static void ValidateSlot(ThemeArtworkDefaultSlot? slot, string name)
    {
        if (slot is null) throw Invalid($"The {name} artwork default is required.");
        var asset = slot.Asset ?? string.Empty;
        if (asset.Length is 0 or > 64 ||
            asset != asset.Trim() ||
            !char.IsLetterOrDigit(asset[0]) ||
            asset.Any(character => !char.IsLetterOrDigit(character) &&
                                   character is not '.' and not '_' and not '-'))
        {
            throw Invalid($"The {name} asset key is invalid.");
        }
        ValidateCssPlacement(slot.Placement, name);
        ValidateEffects(slot.Effects, name);
        ValidateVariants(slot.ResponsiveVariants, 8, 4, 8);
        if (slot.Motion is not null) ValidateMotion(slot.Motion);
    }

    private static void ValidateCssPlacement(ThemeArtworkCssPlacement? placement, string name)
    {
        if (placement is null) throw Invalid($"The {name} placement is required.");
        RequireCanonicalToken(placement.Size?.Width, $"{name} width");
        RequireCanonicalToken(placement.Size?.Height, $"{name} height");
        RequireCanonicalToken(placement.Position?.X, $"{name} X position");
        RequireCanonicalToken(placement.Position?.Y, $"{name} Y position");
        RequireCanonicalToken(placement.Origin?.X, $"{name} X origin");
        RequireCanonicalToken(placement.Origin?.Y, $"{name} Y origin");
        try
        {
            _ = ThemeArtworkPlacementParser.Parse(placement);
        }
        catch (FormatException exception)
        {
            throw Invalid($"The {name} placement is invalid: {exception.Message}");
        }
    }

    private static void ValidatePlacementSpec(ThemeArtworkPlacementSpec placement)
    {
        if (!Enum.IsDefined(placement.SizeMode)) throw Invalid("The artwork size mode is invalid.");
        if (placement.SizeMode == ThemeArtworkSizeMode.Explicit)
        {
            ValidateLength(placement.Width, "artwork width");
            ValidateLength(placement.Height, "artwork height");
        }
        ValidatePosition(placement.PositionX, "artwork X position");
        ValidatePosition(placement.PositionY, "artwork Y position");
        var geometry = placement.Geometry ?? throw Invalid("Artwork geometry is required.");
        RequireRange(geometry.Scale, .1d, 10d, "artwork geometry scale");
        ValidatePosition(geometry.OriginX, "artwork X origin");
        ValidatePosition(geometry.OriginY, "artwork Y origin");
    }

    private static void ValidateLength(ThemeArtworkLength length, string name)
    {
        if (!Enum.IsDefined(length.Unit)) throw Invalid($"The {name} unit is invalid.");
        if (length.Unit != ThemeArtworkLengthUnit.Auto)
        {
            RequireRange(length.Value, .1d, 100000d, name);
        }
    }

    private static void ValidatePosition(ThemeArtworkPositionValue position, string name)
    {
        if (!Enum.IsDefined(position.Kind)) throw Invalid($"The {name} kind is invalid.");
        if (position.Kind is ThemeArtworkPositionKind.Percent or ThemeArtworkPositionKind.Pixels)
        {
            RequireRange(position.Value, -100000d, 100000d, name);
        }
    }

    private static void ValidateEffects(ThemeArtworkDefaultEffects? effects, string name)
    {
        if (effects is null) throw Invalid($"The {name} effects are required.");
        RequireRange(effects.Brightness, 20d, 180d, $"{name} brightness");
        RequireRange(effects.Contrast, 20d, 180d, $"{name} contrast");
        RequireRange(effects.Saturation, 0d, 200d, $"{name} saturation");
        RequireRange(effects.Opacity, 0d, 100d, $"{name} opacity");
        RequireRange(effects.Grayscale, 0d, 100d, $"{name} grayscale");
        RequireRange(effects.HueRotate, -180d, 180d, $"{name} hue rotation");
        RequireRange(effects.Blur, 0d, 20d, $"{name} blur");
        RequireRange(effects.Vignette, 0d, 100d, $"{name} vignette");
        if (!ThemeArtworkAdjustment.IsSupportedBlendMode(effects.BlendMode) ||
            effects.BlendMode != effects.BlendMode.Trim() ||
            effects.BlendMode.Any(char.IsUpper))
        {
            throw Invalid($"The {name} blend mode is unsupported.");
        }
        var overlay = effects.Overlay ?? throw Invalid($"The {name} overlay is required.");
        RequireColor(overlay.Color, $"{name} overlay color");
        RequireRange(overlay.Opacity, 0d, 100d, $"{name} overlay opacity");
        ValidateGradient(effects.GradientVeil, $"{name} gradient veil", 4, 8);
        ValidateReadability(effects.ReadabilityVeil, $"{name} readability veil");
    }

    private static void ValidateGradient(
        ThemeArtworkGradientVeil? veil,
        string name,
        int maximumLayers,
        int maximumStops)
    {
        if (veil is null) throw Invalid($"The {name} is required.");
        RequireRange(veil.Strength, 0d, 100d, $"{name} strength");
        var layers = veil.Layers ?? throw Invalid($"The {name} layers are required.");
        if (layers.Count > maximumLayers || veil.Enabled && layers.Count == 0)
        {
            throw Invalid($"The {name} layer count is invalid.");
        }
        foreach (var layer in layers)
        {
            if (layer is null) throw Invalid($"The {name} contains a null layer.");
            RequireRange(layer.DirectionDeg, -360d, 360d, $"{name} direction");
            RequireRange(layer.Start, 0d, 100d, $"{name} start");
            RequireRange(layer.End, 0d, 100d, $"{name} end");
            if (layer.End < layer.Start) throw Invalid($"The {name} range is reversed.");
            var stops = layer.Stops ?? throw Invalid($"The {name} stops are required.");
            if (stops.Count is < 2 || stops.Count > maximumStops)
            {
                throw Invalid($"The {name} stop count is invalid.");
            }
            var previous = -1d;
            foreach (var stop in stops)
            {
                if (stop is null) throw Invalid($"The {name} contains a null stop.");
                RequireRange(stop.Position, 0d, 100d, $"{name} stop position");
                if (stop.Position < previous) throw Invalid($"The {name} stops are out of order.");
                previous = stop.Position;
                RequireColor(stop.Color, $"{name} stop color");
                RequireRange(stop.Opacity, 0d, 100d, $"{name} stop opacity");
            }
        }
    }

    private static void ValidateReadability(ThemeArtworkReadabilityVeil? veil, string name)
    {
        if (veil is null) throw Invalid($"The {name} is required.");
        RequireColor(veil.Color, $"{name} color");
        RequireRange(veil.Opacity, 0d, 100d, $"{name} opacity");
        RequireRange(veil.DirectionDeg, -360d, 360d, $"{name} direction");
        RequireRange(veil.RangeStart, 0d, 100d, $"{name} range start");
        RequireRange(veil.RangeEnd, 0d, 100d, $"{name} range end");
        if (veil.RangeEnd < veil.RangeStart) throw Invalid($"The {name} range is reversed.");
    }

    private static void ValidateVariants(
        IReadOnlyList<ThemeArtworkResponsiveVariant>? variants,
        int maximumVariants,
        int maximumLayers,
        int maximumStops)
    {
        if (variants is null) throw Invalid("The artwork responsive variant list is required.");
        if (variants.Count > maximumVariants) throw Invalid("There are too many artwork variants.");
        foreach (var variant in variants)
        {
            if (variant is null) throw Invalid("An artwork responsive variant is null.");
            if (variant.MinWidth is null && variant.MaxWidth is null)
            {
                throw Invalid("An artwork responsive variant needs a viewport bound.");
            }
            if (variant.GradientVeil is null && variant.ReadabilityVeil is null)
            {
                throw Invalid("An artwork responsive variant needs a veil override.");
            }
            if (variant.MinWidth is { } minimum)
                RequireRange(minimum, 1d, 10000d, "variant minimum width");
            if (variant.MaxWidth is { } maximum)
                RequireRange(maximum, 1d, 10000d, "variant maximum width");
            if (variant.MinWidth is { } min && variant.MaxWidth is { } max && max < min)
                throw Invalid("An artwork responsive variant range is reversed.");
            if (variant.GradientVeil is not null)
                ValidateGradient(variant.GradientVeil, "variant gradient veil", maximumLayers, maximumStops);
            if (variant.ReadabilityVeil is not null)
                ValidateReadability(variant.ReadabilityVeil, "variant readability veil");
        }
    }

    private static void ValidateMotion(ThemeArtworkMotion motion)
    {
        var mode = (motion.Mode ?? string.Empty).Trim().ToLowerInvariant();
        if (mode is not "none" and not "loop" || motion.Mode != mode)
            throw Invalid("The artwork motion mode is invalid.");
        if (mode == "none") return;
        RequireRange(motion.DurationMs, 100d, 300000d, "motion duration");
        if (motion.Easing is not ("linear" or "ease" or "ease-in" or "ease-out" or "ease-in-out"))
            throw Invalid("The artwork motion easing is invalid.");
        if (motion.Direction is not ("normal" or "reverse" or "alternate" or "alternate-reverse"))
            throw Invalid("The artwork motion direction is invalid.");
        var frames = motion.Keyframes ?? throw Invalid("The artwork motion keyframes are required.");
        if (frames.Count is < 2 or > 16 || frames[0].At != 0d || frames[^1].At != 100d)
            throw Invalid("Artwork motion must contain 2-16 frames from 0 through 100.");
        var previous = -1d;
        foreach (var frame in frames)
        {
            if (frame is null) throw Invalid("An artwork motion keyframe is null.");
            RequireRange(frame.At, 0d, 100d, "motion keyframe offset");
            if (frame.At <= previous) throw Invalid("Artwork motion keyframes are not increasing.");
            previous = frame.At;
            RequireMotionDelta(frame.TranslateX, "motion X translation");
            RequireMotionDelta(frame.TranslateY, "motion Y translation");
            RequireRange(frame.ScaleDelta, -.9d, 1d, "motion scale delta");
            RequireRange(frame.OpacityDelta, -100d, 100d, "motion opacity delta");
        }
    }

    private static void RequireMotionDelta(string? value, string name)
    {
        var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (candidate != value ||
            !ThemeArtworkCssToken.TryReadLength(candidate, allowNegative: true, out var number) ||
            Math.Abs(number) > 10000d)
        {
            throw Invalid($"The {name} is invalid.");
        }
    }

    private static void RequireCanonicalToken(string? value, string name)
    {
        if (string.IsNullOrEmpty(value) || value != value.Trim() || value.Any(char.IsUpper))
            throw Invalid($"The {name} token is not canonical.");
    }

    private static void RequireColor(string? value, string name)
    {
        if (value is null || value.Length != 7 || value[0] != '#' || !value[1..].All(Uri.IsHexDigit))
            throw Invalid($"The {name} is invalid.");
    }

    private static void RequireRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw Invalid($"The {name} is outside the supported range.");
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
