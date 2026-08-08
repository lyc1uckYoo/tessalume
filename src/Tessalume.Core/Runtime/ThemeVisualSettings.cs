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
            "soft-light",
            "luminosity",
        };

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomImagePath { get; init; }

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

    public ThemeArtworkAdjustment Normalize() => this with
    {
        Brightness = NormalizeValue(Brightness, 100d, 20d, 180d),
        Contrast = NormalizeValue(Contrast, 100d, 20d, 180d),
        Saturation = NormalizeValue(Saturation, 100d, 0d, 200d),
        Opacity = NormalizeValue(Opacity, 100d, 0d, 100d),
        Zoom = NormalizeValue(Zoom, 100d, 70d, 200d),
        OffsetX = NormalizeValue(OffsetX, 0d, -200d, 200d),
        OffsetY = NormalizeValue(OffsetY, 0d, -200d, 200d),
        Grayscale = NormalizeValue(Grayscale, 0d, 0d, 100d),
        HueRotation = NormalizeValue(HueRotation, 0d, -180d, 180d),
        Blur = NormalizeValue(Blur, 0d, 0d, 20d),
        CustomImagePath = NormalizePath(CustomImagePath),
        OverlayColor = NormalizeColor(OverlayColor),
        OverlayOpacity = NormalizeValue(OverlayOpacity, 0d, 0d, 100d),
        GradientStrength = NormalizeValue(GradientStrength, 0d, 0d, 100d),
        Vignette = NormalizeValue(Vignette, 0d, 0d, 100d),
        BlendMode = SupportedBlendModes.Contains(BlendMode ?? string.Empty)
            ? (BlendMode ?? "normal").ToLowerInvariant()
            : "normal",
    };

    public ThemeArtworkAdjustment WithoutCustomImage() => this with { CustomImagePath = null };

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

public sealed record ThemeArtworkPreset
{
    public string Name { get; init; } = string.Empty;

    public ThemeVisualModeSettings Settings { get; init; } = new();

    public ThemeArtworkPreset Normalize()
    {
        var name = (Name ?? string.Empty).Trim();
        if (name.Length > 32)
        {
            name = name[..32];
        }

        return this with
        {
            Name = name,
            Settings = StripCustomImages((Settings ?? new ThemeVisualModeSettings()).Normalize()),
        };
    }

    public override string ToString() => Name;

    private static ThemeVisualModeSettings StripCustomImages(ThemeVisualModeSettings settings) =>
        settings with
        {
            Hero = settings.Hero.WithoutCustomImage(),
            Sidebar = settings.Sidebar.WithoutCustomImage(),
            Chat = settings.Chat.WithoutCustomImage(),
        };
}

public sealed record ThemeExperiencePreset
{
    public string Name { get; init; } = string.Empty;

    public string ThemeId { get; init; } = string.Empty;

    public bool DarkMode { get; init; }

    public ThemeVisualSettings Settings { get; init; } = new();

    public ThemeExperiencePreset Normalize()
    {
        var name = (Name ?? string.Empty).Trim();
        if (name.Length > 32) name = name[..32];
        var themeId = (ThemeId ?? string.Empty).Trim();
        if (themeId.Length > 256) themeId = themeId[..256];
        return this with
        {
            Name = name,
            ThemeId = themeId,
            Settings = (Settings ?? new ThemeVisualSettings()).Normalize(),
        };
    }

    public override string ToString() => Name;
}
