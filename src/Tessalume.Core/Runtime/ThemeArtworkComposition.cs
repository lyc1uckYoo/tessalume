using System.Text.Json.Serialization;

namespace Tessalume.Core.Runtime;

[JsonConverter(typeof(JsonStringEnumConverter<ThemeArtworkCompositionMode>))]
public enum ThemeArtworkCompositionMode
{
    Theme,
    Legacy,
    Custom,
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemeArtworkPlacementMode>))]
public enum ThemeArtworkPlacementMode
{
    Crop,
    Contain,
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemeArtworkImageSourceMode>))]
public enum ThemeArtworkImageSourceMode
{
    Theme,
    Custom,
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemeArtworkSizeMode>))]
public enum ThemeArtworkSizeMode
{
    Cover,
    Contain,
    Explicit,
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemeArtworkLengthUnit>))]
public enum ThemeArtworkLengthUnit
{
    Auto,
    Percent,
    Pixels,
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemeArtworkPositionKind>))]
public enum ThemeArtworkPositionKind
{
    Start,
    Center,
    End,
    Percent,
    Pixels,
}

public readonly record struct ThemeArtworkLength(ThemeArtworkLengthUnit Unit, double Value = 0d)
{
    public static ThemeArtworkLength Auto => new(ThemeArtworkLengthUnit.Auto);

    public static ThemeArtworkLength Percent(double value) =>
        new ThemeArtworkLength(ThemeArtworkLengthUnit.Percent, value).Normalize();

    public static ThemeArtworkLength Pixels(double value) =>
        new ThemeArtworkLength(ThemeArtworkLengthUnit.Pixels, value).Normalize();

    public ThemeArtworkLength Normalize() => Unit switch
    {
        ThemeArtworkLengthUnit.Percent => new(
            Unit,
            double.IsFinite(Value) ? Math.Clamp(Value, .1d, 100000d) : 100d),
        ThemeArtworkLengthUnit.Pixels => new(
            Unit,
            double.IsFinite(Value) ? Math.Clamp(Value, .1d, 100000d) : 1d),
        _ => Auto,
    };

    public string ToCss() => Normalize() switch
    {
        { Unit: ThemeArtworkLengthUnit.Percent, Value: var value } =>
            $"{value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}%",
        { Unit: ThemeArtworkLengthUnit.Pixels, Value: var value } =>
            $"{value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}px",
        _ => "auto",
    };
}

public readonly record struct ThemeArtworkPositionValue(
    ThemeArtworkPositionKind Kind,
    double Value = 0d)
{
    public static ThemeArtworkPositionValue Start => new(ThemeArtworkPositionKind.Start);

    public static ThemeArtworkPositionValue Center => new(ThemeArtworkPositionKind.Center);

    public static ThemeArtworkPositionValue End => new(ThemeArtworkPositionKind.End);

    public static ThemeArtworkPositionValue Percent(double value) =>
        new ThemeArtworkPositionValue(ThemeArtworkPositionKind.Percent, value).Normalize();

    public static ThemeArtworkPositionValue Pixels(double value) =>
        new ThemeArtworkPositionValue(ThemeArtworkPositionKind.Pixels, value).Normalize();

    public ThemeArtworkPositionValue Normalize() => Kind switch
    {
        ThemeArtworkPositionKind.Percent => new(
            Kind,
            double.IsFinite(Value) ? Math.Clamp(Value, -100000d, 100000d) : 50d),
        ThemeArtworkPositionKind.Pixels => new(
            Kind,
            double.IsFinite(Value) ? Math.Clamp(Value, -100000d, 100000d) : 0d),
        ThemeArtworkPositionKind.Start => Start,
        ThemeArtworkPositionKind.End => End,
        _ => Center,
    };

    public string ToCss(bool horizontal) => Normalize() switch
    {
        { Kind: ThemeArtworkPositionKind.Start } => horizontal ? "left" : "top",
        { Kind: ThemeArtworkPositionKind.End } => horizontal ? "right" : "bottom",
        { Kind: ThemeArtworkPositionKind.Percent, Value: var value } =>
            $"{value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}%",
        { Kind: ThemeArtworkPositionKind.Pixels, Value: var value } =>
            $"{value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}px",
        _ => "center",
    };
}

public sealed record ThemeArtworkGeometry
{
    public double Scale { get; init; } = 1d;

    public ThemeArtworkPositionValue OriginX { get; init; } =
        ThemeArtworkPositionValue.Center;

    public ThemeArtworkPositionValue OriginY { get; init; } =
        ThemeArtworkPositionValue.Center;

    public bool MirrorX { get; init; }

    public bool MirrorY { get; init; }

    public ThemeArtworkGeometry Normalize() => this with
    {
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, .1d, 10d) : 1d,
        OriginX = OriginX.Normalize(),
        OriginY = OriginY.Normalize(),
    };
}

/// <summary>
/// Canonical, round-trippable CSS-semantic placement used by both theme defaults and
/// custom user composition. A source crop is a projection of this value for one image
/// and surface size, never the persisted truth.
/// </summary>
public sealed record ThemeArtworkPlacementSpec
{
    public ThemeArtworkSizeMode SizeMode { get; init; } = ThemeArtworkSizeMode.Cover;

    public ThemeArtworkLength Width { get; init; } = ThemeArtworkLength.Auto;

    public ThemeArtworkLength Height { get; init; } = ThemeArtworkLength.Auto;

    public ThemeArtworkPositionValue PositionX { get; init; } =
        ThemeArtworkPositionValue.Center;

    public ThemeArtworkPositionValue PositionY { get; init; } =
        ThemeArtworkPositionValue.Center;

    public ThemeArtworkGeometry Geometry { get; init; } = new();

    public ThemeArtworkPlacementSpec Normalize() => this with
    {
        Width = Width.Normalize(),
        Height = Height.Normalize(),
        PositionX = PositionX.Normalize(),
        PositionY = PositionY.Normalize(),
        Geometry = (Geometry ?? new ThemeArtworkGeometry()).Normalize(),
    };

    public string SizeCss => SizeMode switch
    {
        ThemeArtworkSizeMode.Contain => "contain",
        ThemeArtworkSizeMode.Explicit => $"{Width.ToCss()} {Height.ToCss()}",
        _ => "cover",
    };

    public string PositionCss => $"{PositionX.ToCss(horizontal: true)} " +
        PositionY.ToCss(horizontal: false);
}

public static class ThemeArtworkPlacementParser
{
    public static ThemeArtworkPlacementSpec Parse(ThemeArtworkCssPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var size = placement.Size ?? throw new FormatException("Artwork placement size is required.");
        var position = placement.Position ??
            throw new FormatException("Artwork placement position is required.");
        var origin = placement.Origin ??
            throw new FormatException("Artwork placement transform origin is required.");
        var widthToken = (size.Width ?? string.Empty).Trim().ToLowerInvariant();
        var heightToken = (size.Height ?? string.Empty).Trim().ToLowerInvariant();
        var mode = widthToken switch
        {
            "contain" => ThemeArtworkSizeMode.Contain,
            "cover" => ThemeArtworkSizeMode.Cover,
            _ => ThemeArtworkSizeMode.Explicit,
        };
        if (mode is ThemeArtworkSizeMode.Cover or ThemeArtworkSizeMode.Contain &&
            heightToken != "auto")
        {
            throw new FormatException("Cover and contain artwork sizes require an auto height token.");
        }
        if (!double.IsFinite(placement.Scale) || placement.Scale is < .1d or > 10d)
        {
            throw new FormatException("Artwork placement scale must be between 0.1 and 10.");
        }
        return new ThemeArtworkPlacementSpec
        {
            SizeMode = mode,
            Width = mode == ThemeArtworkSizeMode.Explicit
                ? ParseLength(widthToken)
                : ThemeArtworkLength.Auto,
            Height = mode == ThemeArtworkSizeMode.Explicit
                ? ParseLength(heightToken)
                : ThemeArtworkLength.Auto,
            PositionX = ParsePosition(position.X, horizontal: true),
            PositionY = ParsePosition(position.Y, horizontal: false),
            Geometry = new ThemeArtworkGeometry
            {
                Scale = placement.Scale,
                OriginX = ParsePosition(origin.X, horizontal: true),
                OriginY = ParsePosition(origin.Y, horizontal: false),
                MirrorX = placement.MirrorX,
                MirrorY = placement.MirrorY,
            },
        };
    }

    public static ThemeArtworkLength ParseLength(string token)
    {
        var value = (token ?? string.Empty).Trim().ToLowerInvariant();
        if (value == "auto") return ThemeArtworkLength.Auto;
        if (!ThemeArtworkCssToken.TryReadLength(value, allowNegative: false, out var number))
        {
            throw new FormatException($"Unsupported artwork size token '{token}'.");
        }
        if (number > 100000d)
        {
            throw new FormatException($"Artwork size token '{token}' exceeds the supported range.");
        }
        return value.EndsWith('%')
            ? ThemeArtworkLength.Percent(number)
            : ThemeArtworkLength.Pixels(number);
    }

    public static ThemeArtworkPositionValue ParsePosition(string token, bool horizontal)
    {
        var value = (token ?? string.Empty).Trim().ToLowerInvariant();
        if (value == "center") return ThemeArtworkPositionValue.Center;
        if (value == (horizontal ? "left" : "top")) return ThemeArtworkPositionValue.Start;
        if (value == (horizontal ? "right" : "bottom")) return ThemeArtworkPositionValue.End;
        if (!ThemeArtworkCssToken.TryReadLength(value, allowNegative: true, out var number))
        {
            throw new FormatException($"Unsupported artwork position token '{token}'.");
        }
        if (Math.Abs(number) > 100000d)
        {
            throw new FormatException($"Artwork position token '{token}' exceeds the supported range.");
        }
        return value.EndsWith('%')
            ? ThemeArtworkPositionValue.Percent(number)
            : ThemeArtworkPositionValue.Pixels(number);
    }
}

/// <summary>
/// A resolution-independent, final source crop. Crop coordinates are fractions of the
/// source bitmap, not offsets layered on top of a theme CSS crop.
/// </summary>
public sealed record ThemeArtworkSourcePlacement
{
    public ThemeArtworkPlacementMode Mode { get; init; } = ThemeArtworkPlacementMode.Crop;

    public double SourceX { get; init; }

    public double SourceY { get; init; }

    public double SourceWidth { get; init; } = 1d;

    public double SourceHeight { get; init; } = 1d;

    public double AlignmentX { get; init; } = .5d;

    public double AlignmentY { get; init; } = .5d;

    public ThemeArtworkSourcePlacement Normalize()
    {
        var width = NormalizeFraction(SourceWidth, 1d, .001d, 1d);
        var height = NormalizeFraction(SourceHeight, 1d, .001d, 1d);
        return this with
        {
            SourceWidth = width,
            SourceHeight = height,
            SourceX = NormalizeFraction(SourceX, 0d, 0d, 1d - width),
            SourceY = NormalizeFraction(SourceY, 0d, 0d, 1d - height),
            AlignmentX = NormalizeFraction(AlignmentX, .5d, 0d, 1d),
            AlignmentY = NormalizeFraction(AlignmentY, .5d, 0d, 1d),
        };
    }

    private static double NormalizeFraction(
        double value,
        double fallback,
        double minimum,
        double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}

/// <summary>
/// Exact theme-authored placement tokens. The runtime resolves scale/origin into final
/// background size/position against the live surface and then neutralizes element transforms.
/// </summary>
public sealed record ThemeArtworkCssPlacement
{
    public ThemeArtworkCssSize Size { get; init; } = new();

    public ThemeArtworkCssPosition Position { get; init; } = new();

    public double Scale { get; init; } = 1d;

    public ThemeArtworkCssPosition Origin { get; init; } = new();

    public bool MirrorX { get; init; }

    public bool MirrorY { get; init; }

    public ThemeArtworkCssPlacement Normalize() => this with
    {
        Size = (Size ?? new ThemeArtworkCssSize()).Normalize(),
        Position = (Position ?? new ThemeArtworkCssPosition()).Normalize(),
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, .1d, 10d) : 1d,
        Origin = (Origin ?? new ThemeArtworkCssPosition()).Normalize(),
    };
}

public sealed record ThemeArtworkCssSize
{
    public string Width { get; init; } = "cover";

    public string Height { get; init; } = "auto";

    public ThemeArtworkCssSize Normalize() => this with
    {
        Width = ThemeArtworkCssToken.NormalizeSize(Width, "cover", allowKeyword: true),
        Height = ThemeArtworkCssToken.NormalizeSize(Height, "auto", allowKeyword: false),
    };
}

public sealed record ThemeArtworkCssPosition
{
    public string X { get; init; } = "50%";

    public string Y { get; init; } = "50%";

    public ThemeArtworkCssPosition Normalize() => this with
    {
        X = ThemeArtworkCssToken.NormalizePosition(X, "50%", horizontal: true),
        Y = ThemeArtworkCssToken.NormalizePosition(Y, "50%", horizontal: false),
    };
}

internal static class ThemeArtworkCssToken
{
    public static string NormalizeSize(string? value, string fallback, bool allowKeyword)
    {
        var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (candidate == "auto" ||
            (allowKeyword && candidate is "cover" or "contain") ||
            TryReadLength(candidate, allowNegative: false, out _))
        {
            return candidate;
        }
        return fallback;
    }

    public static string NormalizePosition(
        string? value,
        string fallback,
        bool horizontal)
    {
        var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (candidate == "center" ||
            (horizontal && candidate is "left" or "right") ||
            (!horizontal && candidate is "top" or "bottom") ||
            TryReadLength(candidate, allowNegative: true, out _))
        {
            return candidate;
        }
        return fallback;
    }

    public static bool TryReadLength(string value, bool allowNegative, out double number)
    {
        number = 0d;
        var suffixLength = value.EndsWith('%') ? 1 :
            value.EndsWith("px", StringComparison.Ordinal) ? 2 : 0;
        if (suffixLength == 0 || !double.TryParse(
                value[..^suffixLength],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out number) ||
            !double.IsFinite(number) ||
            (!allowNegative && number <= 0))
        {
            return false;
        }
        return true;
    }
}

public sealed record ThemeArtworkGradientStop
{
    public double Position { get; init; }

    public string Color { get; init; } = "#000000";

    public double Opacity { get; init; }

    public ThemeArtworkGradientStop Normalize() => this with
    {
        Position = NormalizePercent(Position),
        Color = ThemeArtworkValueNormalization.NormalizeColor(Color),
        Opacity = NormalizePercent(Opacity),
    };

    private static double NormalizePercent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : 0d;
}

public sealed record ThemeArtworkGradientLayer
{
    public double DirectionDeg { get; init; } = 90d;

    public double Start { get; init; }

    public double End { get; init; } = 100d;

    public IReadOnlyList<ThemeArtworkGradientStop> Stops { get; init; } = [];

    public ThemeArtworkGradientLayer Normalize()
    {
        var start = NormalizePercent(Start, 0d);
        var end = NormalizePercent(End, 100d);
        if (end < start) (start, end) = (end, start);
        return this with
        {
            DirectionDeg = double.IsFinite(DirectionDeg)
                ? Math.Clamp(DirectionDeg, -360d, 360d)
                : 90d,
            Start = start,
            End = end,
            Stops = (Stops ?? [])
                .Where(stop => stop is not null)
                .Take(16)
                .Select(stop => stop.Normalize())
                .OrderBy(stop => stop.Position)
                .ToArray(),
        };
    }

    private static double NormalizePercent(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : fallback;
}

public sealed record ThemeArtworkGradientVeil
{
    public bool Enabled { get; init; }

    public double Strength { get; init; }

    public IReadOnlyList<ThemeArtworkGradientLayer> Layers { get; init; } = [];

    public ThemeArtworkGradientVeil Normalize() => this with
    {
        Strength = double.IsFinite(Strength) ? Math.Clamp(Strength, 0d, 100d) : 0d,
        Layers = (Layers ?? [])
            .Where(layer => layer is not null)
            .Take(8)
            .Select(layer => layer.Normalize())
            .ToArray(),
    };
}

public sealed record ThemeArtworkReadabilityVeil
{
    public bool Enabled { get; init; }

    public string Color { get; init; } = "#000000";

    public double Opacity { get; init; }

    public double DirectionDeg { get; init; } = 90d;

    public double RangeStart { get; init; }

    public double RangeEnd { get; init; } = 100d;

    public ThemeArtworkReadabilityVeil Normalize()
    {
        var start = NormalizePercent(RangeStart, 0d);
        var end = NormalizePercent(RangeEnd, 100d);
        if (end < start) (start, end) = (end, start);
        return this with
        {
            Color = ThemeArtworkValueNormalization.NormalizeColor(Color),
            Opacity = NormalizePercent(Opacity, 0d),
            DirectionDeg = double.IsFinite(DirectionDeg)
                ? Math.Clamp(DirectionDeg, -360d, 360d)
                : 90d,
            RangeStart = start,
            RangeEnd = end,
        };
    }

    private static double NormalizePercent(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : fallback;
}

public sealed record ThemeArtworkResponsiveVariant
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinWidth { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxWidth { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkGradientVeil? GradientVeil { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkReadabilityVeil? ReadabilityVeil { get; init; }

    public ThemeArtworkResponsiveVariant Normalize()
    {
        var min = NormalizeWidth(MinWidth);
        var max = NormalizeWidth(MaxWidth);
        if (min is { } minimum && max is { } maximum && maximum < minimum)
        {
            (min, max) = (max, min);
        }
        return this with
        {
            MinWidth = min,
            MaxWidth = max,
            GradientVeil = GradientVeil?.Normalize(),
            ReadabilityVeil = ReadabilityVeil?.Normalize(),
        };
    }

    private static double? NormalizeWidth(double? value) =>
        value is { } width && double.IsFinite(width)
            ? Math.Clamp(width, 1d, 10000d)
            : null;
}

/// <summary>
/// Theme-authored motion relative to the already resolved final composition. It is
/// deliberately unable to express another crop or placement.
/// </summary>
public sealed record ThemeArtworkMotion
{
    public string Mode { get; init; } = "none";

    public double DurationMs { get; init; } = 1000d;

    public string Easing { get; init; } = "ease-in-out";

    public string Direction { get; init; } = "alternate";

    public IReadOnlyList<ThemeArtworkMotionKeyframe> Keyframes { get; init; } = [];

    public ThemeArtworkMotion Normalize()
    {
        var mode = string.Equals(Mode, "loop", StringComparison.OrdinalIgnoreCase)
            ? "loop"
            : "none";
        return this with
        {
            Mode = mode,
            DurationMs = double.IsFinite(DurationMs)
                ? Math.Clamp(DurationMs, 100d, 300000d)
                : 1000d,
            Easing = NormalizeChoice(
                Easing,
                ["linear", "ease", "ease-in", "ease-out", "ease-in-out"],
                "ease-in-out"),
            Direction = NormalizeChoice(
                Direction,
                ["normal", "reverse", "alternate", "alternate-reverse"],
                "alternate"),
            Keyframes = mode == "loop"
                ? (Keyframes ?? [])
                    .Where(frame => frame is not null)
                    .Take(16)
                    .Select(frame => frame.Normalize())
                    .OrderBy(frame => frame.At)
                    .ToArray()
                : [],
        };
    }

    private static string NormalizeChoice(
        string? value,
        IReadOnlyList<string> supported,
        string fallback)
    {
        var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
        return supported.Contains(candidate, StringComparer.Ordinal) ? candidate : fallback;
    }
}

public sealed record ThemeArtworkMotionKeyframe
{
    public double At { get; init; }

    public string TranslateX { get; init; } = "0px";

    public string TranslateY { get; init; } = "0px";

    public double ScaleDelta { get; init; }

    public double OpacityDelta { get; init; }

    public ThemeArtworkMotionKeyframe Normalize() => this with
    {
        At = double.IsFinite(At) ? Math.Clamp(At, 0d, 100d) : 0d,
        TranslateX = NormalizeDelta(TranslateX),
        TranslateY = NormalizeDelta(TranslateY),
        ScaleDelta = double.IsFinite(ScaleDelta)
            ? Math.Clamp(ScaleDelta, -.9d, 1d)
            : 0d,
        OpacityDelta = double.IsFinite(OpacityDelta)
            ? Math.Clamp(OpacityDelta, -100d, 100d)
            : 0d,
    };

    internal static string NormalizeDelta(string? value)
    {
        var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
        return ThemeArtworkCssToken.TryReadLength(candidate, allowNegative: true, out var number) &&
               Math.Abs(number) <= 10000d
            ? candidate
            : "0px";
    }
}

internal static class ThemeArtworkValueNormalization
{
    public static string NormalizeColor(string? color)
    {
        var value = (color ?? string.Empty).Trim();
        return value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit)
            ? value.ToUpperInvariant()
            : "#000000";
    }
}
