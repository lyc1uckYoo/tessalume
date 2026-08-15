using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

internal readonly record struct ArtworkSize(double Width, double Height)
{
    public bool IsValid =>
        double.IsFinite(Width) && Width > 0 &&
        double.IsFinite(Height) && Height > 0;
}

internal readonly record struct ArtworkCanvasTransform(
    double Zoom,
    double OffsetX,
    double OffsetY);

internal readonly record struct ArtworkRect(
    double X,
    double Y,
    double Width,
    double Height)
{
    public bool IsValid =>
        double.IsFinite(X) && double.IsFinite(Y) &&
        double.IsFinite(Width) && Width > 0d &&
        double.IsFinite(Height) && Height > 0d;
}

internal sealed record ArtworkPlacementProjection(
    ArtworkRect RenderedImage,
    ArtworkRect SourceViewport,
    ThemeArtworkSourcePlacement SourceProjection,
    bool CoversSurface,
    bool IsDistorted,
    bool IsHorizontallyMirrored,
    bool IsVerticallyMirrored,
    string SizeCss,
    string PositionCss);

internal sealed record ArtworkCropMutationResult(
    ThemeArtworkSourcePlacement Crop,
    bool HitLeft,
    bool HitTop,
    bool HitRight,
    bool HitBottom);
