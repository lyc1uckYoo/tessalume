namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

internal enum ArtworkRegion
{
    Hero,
    Sidebar,
    Chat,
}

internal enum ArtworkColorMode
{
    Light,
    Dark,
}

internal enum ArtworkParameterGroup
{
    Basic,
    Composition,
    Effects,
}

internal enum ArtworkParameter
{
    Brightness,
    Contrast,
    Saturation,
    Opacity,
    Zoom,
    OffsetX,
    OffsetY,
    PlacementSize,
    PlacementX,
    PlacementY,
    Grayscale,
    HueRotation,
    Blur,
    OverlayColor,
    OverlayOpacity,
    GradientStrength,
    Vignette,
    BlendMode,
    ReadabilityProtection,
}

internal enum ArtworkResetScope
{
    Parameter,
    ParameterGroup,
    RegionMode,
}

internal readonly record struct ArtworkTarget(
    ArtworkColorMode Mode,
    ArtworkRegion Region);

internal readonly record struct ArtworkResetRequest(
    ArtworkResetScope Scope,
    ArtworkColorMode Mode = ArtworkColorMode.Light,
    ArtworkRegion Region = ArtworkRegion.Hero,
    ArtworkParameter? Parameter = null,
    ArtworkParameterGroup? Group = null)
{
    public static ArtworkResetRequest ForParameter(
        ArtworkColorMode mode,
        ArtworkRegion region,
        ArtworkParameter parameter) =>
        new(ArtworkResetScope.Parameter, mode, region, parameter);

    public static ArtworkResetRequest ForGroup(
        ArtworkColorMode mode,
        ArtworkRegion region,
        ArtworkParameterGroup group) =>
        new(ArtworkResetScope.ParameterGroup, mode, region, Group: group);

    public static ArtworkResetRequest ForRegionMode(
        ArtworkColorMode mode,
        ArtworkRegion region) =>
        new(ArtworkResetScope.RegionMode, mode, region);
}

internal static class ArtworkParameterExtensions
{
    public static ArtworkParameterGroup GetGroup(this ArtworkParameter parameter) => parameter switch
    {
        ArtworkParameter.Brightness or
        ArtworkParameter.Contrast or
        ArtworkParameter.Saturation or
        ArtworkParameter.Opacity => ArtworkParameterGroup.Basic,

        ArtworkParameter.Zoom or
        ArtworkParameter.OffsetX or
        ArtworkParameter.OffsetY or
        ArtworkParameter.PlacementSize or
        ArtworkParameter.PlacementX or
        ArtworkParameter.PlacementY => ArtworkParameterGroup.Composition,

        _ => ArtworkParameterGroup.Effects,
    };
}
