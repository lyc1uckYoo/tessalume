using System.Globalization;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

internal static class ArtworkPresentationFormatter
{
    private static readonly HashSet<string> CssKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto",
        "center",
        "contain",
        "cover",
    };

    public static string CssToken(string? token)
    {
        var value = token?.Trim() ?? string.Empty;
        if (value.Length == 0 || CssKeywords.Contains(value)) return value;

        var suffix = value.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            ? "px"
            : value.EndsWith('%')
                ? "%"
                : string.Empty;
        var numeric = suffix.Length == 0 ? value : value[..^suffix.Length];
        return double.TryParse(
                   numeric,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out var parsed) &&
               double.IsFinite(parsed)
            ? $"{Number(parsed)}{suffix}"
            : value;
    }

    public static string CssValue(string? value) => string.Join(
        ' ',
        (value ?? string.Empty)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(CssToken));

    public static string Number(double value)
    {
        if (!double.IsFinite(value)) return value.ToString(CultureInfo.InvariantCulture);
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) < .005d) rounded = 0d;
        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string Percent(double value) => $"{Number(value)}%";

    public static string Pixels(double value, bool spaced = false) =>
        spaced ? $"{Number(value)} px" : $"{Number(value)}px";

    public static string ExactCss(ThemeArtworkLength value) => value.Normalize() switch
    {
        { Unit: ThemeArtworkLengthUnit.Percent, Value: var number } =>
            $"{number.ToString("R", CultureInfo.InvariantCulture)}%",
        { Unit: ThemeArtworkLengthUnit.Pixels, Value: var number } =>
            $"{number.ToString("R", CultureInfo.InvariantCulture)}px",
        _ => "auto",
    };

    public static string ExactCss(ThemeArtworkPositionValue value, bool horizontal) =>
        value.Normalize() switch
        {
            { Kind: ThemeArtworkPositionKind.Start } => horizontal ? "left" : "top",
            { Kind: ThemeArtworkPositionKind.End } => horizontal ? "right" : "bottom",
            { Kind: ThemeArtworkPositionKind.Percent, Value: var number } =>
                $"{number.ToString("R", CultureInfo.InvariantCulture)}%",
            { Kind: ThemeArtworkPositionKind.Pixels, Value: var number } =>
                $"{number.ToString("R", CultureInfo.InvariantCulture)}px",
            _ => "center",
        };
}
