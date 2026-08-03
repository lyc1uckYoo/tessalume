namespace Tessalume.Core.Runtime;

public sealed record ThemeArtworkAdjustment
{
    public double Brightness { get; init; } = 100d;

    public double Contrast { get; init; } = 100d;

    public double Saturation { get; init; } = 100d;

    public double Opacity { get; init; } = 100d;

    public ThemeArtworkAdjustment Normalize() => this with
    {
        Brightness = Math.Clamp(Brightness, 20d, 180d),
        Contrast = Math.Clamp(Contrast, 20d, 180d),
        Saturation = Math.Clamp(Saturation, 0d, 200d),
        Opacity = Math.Clamp(Opacity, 0d, 100d),
    };
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
