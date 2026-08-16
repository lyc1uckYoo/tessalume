using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;

internal static partial class ArtworkPlacementMapper
{
    private const double Epsilon = .000001d;

    public static ArtworkPlacementProjection ResolveEffectivePlacement(
        ThemeArtworkAdjustment adjustment,
        ThemeArtworkPlacementSpec themeDefault,
        ArtworkSize imageSize,
        ArtworkSize targetSize)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        ArgumentNullException.ThrowIfNull(themeDefault);
        var normalized = adjustment.Normalize();
        var spec = normalized.CompositionMode == ThemeArtworkCompositionMode.Custom
            ? normalized.Placement ?? themeDefault
            : themeDefault;
        var legacy = normalized.CompositionMode == ThemeArtworkCompositionMode.Legacy
            ? new ArtworkCanvasTransform(
                normalized.Zoom,
                normalized.OffsetX,
                normalized.OffsetY)
            : (ArtworkCanvasTransform?)null;
        return Project(spec, imageSize, targetSize, legacy);
    }

    public static ThemeArtworkAdjustment ConvertToCustomEquivalent(
        ThemeArtworkAdjustment adjustment,
        ThemeArtworkPlacementSpec themeDefault,
        ArtworkSize imageSize,
        ArtworkSize targetSize,
        bool fixedWidthSurface = false)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        var normalized = adjustment.Normalize();
        if (normalized.CompositionMode == ThemeArtworkCompositionMode.Custom) return normalized;
        var projection = ResolveEffectivePlacement(
            normalized,
            themeDefault,
            imageSize,
            targetSize);
        var placement = projection.CoversSurface
            ? CommitCrop(
                projection.SourceProjection with { Mode = ThemeArtworkPlacementMode.Crop },
                imageSize,
                targetSize,
                projection.IsHorizontallyMirrored,
                projection.IsVerticallyMirrored,
                fixedWidthSurface)
            : Contain(
                projection.SourceProjection.AlignmentX,
                projection.SourceProjection.AlignmentY,
                projection.IsHorizontallyMirrored,
                projection.IsVerticallyMirrored);
        return (normalized with
        {
            CompositionMode = ThemeArtworkCompositionMode.Custom,
            Placement = placement,
            Zoom = 100d,
            OffsetX = 0d,
            OffsetY = 0d,
        }).Normalize();
    }

    public static ArtworkPlacementProjection Project(
        ThemeArtworkPlacementSpec spec,
        ArtworkSize imageSize,
        ArtworkSize targetSize,
        ArtworkCanvasTransform? legacyTransform = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        EnsureValid(imageSize, nameof(imageSize));
        EnsureValid(targetSize, nameof(targetSize));
        var normalized = spec.Normalize();
        var (width, height, distorted) = ResolveSize(normalized, imageSize, targetSize);
        var left = ResolvePosition(normalized.PositionX, targetSize.Width, width);
        var top = ResolvePosition(normalized.PositionY, targetSize.Height, height);
        var geometry = normalized.Geometry;
        var originX = ResolvePositionValue(geometry.OriginX, targetSize.Width);
        var originY = ResolvePositionValue(geometry.OriginY, targetSize.Height);
        var scale = geometry.Scale;
        left = originX + ((left - originX) * scale);
        top = originY + ((top - originY) * scale);
        width *= scale;
        height *= scale;

        if (legacyTransform is { } legacy)
        {
            var legacyScale = double.IsFinite(legacy.Zoom)
                ? Math.Clamp(legacy.Zoom, 70d, 200d) / 100d
                : 1d;
            var centerX = targetSize.Width / 2d;
            var centerY = targetSize.Height / 2d;
            left = centerX + ((left - centerX) * legacyScale) +
                   Math.Clamp(legacy.OffsetX, -200d, 200d);
            top = centerY + ((top - centerY) * legacyScale) +
                  Math.Clamp(legacy.OffsetY, -200d, 200d);
            width *= legacyScale;
            height *= legacyScale;
        }

        var viewportLeft = -left / width;
        var viewportTop = -top / height;
        var viewportWidth = targetSize.Width / width;
        var viewportHeight = targetSize.Height / height;
        if (geometry.MirrorX) viewportLeft = 1d - viewportLeft - viewportWidth;
        if (geometry.MirrorY) viewportTop = 1d - viewportTop - viewportHeight;
        var visibleLeft = Math.Clamp(viewportLeft, 0d, 1d);
        var visibleTop = Math.Clamp(viewportTop, 0d, 1d);
        var visibleRight = Math.Clamp(viewportLeft + viewportWidth, 0d, 1d);
        var visibleBottom = Math.Clamp(viewportTop + viewportHeight, 0d, 1d);
        if (geometry.MirrorX)
        {
            // SourceViewport was already mirrored above. Keep the clamped interval
            // ordered for crop-frame rendering.
            (visibleLeft, visibleRight) = (
                Math.Min(visibleLeft, visibleRight),
                Math.Max(visibleLeft, visibleRight));
        }
        if (geometry.MirrorY)
        {
            (visibleTop, visibleBottom) = (
                Math.Min(visibleTop, visibleBottom),
                Math.Max(visibleTop, visibleBottom));
        }
        var sourceWidth = Math.Max(.001d, visibleRight - visibleLeft);
        var sourceHeight = Math.Max(.001d, visibleBottom - visibleTop);
        var crop = new ThemeArtworkSourcePlacement
        {
            Mode = width + Epsilon >= targetSize.Width && height + Epsilon >= targetSize.Height
                ? ThemeArtworkPlacementMode.Crop
                : ThemeArtworkPlacementMode.Contain,
            SourceX = visibleLeft,
            SourceY = visibleTop,
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            AlignmentX = ResolveAlignment(left, width, targetSize.Width),
            AlignmentY = ResolveAlignment(top, height, targetSize.Height),
        }.Normalize();
        return new ArtworkPlacementProjection(
            new ArtworkRect(left, top, width, height),
            new ArtworkRect(viewportLeft, viewportTop, viewportWidth, viewportHeight),
            crop,
            width + Epsilon >= targetSize.Width && height + Epsilon >= targetSize.Height,
            distorted,
            geometry.MirrorX,
            geometry.MirrorY,
            normalized.SizeCss,
            normalized.PositionCss);
    }

    public static ThemeArtworkPlacementSpec CommitCrop(
        ThemeArtworkSourcePlacement sourceCrop,
        ArtworkSize imageSize,
        ArtworkSize targetSize,
        bool mirrorX = false,
        bool mirrorY = false,
        bool fixedWidthSurface = false)
    {
        EnsureValid(imageSize, nameof(imageSize));
        EnsureValid(targetSize, nameof(targetSize));
        var crop = ConstrainAspect(sourceCrop, imageSize, targetSize).Crop;
        if (crop.Mode == ThemeArtworkPlacementMode.Contain)
        {
            return Contain(crop.AlignmentX, crop.AlignmentY, mirrorX, mirrorY);
        }

        var renderedWidth = targetSize.Width / crop.SourceWidth;
        var renderedHeight = renderedWidth * imageSize.Height / imageSize.Width;
        return new ThemeArtworkPlacementSpec
        {
            SizeMode = ThemeArtworkSizeMode.Explicit,
            Width = ThemeArtworkLength.Percent(100d / crop.SourceWidth),
            Height = fixedWidthSurface
                ? ThemeArtworkLength.Auto
                : ThemeArtworkLength.Percent(100d / crop.SourceHeight),
            PositionX = ToBackgroundPosition(crop.SourceX, crop.SourceWidth),
            PositionY = fixedWidthSurface
                ? ThemeArtworkPositionValue.Pixels(-renderedHeight * crop.SourceY)
                : ToBackgroundPosition(crop.SourceY, crop.SourceHeight),
            Geometry = new ThemeArtworkGeometry
            {
                MirrorX = mirrorX,
                MirrorY = mirrorY,
            },
        }.Normalize();
    }

    public static ArtworkCropMutationResult MoveCrop(
        ThemeArtworkSourcePlacement sourceCrop,
        double deltaX,
        double deltaY,
        ArtworkSize imageSize,
        ArtworkSize targetSize)
    {
        var crop = ConstrainAspect(sourceCrop, imageSize, targetSize).Crop;
        if (crop.Mode == ThemeArtworkPlacementMode.Contain)
        {
            var spec = Contain(crop.AlignmentX, crop.AlignmentY);
            var projection = Project(spec, imageSize, targetSize);
            var containRequestedX = projection.SourceViewport.X +
                             (double.IsFinite(deltaX) ? deltaX : 0d);
            var containRequestedY = projection.SourceViewport.Y +
                             (double.IsFinite(deltaY) ? deltaY : 0d);
            var horizontalGap = Math.Max(0d, projection.SourceViewport.Width - 1d);
            var verticalGap = Math.Max(0d, projection.SourceViewport.Height - 1d);
            var alignmentX = horizontalGap <= Epsilon
                ? .5d
                : Math.Clamp(-containRequestedX / horizontalGap, 0d, 1d);
            var alignmentY = verticalGap <= Epsilon
                ? .5d
                : Math.Clamp(-containRequestedY / verticalGap, 0d, 1d);
            var result = crop with { AlignmentX = alignmentX, AlignmentY = alignmentY };
            return new ArtworkCropMutationResult(
                result,
                alignmentX >= 1d - Epsilon,
                alignmentY >= 1d - Epsilon,
                alignmentX <= Epsilon,
                alignmentY <= Epsilon);
        }
        var requestedX = crop.SourceX + (double.IsFinite(deltaX) ? deltaX : 0d);
        var requestedY = crop.SourceY + (double.IsFinite(deltaY) ? deltaY : 0d);
        var x = Math.Clamp(requestedX, 0d, 1d - crop.SourceWidth);
        var y = Math.Clamp(requestedY, 0d, 1d - crop.SourceHeight);
        return new ArtworkCropMutationResult(
            crop with { SourceX = x, SourceY = y },
            x <= Epsilon,
            y <= Epsilon,
            x + crop.SourceWidth >= 1d - Epsilon,
            y + crop.SourceHeight >= 1d - Epsilon);
    }

    public static ArtworkCropMutationResult ResizeCrop(
        ThemeArtworkSourcePlacement sourceCrop,
        double scaleFactor,
        double anchorX,
        double anchorY,
        ArtworkSize imageSize,
        ArtworkSize targetSize)
    {
        var crop = ConstrainAspect(sourceCrop, imageSize, targetSize).Crop;
        var factor = double.IsFinite(scaleFactor) ? Math.Clamp(scaleFactor, .05d, 20d) : 1d;
        var ax = double.IsFinite(anchorX) ? Math.Clamp(anchorX, 0d, 1d) : .5d;
        var ay = double.IsFinite(anchorY) ? Math.Clamp(anchorY, 0d, 1d) : .5d;
        var desiredWidth = Math.Clamp(crop.SourceWidth * factor, .01d, 1d);
        var desiredHeight = desiredWidth * imageSize.Width * targetSize.Height /
                            (imageSize.Height * targetSize.Width);
        if (desiredHeight > 1d)
        {
            desiredHeight = 1d;
            desiredWidth = desiredHeight * imageSize.Height * targetSize.Width /
                           (imageSize.Width * targetSize.Height);
        }
        var focalX = crop.SourceX + (crop.SourceWidth * ax);
        var focalY = crop.SourceY + (crop.SourceHeight * ay);
        var x = Math.Clamp(focalX - (desiredWidth * ax), 0d, 1d - desiredWidth);
        var y = Math.Clamp(focalY - (desiredHeight * ay), 0d, 1d - desiredHeight);
        var result = new ThemeArtworkSourcePlacement
        {
            Mode = ThemeArtworkPlacementMode.Crop,
            SourceX = x,
            SourceY = y,
            SourceWidth = desiredWidth,
            SourceHeight = desiredHeight,
            AlignmentX = crop.AlignmentX,
            AlignmentY = crop.AlignmentY,
        }.Normalize();
        return new ArtworkCropMutationResult(
            result,
            x <= Epsilon,
            y <= Epsilon,
            x + desiredWidth >= 1d - Epsilon,
            y + desiredHeight >= 1d - Epsilon);
    }

    public static ArtworkCropMutationResult ZoomAt(
        ThemeArtworkSourcePlacement sourceCrop,
        double zoomFactor,
        double focalSourceX,
        double focalSourceY,
        ArtworkSize imageSize,
        ArtworkSize targetSize)
    {
        var crop = ConstrainAspect(sourceCrop, imageSize, targetSize).Crop;
        var anchorX = crop.SourceWidth <= Epsilon
            ? .5d
            : (focalSourceX - crop.SourceX) / crop.SourceWidth;
        var anchorY = crop.SourceHeight <= Epsilon
            ? .5d
            : (focalSourceY - crop.SourceY) / crop.SourceHeight;
        return ResizeCrop(
            crop,
            double.IsFinite(zoomFactor) && zoomFactor > 0d ? 1d / zoomFactor : 1d,
            anchorX,
            anchorY,
            imageSize,
            targetSize);
    }

    public static ThemeArtworkPlacementSpec Contain(
        double alignmentX = .5d,
        double alignmentY = .5d,
        bool mirrorX = false,
        bool mirrorY = false) => new()
        {
            SizeMode = ThemeArtworkSizeMode.Contain,
            PositionX = ThemeArtworkPositionValue.Percent(
            double.IsFinite(alignmentX) ? Math.Clamp(alignmentX, 0d, 1d) * 100d : 50d),
            PositionY = ThemeArtworkPositionValue.Percent(
            double.IsFinite(alignmentY) ? Math.Clamp(alignmentY, 0d, 1d) * 100d : 50d),
            Geometry = new ThemeArtworkGeometry { MirrorX = mirrorX, MirrorY = mirrorY },
        };

    public static ThemeArtworkPlacementSpec Fill(
        ArtworkSize imageSize,
        ArtworkSize targetSize,
        double focalX = .5d,
        double focalY = .5d,
        bool mirrorX = false,
        bool mirrorY = false,
        bool fixedWidthSurface = false)
    {
        EnsureValid(imageSize, nameof(imageSize));
        EnsureValid(targetSize, nameof(targetSize));
        var imageAspect = imageSize.Width / imageSize.Height;
        var targetAspect = targetSize.Width / targetSize.Height;
        var width = targetAspect >= imageAspect ? 1d : targetAspect / imageAspect;
        var height = targetAspect >= imageAspect ? imageAspect / targetAspect : 1d;
        var crop = new ThemeArtworkSourcePlacement
        {
            SourceWidth = width,
            SourceHeight = height,
            SourceX = Math.Clamp(focalX, 0d, 1d) - (width / 2d),
            SourceY = Math.Clamp(focalY, 0d, 1d) - (height / 2d),
        }.Normalize();
        return CommitCrop(
            crop,
            imageSize,
            targetSize,
            mirrorX,
            mirrorY,
            fixedWidthSurface);
    }

    public static ThemeArtworkPlacementSpec Center(ThemeArtworkPlacementSpec spec)
    {
        var normalized = (spec ?? new ThemeArtworkPlacementSpec()).Normalize();
        return normalized with
        {
            PositionX = ThemeArtworkPositionValue.Center,
            PositionY = ThemeArtworkPositionValue.Center,
            Geometry = normalized.SizeMode == ThemeArtworkSizeMode.Cover
                ? normalized.Geometry with
                {
                    OriginX = ThemeArtworkPositionValue.Center,
                    OriginY = ThemeArtworkPositionValue.Center,
                }
                : normalized.Geometry,
        };
    }

    public static ThemeArtworkPlacementSpec AdaptFixedWidthSidebar(
        ThemeArtworkPlacementSpec spec)
    {
        var normalized = (spec ?? new ThemeArtworkPlacementSpec()).Normalize();
        return normalized.SizeMode == ThemeArtworkSizeMode.Explicit
            ? normalized with { Height = ThemeArtworkLength.Auto }
            : normalized;
    }

    public static ThemeArtworkPlacementSpec AdaptFixedWidthSidebar(
        ThemeArtworkPlacementSpec spec,
        ArtworkSize imageSize,
        ArtworkSize targetSize)
    {
        EnsureValid(imageSize, nameof(imageSize));
        EnsureValid(targetSize, nameof(targetSize));
        var normalized = (spec ?? new ThemeArtworkPlacementSpec()).Normalize();
        if (normalized.SizeMode != ThemeArtworkSizeMode.Explicit)
        {
            return normalized;
        }

        var positionY = normalized.PositionY;
        if (positionY.Kind != ThemeArtworkPositionKind.Pixels &&
            normalized.Height is
            {
                Unit: ThemeArtworkLengthUnit.Percent,
                Value: > Epsilon,
            })
        {
            var renderedWidth = ResolveLength(normalized.Width, targetSize.Width) ??
                                imageSize.Width;
            var renderedHeight = renderedWidth * imageSize.Height / imageSize.Width;
            var referenceHeight = renderedHeight * 100d / normalized.Height.Value;
            positionY = ThemeArtworkPositionValue.Pixels(
                (referenceHeight - renderedHeight) * ResolvePositionAlignment(positionY));
        }

        return normalized with
        {
            Height = ThemeArtworkLength.Auto,
            PositionY = positionY,
        };
    }

    public static ArtworkCropMutationResult ConstrainAspect(
        ThemeArtworkSourcePlacement sourceCrop,
        ArtworkSize imageSize,
        ArtworkSize targetSize)
    {
        ArgumentNullException.ThrowIfNull(sourceCrop);
        EnsureValid(imageSize, nameof(imageSize));
        EnsureValid(targetSize, nameof(targetSize));
        var value = sourceCrop.Normalize();
        if (value.Mode == ThemeArtworkPlacementMode.Contain)
        {
            return new ArtworkCropMutationResult(value, true, true, true, true);
        }
        var targetHeight = value.SourceWidth * imageSize.Width * targetSize.Height /
                           (imageSize.Height * targetSize.Width);
        var width = value.SourceWidth;
        var height = targetHeight;
        if (height > 1d)
        {
            height = 1d;
            width = height * imageSize.Height * targetSize.Width /
                    (imageSize.Width * targetSize.Height);
        }
        var centerX = value.SourceX + (value.SourceWidth / 2d);
        var centerY = value.SourceY + (value.SourceHeight / 2d);
        var x = Math.Clamp(centerX - (width / 2d), 0d, 1d - width);
        var y = Math.Clamp(centerY - (height / 2d), 0d, 1d - height);
        var result = value with
        {
            SourceX = x,
            SourceY = y,
            SourceWidth = width,
            SourceHeight = height,
        };
        return new ArtworkCropMutationResult(
            result,
            x <= Epsilon,
            y <= Epsilon,
            x + width >= 1d - Epsilon,
            y + height >= 1d - Epsilon);
    }

    private static (double Width, double Height, bool Distorted) ResolveSize(
        ThemeArtworkPlacementSpec spec,
        ArtworkSize image,
        ArtworkSize target)
    {
        if (spec.SizeMode is ThemeArtworkSizeMode.Cover or ThemeArtworkSizeMode.Contain)
        {
            var fill = spec.SizeMode == ThemeArtworkSizeMode.Cover;
            var scale = fill
                ? Math.Max(target.Width / image.Width, target.Height / image.Height)
                : Math.Min(target.Width / image.Width, target.Height / image.Height);
            return (image.Width * scale, image.Height * scale, false);
        }

        var width = ResolveLength(spec.Width, target.Width);
        var height = ResolveLength(spec.Height, target.Height);
        if (width is null && height is null)
        {
            width = image.Width;
            height = image.Height;
        }
        else if (width is null)
        {
            width = height!.Value * image.Width / image.Height;
        }
        else if (height is null)
        {
            height = width.Value * image.Height / image.Width;
        }
        var actualWidth = Math.Max(.001d, width.Value);
        var actualHeight = Math.Max(.001d, height.Value);
        var distorted = Math.Abs(
            (actualWidth / actualHeight) - (image.Width / image.Height)) > .001d;
        return (actualWidth, actualHeight, distorted);
    }

    private static double? ResolveLength(ThemeArtworkLength length, double targetLength) =>
        length.Normalize() switch
        {
            { Unit: ThemeArtworkLengthUnit.Percent, Value: var value } =>
                targetLength * value / 100d,
            { Unit: ThemeArtworkLengthUnit.Pixels, Value: var value } => value,
            _ => null,
        };

    private static double ResolvePosition(
        ThemeArtworkPositionValue position,
        double targetLength,
        double renderedLength) => position.Normalize() switch
        {
            { Kind: ThemeArtworkPositionKind.Start } => 0d,
            { Kind: ThemeArtworkPositionKind.End } => targetLength - renderedLength,
            { Kind: ThemeArtworkPositionKind.Percent, Value: var value } =>
                (targetLength - renderedLength) * value / 100d,
            { Kind: ThemeArtworkPositionKind.Pixels, Value: var value } => value,
            _ => (targetLength - renderedLength) / 2d,
        };

    private static double ResolvePositionAlignment(
        ThemeArtworkPositionValue position) => position.Normalize() switch
        {
            { Kind: ThemeArtworkPositionKind.Start } => 0d,
            { Kind: ThemeArtworkPositionKind.End } => 1d,
            { Kind: ThemeArtworkPositionKind.Percent, Value: var value } => value / 100d,
            _ => .5d,
        };

    private static double ResolvePositionValue(
        ThemeArtworkPositionValue position,
        double targetLength) => position.Normalize() switch
        {
            { Kind: ThemeArtworkPositionKind.Start } => 0d,
            { Kind: ThemeArtworkPositionKind.End } => targetLength,
            { Kind: ThemeArtworkPositionKind.Percent, Value: var value } =>
                targetLength * value / 100d,
            { Kind: ThemeArtworkPositionKind.Pixels, Value: var value } => value,
            _ => targetLength / 2d,
        };

    private static ThemeArtworkPositionValue ToBackgroundPosition(double start, double length) =>
        1d - length <= Epsilon
            ? ThemeArtworkPositionValue.Center
            : ThemeArtworkPositionValue.Percent(start / (1d - length) * 100d);

    private static double ResolveAlignment(double offset, double rendered, double target)
    {
        var travel = target - rendered;
        return Math.Abs(travel) <= Epsilon ? .5d : Math.Clamp(offset / travel, 0d, 1d);
    }

    private static void EnsureValid(ArtworkSize size, string parameterName)
    {
        if (!size.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Artwork dimensions must be finite and positive.");
        }
    }
}
