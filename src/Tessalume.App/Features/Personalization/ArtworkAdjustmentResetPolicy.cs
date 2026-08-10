using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization;

internal static class ArtworkAdjustmentResetPolicy
{
    public static ThemeArtworkAdjustment ResetGroup(
        ThemeArtworkAdjustment adjustment,
        ArtworkAdjustmentGroup group)
    {
        var defaults = new ThemeArtworkAdjustment();
        return (group switch
        {
            ArtworkAdjustmentGroup.Composition => adjustment with
            {
                Zoom = defaults.Zoom,
                OffsetX = defaults.OffsetX,
                OffsetY = defaults.OffsetY,
            },
            ArtworkAdjustmentGroup.Effects => adjustment with
            {
                Grayscale = defaults.Grayscale,
                HueRotation = defaults.HueRotation,
                Blur = defaults.Blur,
                OverlayOpacity = defaults.OverlayOpacity,
                OverlayColor = defaults.OverlayColor,
                GradientStrength = defaults.GradientStrength,
                Vignette = defaults.Vignette,
                BlendMode = defaults.BlendMode,
                ReadabilityProtection = defaults.ReadabilityProtection,
            },
            _ => adjustment with
            {
                Brightness = defaults.Brightness,
                Contrast = defaults.Contrast,
                Saturation = defaults.Saturation,
                Opacity = defaults.Opacity,
            },
        }).Normalize();
    }
}
