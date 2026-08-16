using System.Text.Json.Serialization;

namespace Tessalume.Core.Runtime;

public sealed record ThemeArtworkAdjustment
{
    private static readonly HashSet<string> SupportedBlendModes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "normal",
            "multiply",
            "screen",
            "overlay",
            "darken",
            "lighten",
            "color-dodge",
            "color-burn",
            "hard-light",
            "soft-light",
            "difference",
            "exclusion",
            "hue",
            "saturation",
            "color",
            "luminosity",
            "plus-lighter",
        };

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomImagePath { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThemeAssetKey { get; init; }

    public ThemeArtworkCompositionMode CompositionMode { get; init; } =
        ThemeArtworkCompositionMode.Theme;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkPlacementSpec? Placement { get; init; } = new();

    public double Brightness { get; init; } = 100d;

    public double Contrast { get; init; } = 100d;

    public double Saturation { get; init; } = 100d;

    public double Opacity { get; init; } = 100d;

    public double Zoom { get; init; } = 100d;

    public double OffsetX { get; init; }

    public double OffsetY { get; init; }

    public double Grayscale { get; init; }

    public double HueRotation { get; init; }

    public double Blur { get; init; }

    public string OverlayColor { get; init; } = "#000000";

    public double OverlayOpacity { get; init; }

    public double GradientStrength { get; init; }

    public double Vignette { get; init; }

    public string BlendMode { get; init; } = "normal";

    public bool ReadabilityProtection { get; init; }

    public ThemeArtworkGradientVeil GradientVeil { get; init; } = new();

    public ThemeArtworkReadabilityVeil ReadabilityVeil { get; init; } = new();

    public IReadOnlyList<ThemeArtworkResponsiveVariant> ResponsiveVariants { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkMotion? Motion { get; init; }

    public ThemeArtworkAdjustment Normalize()
    {
        var zoom = NormalizeValue(Zoom, 100d, 70d, 200d);
        var offsetX = NormalizeValue(OffsetX, 0d, -200d, 200d);
        var offsetY = NormalizeValue(OffsetY, 0d, -200d, 200d);
        var compositionMode = CompositionMode;
        // Old callers and schema-five JSON have no CompositionMode. Preserve their
        // non-default transform exactly instead of silently reinterpreting it.
        if (compositionMode == ThemeArtworkCompositionMode.Theme &&
            (Math.Abs(zoom - 100d) > .000001d ||
             Math.Abs(offsetX) > .000001d ||
             Math.Abs(offsetY) > .000001d))
        {
            compositionMode = ThemeArtworkCompositionMode.Legacy;
        }

        if (compositionMode != ThemeArtworkCompositionMode.Legacy)
        {
            zoom = 100d;
            offsetX = 0d;
            offsetY = 0d;
        }

        return this with
        {
            CompositionMode = compositionMode,
            Placement = (Placement ?? new ThemeArtworkPlacementSpec()).Normalize(),
            Brightness = NormalizeValue(Brightness, 100d, 20d, 180d),
            Contrast = NormalizeValue(Contrast, 100d, 20d, 180d),
            Saturation = NormalizeValue(Saturation, 100d, 0d, 200d),
            Opacity = NormalizeValue(Opacity, 100d, 0d, 100d),
            Zoom = zoom,
            OffsetX = offsetX,
            OffsetY = offsetY,
            Grayscale = NormalizeValue(Grayscale, 0d, 0d, 100d),
            HueRotation = NormalizeValue(HueRotation, 0d, -180d, 180d),
            Blur = NormalizeValue(Blur, 0d, 0d, 20d),
            CustomImagePath = NormalizePath(CustomImagePath),
            ThemeAssetKey = NormalizeAssetKey(ThemeAssetKey),
            OverlayColor = NormalizeColor(OverlayColor),
            OverlayOpacity = NormalizeValue(OverlayOpacity, 0d, 0d, 100d),
            GradientStrength = NormalizeValue(GradientStrength, 0d, 0d, 100d),
            GradientVeil = (GradientVeil ?? new ThemeArtworkGradientVeil()).Normalize(),
            Vignette = NormalizeValue(Vignette, 0d, 0d, 100d),
            ReadabilityVeil = (ReadabilityVeil ?? new ThemeArtworkReadabilityVeil()).Normalize(),
            ResponsiveVariants = (ResponsiveVariants ?? [])
                .Where(variant => variant is not null)
                .Take(16)
                .Select(variant => variant.Normalize())
                .ToArray(),
            Motion = Motion?.Normalize(),
            BlendMode = SupportedBlendModes.Contains(BlendMode ?? string.Empty)
                ? (BlendMode ?? "normal").ToLowerInvariant()
                : "normal",
        };
    }

    public static bool IsSupportedBlendMode(string? blendMode) =>
        SupportedBlendModes.Contains(blendMode ?? string.Empty);

    private static double NormalizeValue(double value, double fallback, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static string? NormalizePath(string? path)
    {
        var value = (path ?? string.Empty).Trim();
        return value.Length switch
        {
            0 => null,
            > 512 => value[..512],
            _ => value,
        };
    }

    private static string? NormalizeAssetKey(string? assetKey)
    {
        var value = (assetKey ?? string.Empty).Trim();
        if (value.Length is 0 or > 128 ||
            value.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return null;
        }
        return value;
    }

    private static string NormalizeColor(string? color)
    {
        var value = (color ?? string.Empty).Trim();
        if (value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit))
        {
            return value.ToUpperInvariant();
        }
        return "#000000";
    }
}

public sealed record ThemeDisplayPreferences
{
    private static readonly HashSet<string> MotionOptions =
        new(["full", "reduced", "off"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TextScaleOptions =
        new(["small", "standard", "large"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DensityOptions =
        new(["compact", "comfortable", "spacious"], StringComparer.OrdinalIgnoreCase);

    public string MotionIntensity { get; init; } = "full";

    public string TextScale { get; init; } = "standard";

    public string Density { get; init; } = "comfortable";

    public ThemeDisplayPreferences Normalize() => this with
    {
        MotionIntensity = NormalizeOption(MotionIntensity, MotionOptions, "full"),
        TextScale = NormalizeOption(TextScale, TextScaleOptions, "standard"),
        Density = NormalizeOption(Density, DensityOptions, "comfortable"),
    };

    private static string NormalizeOption(
        string? value,
        HashSet<string> supported,
        string fallback)
    {
        var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
        return supported.Contains(candidate) ? candidate : fallback;
    }
}

public sealed record ThemeVisualModeSettings
{
    public ThemeArtworkAdjustment Hero { get; init; } = new();

    public ThemeArtworkAdjustment Sidebar { get; init; } = new();

    public ThemeArtworkAdjustment Chat { get; init; } = new();

    public ThemeVisualModeSettings Normalize() => this with
    {
        Hero = (Hero ?? new ThemeArtworkAdjustment()).Normalize(),
        Sidebar = (Sidebar ?? new ThemeArtworkAdjustment()).Normalize(),
        Chat = (Chat ?? new ThemeArtworkAdjustment()).Normalize(),
    };
}

public sealed record ThemeVisualSettings
{
    public ThemeVisualModeSettings Light { get; init; } = new();

    public ThemeVisualModeSettings Dark { get; init; } = new();

    public ThemeDisplayPreferences Display { get; init; } = new();

    public ThemeVisualSettings Normalize() => this with
    {
        Light = (Light ?? new ThemeVisualModeSettings()).Normalize(),
        Dark = (Dark ?? new ThemeVisualModeSettings()).Normalize(),
        Display = (Display ?? new ThemeDisplayPreferences()).Normalize(),
    };
}
