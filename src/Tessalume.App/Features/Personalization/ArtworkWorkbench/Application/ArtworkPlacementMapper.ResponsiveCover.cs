using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;

internal static partial class ArtworkPlacementMapper
{
    public static ThemeArtworkPlacementSpec CommitResponsiveCover(
        ThemeArtworkSourcePlacement sourceCrop,
        ArtworkSize imageSize,
        ArtworkSize targetSize,
        bool mirrorX = false,
        bool mirrorY = false)
    {
        EnsureValid(imageSize, nameof(imageSize));
        EnsureValid(targetSize, nameof(targetSize));
        var crop = ConstrainAspect(sourceCrop, imageSize, targetSize).Crop;
        if (crop.Mode == ThemeArtworkPlacementMode.Contain)
        {
            return Contain(
                crop.AlignmentX,
                crop.AlignmentY,
                mirrorX,
                mirrorY);
        }

        var coverFactor = Math.Max(
            targetSize.Width / imageSize.Width,
            targetSize.Height / imageSize.Height);
        var coverWidth = imageSize.Width * coverFactor;
        var coverHeight = imageSize.Height * coverFactor;
        var coverSourceWidth = targetSize.Width / coverWidth;
        var coverSourceHeight = targetSize.Height / coverHeight;
        var zoomX = coverSourceWidth / crop.SourceWidth;
        var zoomY = coverSourceHeight / crop.SourceHeight;
        var zoom = Math.Clamp(Math.Max(1d, Math.Max(zoomX, zoomY)), 1d, 10d);
        var sourceX = mirrorX
            ? 1d - crop.SourceX - crop.SourceWidth
            : crop.SourceX;
        var sourceY = mirrorY
            ? 1d - crop.SourceY - crop.SourceHeight
            : crop.SourceY;
        var positionX = ToBackgroundPosition(sourceX, crop.SourceWidth);
        var positionY = ToBackgroundPosition(sourceY, crop.SourceHeight);
        return new ThemeArtworkPlacementSpec
        {
            SizeMode = ThemeArtworkSizeMode.Cover,
            PositionX = positionX,
            PositionY = positionY,
            Geometry = new ThemeArtworkGeometry
            {
                Scale = zoom,
                OriginX = positionX,
                OriginY = positionY,
                MirrorX = mirrorX,
                MirrorY = mirrorY,
            },
        }.Normalize();
    }

    public static ThemeArtworkPlacementSpec AdaptResponsiveCover(
        ThemeArtworkPlacementSpec spec,
        ArtworkSize imageSize,
        ArtworkSize targetSize)
    {
        var projection = Project(
            UseResponsiveCoverMode(spec),
            imageSize,
            targetSize);
        return projection.CoversSurface
            ? CommitResponsiveCover(
                projection.SourceProjection,
                imageSize,
                targetSize,
                projection.IsHorizontallyMirrored,
                projection.IsVerticallyMirrored)
            : Contain(
                projection.SourceProjection.AlignmentX,
                projection.SourceProjection.AlignmentY,
                projection.IsHorizontallyMirrored,
                projection.IsVerticallyMirrored);
    }

    public static ThemeArtworkPlacementSpec UseResponsiveCoverMode(
        ThemeArtworkPlacementSpec spec)
    {
        var normalized = (spec ?? new ThemeArtworkPlacementSpec()).Normalize();
        if (normalized.SizeMode != ThemeArtworkSizeMode.Explicit)
        {
            return normalized;
        }
        return normalized with
        {
            SizeMode = ThemeArtworkSizeMode.Cover,
            Width = ThemeArtworkLength.Auto,
            Height = ThemeArtworkLength.Auto,
            Geometry = normalized.Geometry with
            {
                OriginX = normalized.PositionX,
                OriginY = normalized.PositionY,
            },
        };
    }

    public static ThemeArtworkPlacementSpec Cover(
        double alignmentX = .5d,
        double alignmentY = .5d,
        bool mirrorX = false,
        bool mirrorY = false)
    {
        var positionX = ThemeArtworkPositionValue.Percent(
            double.IsFinite(alignmentX) ? Math.Clamp(alignmentX, 0d, 1d) * 100d : 50d);
        var positionY = ThemeArtworkPositionValue.Percent(
            double.IsFinite(alignmentY) ? Math.Clamp(alignmentY, 0d, 1d) * 100d : 50d);
        return new ThemeArtworkPlacementSpec
        {
            SizeMode = ThemeArtworkSizeMode.Cover,
            PositionX = positionX,
            PositionY = positionY,
            Geometry = new ThemeArtworkGeometry
            {
                OriginX = positionX,
                OriginY = positionY,
                MirrorX = mirrorX,
                MirrorY = mirrorY,
            },
        };
    }
}
