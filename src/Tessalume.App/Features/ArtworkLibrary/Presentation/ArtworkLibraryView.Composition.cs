using System.Windows;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.ArtworkLibrary.Presentation;

public partial class ArtworkLibraryView
{
    private void Canvas_DragRequested(object? sender, ArtworkCanvasDragEventArgs e)
    {
        if (!_previewReady || _busy || _gestureStart is not { } start || e.ViewportSize.Width <= 0 || e.ViewportSize.Height <= 0) return;
        var x = -e.TotalDelta.X / e.ViewportSize.Width * start.SourceViewport.Width;
        var y = -e.TotalDelta.Y / e.ViewportSize.Height * start.SourceViewport.Height;
        if (start.IsHorizontallyMirrored) x = -x;
        if (start.IsVerticallyMirrored) y = -y;
        ApplyCrop(ArtworkPlacementMapper.MoveCrop(start.SourceProjection, x, y,
            PreviewCanvas.SourcePixelSize, PreviewCanvas.TargetSize), start);
    }

    private void Canvas_ZoomRequested(object? sender, ArtworkCanvasZoomEventArgs e)
    {
        if (!_previewReady || _busy || PreviewCanvas.PlacementProjection is not { } projection) return;
        var x = projection.SourceProjection.SourceX + projection.SourceProjection.SourceWidth / 2;
        var y = projection.SourceProjection.SourceY + projection.SourceProjection.SourceHeight / 2;
        if (!e.SourceImageBounds.IsEmpty)
        {
            x = (e.Anchor.X - e.SourceImageBounds.Left) / e.SourceImageBounds.Width;
            y = (e.Anchor.Y - e.SourceImageBounds.Top) / e.SourceImageBounds.Height;
        }
        else if (PreviewCanvas.ViewportSize.Width > 0 && PreviewCanvas.ViewportSize.Height > 0)
        {
            x = projection.SourceViewport.X + e.Anchor.X / PreviewCanvas.ViewportSize.Width * projection.SourceViewport.Width;
            y = projection.SourceViewport.Y + e.Anchor.Y / PreviewCanvas.ViewportSize.Height * projection.SourceViewport.Height;
        }
        ApplyCrop(ArtworkPlacementMapper.ZoomAt(projection.SourceProjection, Math.Pow(1.08, e.Detents), x, y,
            PreviewCanvas.SourcePixelSize, PreviewCanvas.TargetSize), projection);
    }

    private void Canvas_CropFrameChanged(object? sender, ArtworkCropFrameChangedEventArgs e)
    {
        if (!_previewReady || _busy || _gestureStart is not { } start || e.SourceImageBounds.Width <= 0 || e.SourceImageBounds.Height <= 0) return;
        ArtworkCropMutationResult mutation;
        if (e.Handle == "move")
            mutation = ArtworkPlacementMapper.MoveCrop(start.SourceProjection,
                e.TotalDelta.X / e.SourceImageBounds.Width, e.TotalDelta.Y / e.SourceImageBounds.Height,
                PreviewCanvas.SourcePixelSize, PreviewCanvas.TargetSize);
        else
        {
            var fromLeft = e.Handle is "topLeft" or "bottomLeft";
            var fromTop = e.Handle is "topLeft" or "topRight";
            var width = 1 + (fromLeft ? -e.TotalDelta.X : e.TotalDelta.X) / Math.Max(1, e.CropFrameBounds.Width);
            var height = 1 + (fromTop ? -e.TotalDelta.Y : e.TotalDelta.Y) / Math.Max(1, e.CropFrameBounds.Height);
            mutation = ArtworkPlacementMapper.ResizeCrop(start.SourceProjection, (width + height) / 2,
                fromLeft ? 1 : 0, fromTop ? 1 : 0, PreviewCanvas.SourcePixelSize, PreviewCanvas.TargetSize);
        }
        ApplyCrop(mutation, start);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ChangeZoom(false);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ChangeZoom(true);
    private void ChangeZoom(bool zoomIn)
    {
        if (!_previewReady || _busy || PreviewCanvas.PlacementProjection is not { } projection) return;
        ApplyCrop(ArtworkPlacementMapper.ResizeCrop(projection.SourceProjection, zoomIn ? .92 : 1.08,
            .5, .5, PreviewCanvas.SourcePixelSize, PreviewCanvas.TargetSize), projection);
    }

    private void ApplyCrop(ArtworkCropMutationResult mutation, ArtworkPlacementProjection projection)
    {
        var placement = SelectedRegion == ArtworkRegion.Sidebar
            ? ArtworkPlacementMapper.CommitCrop(mutation.Crop, PreviewCanvas.SourcePixelSize, PreviewCanvas.TargetSize,
                projection.IsHorizontallyMirrored, projection.IsVerticallyMirrored, fixedWidthSurface: true)
            : ArtworkPlacementMapper.CommitResponsiveCover(mutation.Crop, PreviewCanvas.SourcePixelSize, PreviewCanvas.TargetSize,
                projection.IsHorizontallyMirrored, projection.IsVerticallyMirrored);
        _draft = (_draft with
        {
            CompositionMode = ThemeArtworkCompositionMode.Custom,
            Placement = placement,
            Zoom = 100,
            OffsetX = 0,
            OffsetY = 0,
        }).Normalize();
        PreviewCanvas.SetComposition(_draft, _initialDraft.Placement ?? new ThemeArtworkPlacementSpec());
        _draftEdited = true;
        RenderSelectedMetadata();
        if (mutation.HitLeft || mutation.HitTop || mutation.HitRight || mutation.HitBottom) PreviewCanvas.ShowBoundaryFeedback();
    }
}
