using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkCanvasControl
{
    private void UpdateFullSourceLayout()
    {
        var displayedSource = _showOriginal ? _themeOriginalSource : _originalSource;
        if (displayedSource is null ||
            FullSourceCanvas.ActualWidth <= 0 ||
            FullSourceCanvas.ActualHeight <= 0)
        {
            return;
        }

        if (!_showOriginal && _placementProjection is null) return;
        var availableWidth = Math.Max(1d, FullSourceCanvas.ActualWidth);
        var availableHeight = Math.Max(1d, FullSourceCanvas.ActualHeight);
        var sourceViewport = _showOriginal
            ? new ArtworkRect(0d, 0d, 1d, 1d)
            : _placementProjection!.SourceViewport;
        var displayedPixelSize = _showOriginal
            ? _themeOriginalPixelSize
            : _sourcePixelSize;
        var sourcePixelWidth = Math.Max(1d, displayedPixelSize.Width);
        var sourcePixelHeight = Math.Max(1d, displayedPixelSize.Height);
        var worldLeft = Math.Min(0d, sourceViewport.X) * sourcePixelWidth;
        var worldTop = Math.Min(0d, sourceViewport.Y) * sourcePixelHeight;
        var worldRight = Math.Max(1d, sourceViewport.X + sourceViewport.Width) *
                         sourcePixelWidth;
        var worldBottom = Math.Max(1d, sourceViewport.Y + sourceViewport.Height) *
                          sourcePixelHeight;
        var worldWidth = Math.Max(.001d, worldRight - worldLeft);
        var worldHeight = Math.Max(.001d, worldBottom - worldTop);
        var scale = Math.Min(availableWidth / worldWidth, availableHeight / worldHeight) * .86d;
        var contentWidth = worldWidth * scale;
        var contentHeight = worldHeight * scale;
        var contentLeft = (availableWidth - contentWidth) / 2d;
        var contentTop = (availableHeight - contentHeight) / 2d;
        var imageWidth = sourcePixelWidth * scale;
        var imageHeight = sourcePixelHeight * scale;
        var imageLeft = contentLeft - (worldLeft * scale);
        var imageTop = contentTop - (worldTop * scale);
        _sourceImageRect = new Rect(imageLeft, imageTop, imageWidth, imageHeight);
        FullSourceImageBorder.Width = imageWidth;
        FullSourceImageBorder.Height = imageHeight;
        Canvas.SetLeft(FullSourceImageBorder, imageLeft);
        Canvas.SetTop(FullSourceImageBorder, imageTop);

        _cropFrameRect = new Rect(
            imageLeft + (sourceViewport.X * imageWidth),
            imageTop + (sourceViewport.Y * imageHeight),
            Math.Max(1d, sourceViewport.Width * imageWidth),
            Math.Max(1d, sourceViewport.Height * imageHeight));
        PositionCropFrame();
        FullSourceStatusText.Text = _showOriginal
            ? $"主题原始资产 {sourcePixelWidth:0}×{sourcePixelHeight:0}  ·  中性效果"
            : $"当前素材完整图 {sourcePixelWidth:0}×{sourcePixelHeight:0}  ·  " +
              $"size {_placementProjection!.SizeCss}  ·  position {_placementProjection.PositionCss}";
    }

    private void PositionCropFrame()
    {
        CropFrame.Width = _cropFrameRect.Width;
        CropFrame.Height = _cropFrameRect.Height;
        Canvas.SetLeft(CropFrame, _cropFrameRect.Left);
        Canvas.SetTop(CropFrame, _cropFrameRect.Top);

        PositionCropHandle(CropHandleTopLeft, _cropFrameRect.Left, _cropFrameRect.Top);
        PositionCropHandle(CropHandleTopRight, _cropFrameRect.Right, _cropFrameRect.Top);
        PositionCropHandle(CropHandleBottomLeft, _cropFrameRect.Left, _cropFrameRect.Bottom);
        PositionCropHandle(CropHandleBottomRight, _cropFrameRect.Right, _cropFrameRect.Bottom);
        PositionCropShades();
    }

    private static void PositionCropHandle(Thumb handle, double x, double y)
    {
        Canvas.SetLeft(handle, x - handle.Width / 2d);
        Canvas.SetTop(handle, y - handle.Height / 2d);
    }

    private void PositionCropShades()
    {
        var width = Math.Max(0d, FullSourceCanvas.ActualWidth);
        var height = Math.Max(0d, FullSourceCanvas.ActualHeight);
        SetCanvasRect(CropShadeTop, 0, 0, width, Math.Max(0d, _cropFrameRect.Top));
        SetCanvasRect(
            CropShadeBottom,
            0,
            Math.Max(0d, _cropFrameRect.Bottom),
            width,
            Math.Max(0d, height - _cropFrameRect.Bottom));
        SetCanvasRect(
            CropShadeLeft,
            0,
            Math.Max(0d, _cropFrameRect.Top),
            Math.Max(0d, _cropFrameRect.Left),
            Math.Max(0d, _cropFrameRect.Height));
        SetCanvasRect(
            CropShadeRight,
            Math.Max(0d, _cropFrameRect.Right),
            Math.Max(0d, _cropFrameRect.Top),
            Math.Max(0d, width - _cropFrameRect.Right),
            Math.Max(0d, _cropFrameRect.Height));
    }

    private static void SetCanvasRect(
        FrameworkElement element,
        double left,
        double top,
        double width,
        double height)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = width;
        element.Height = height;
    }

    private void SetCropAdornerVisibility(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        CropFrame.Visibility = visibility;
        CropHandleTopLeft.Visibility = visibility;
        CropHandleTopRight.Visibility = visibility;
        CropHandleBottomLeft.Visibility = visibility;
        CropHandleBottomRight.Visibility = visibility;
        CropShadeTop.Visibility = visibility;
        CropShadeLeft.Visibility = visibility;
        CropShadeRight.Visibility = visibility;
        CropShadeBottom.Visibility = visibility;
    }

    private void CropFrame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_originalSource is null || _showOriginal) return;
        _dragging = true;
        _cropDragOrigin = e.GetPosition(FullSourceCanvas);
        _cropDragSourceImageRect = _sourceImageRect;
        _cropDragFrameRect = _cropFrameRect;
        CropFrame.CaptureMouse();
        CropFrame.Cursor = Cursors.SizeAll;
        InteractionStarted?.Invoke(this, EventArgs.Empty);
        Focus();
        e.Handled = true;
    }

    private void CropFrame_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(FullSourceCanvas);
        CropFrameChanged?.Invoke(
            this,
            new ArtworkCropFrameChangedEventArgs(
                "move",
                current - _cropDragOrigin,
                _cropDragSourceImageRect,
                _cropDragFrameRect));
        e.Handled = true;
    }

    private void CropFrame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CompleteCropFrameDrag();
        e.Handled = true;
    }

    private void CropFrame_LostMouseCapture(object sender, MouseEventArgs e) =>
        CompleteCropFrameDrag();

    private void CompleteCropFrameDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        CropFrame.ReleaseMouseCapture();
        CropFrame.Cursor = Cursors.SizeAll;
        InteractionCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void CropFrame_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_originalSource is null || _showOriginal) return;
        ZoomRequested?.Invoke(
            this,
            new ArtworkCanvasZoomEventArgs(
                Math.Sign(e.Delta),
                e.GetPosition(FullSourceCanvas),
                _sourceImageRect,
                _cropFrameRect));
        e.Handled = true;
    }

    private void CropHandle_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (_originalSource is null || _showOriginal) return;
        _cropResizeDelta = default;
        _cropResizeSourceImageRect = _sourceImageRect;
        _cropResizeFrameRect = _cropFrameRect;
        InteractionStarted?.Invoke(this, EventArgs.Empty);
    }

    private void CropHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: string handle } || _showOriginal) return;
        _cropResizeDelta += new Vector(e.HorizontalChange, e.VerticalChange);
        CropFrameChanged?.Invoke(
            this,
            new ArtworkCropFrameChangedEventArgs(
                handle,
                _cropResizeDelta,
                _cropResizeSourceImageRect,
                _cropResizeFrameRect));
    }

    private void CropHandle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_originalSource is null || _showOriginal) return;
        InteractionCompleted?.Invoke(this, EventArgs.Empty);
    }
}
