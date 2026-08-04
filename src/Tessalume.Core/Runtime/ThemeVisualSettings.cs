namespace Tessalume.Core.Runtime;

public sealed record ThemeArtworkAdjustment
{
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
    };

    private static double NormalizeValue(double value, double fallback, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
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

    public ThemeVisualSettings Normalize() => this with
    {
        Light = (Light ?? new ThemeVisualModeSettings()).Normalize(),
        Dark = (Dark ?? new ThemeVisualModeSettings()).Normalize(),
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
            Settings = (Settings ?? new ThemeVisualModeSettings()).Normalize(),
        };
    }
}
