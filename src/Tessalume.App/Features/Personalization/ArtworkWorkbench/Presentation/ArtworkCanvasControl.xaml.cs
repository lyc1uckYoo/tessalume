using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

internal enum ArtworkCanvasViewMode
{
    FullSource,
    Result,
}

internal sealed class ArtworkCanvasDragEventArgs(Vector totalDelta, Size viewportSize) : EventArgs
{
    public Vector TotalDelta { get; } = totalDelta;

    public Size ViewportSize { get; } = viewportSize;
}

internal sealed class ArtworkCanvasZoomEventArgs(
    int detents,
    Point anchor,
    Rect sourceImageBounds,
    Rect cropFrameBounds) : EventArgs
{
    public int Detents { get; } = detents;

    public Point Anchor { get; } = anchor;

    public Rect SourceImageBounds { get; } = sourceImageBounds;

    public Rect CropFrameBounds { get; } = cropFrameBounds;
}

internal sealed class ArtworkCropFrameChangedEventArgs(
    string handle,
    Vector totalDelta,
    Rect sourceImageBounds,
    Rect cropFrameBounds) : EventArgs
{
    public string Handle { get; } = handle;

    public Vector TotalDelta { get; } = totalDelta;

    public Rect SourceImageBounds { get; } = sourceImageBounds;

    public Rect CropFrameBounds { get; } = cropFrameBounds;
}

public partial class ArtworkCanvasControl : UserControl
{
    private ArtworkRegion _region = ArtworkRegion.Hero;
    private ArtworkColorMode _mode = ArtworkColorMode.Light;
    private ThemeArtworkAdjustment _adjustment = new();
    private ThemeArtworkPlacementSpec _themeDefaultPlacement = new();
    private ArtworkPlacementProjection? _placementProjection;
    private BitmapSource? _originalSource;
    private BitmapSource? _processedSource;
    private BitmapSource? _themeOriginalSource;
    private ArtworkSize _sourcePixelSize;
    private ArtworkSize _themeOriginalPixelSize;
    private Point _dragOrigin;
    private bool _dragging;
    private bool _showOriginal;
    private bool _showGuides = true;
    private ArtworkCanvasViewMode _viewMode = ArtworkCanvasViewMode.FullSource;
    private Point _cropDragOrigin;
    private Rect _cropDragSourceImageRect;
    private Rect _cropDragFrameRect;
    private Rect _cropFrameRect;
    private Rect _sourceImageRect;
    private Vector _cropResizeDelta;
    private Rect _cropResizeSourceImageRect;
    private Rect _cropResizeFrameRect;
    private Size _targetViewport = new(1440d, 420d);

    public ArtworkCanvasControl()
    {
        InitializeComponent();
        ViewportGrid.SizeChanged += (_, _) =>
        {
            UpdateResultPlacement();
            UpdateGuides();
            UpdateMotionPreview();
        };
        FullSourceHost.SizeChanged += (_, _) => UpdateFullSourceLayout();
        FullSourceCanvas.SizeChanged += (_, _) => UpdateFullSourceLayout();
    }

    internal event EventHandler? InteractionStarted;

    internal event EventHandler<ArtworkCanvasDragEventArgs>? DragRequested;

    internal event EventHandler? InteractionCompleted;

    internal event EventHandler<ArtworkCanvasZoomEventArgs>? ZoomRequested;

    internal event EventHandler<ArtworkCropFrameChangedEventArgs>? CropFrameChanged;

    internal Size ViewportSize => ViewportGrid.RenderSize;

    internal ArtworkPlacementProjection? PlacementProjection => _placementProjection;

    internal ArtworkSize SourcePixelSize => _originalSource is null
        ? default
        : _sourcePixelSize;

    internal ArtworkSize TargetSize => new(_targetViewport.Width, _targetViewport.Height);

    internal ArtworkCanvasViewMode ViewMode => _viewMode;

    internal void SetViewMode(ArtworkCanvasViewMode viewMode)
    {
        _viewMode = viewMode;
        FullSourceStage.Visibility = viewMode == ArtworkCanvasViewMode.FullSource
            ? Visibility.Visible
            : Visibility.Collapsed;
        PreviewStage.Visibility = viewMode == ArtworkCanvasViewMode.Result
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (viewMode == ArtworkCanvasViewMode.FullSource)
        {
            UpdateFullSourceLayout();
        }
        else
        {
            ResizeViewport(CanvasHost.RenderSize);
        }
        UpdateMotionPreview();
    }

    internal void SetRegion(ArtworkRegion region)
    {
        _region = region;
        HeroMock.Visibility = region == ArtworkRegion.Hero ? Visibility.Visible : Visibility.Collapsed;
        SidebarMock.Visibility = region == ArtworkRegion.Sidebar ? Visibility.Visible : Visibility.Collapsed;
        ChatMock.Visibility = region == ArtworkRegion.Chat ? Visibility.Visible : Visibility.Collapsed;
        ResizeViewport(CanvasHost.RenderSize);
        UpdateFullSourceLayout();
        UpdateGuides();
        UpdateMockPalette();
        UpdatePlacementProjection();
    }

    internal void SetColorMode(ArtworkColorMode mode)
    {
        _mode = mode;
        UpdateMockPalette();
        UpdateEffects();
    }

    internal void SetTargetViewport(Size viewport)
    {
        if (!double.IsFinite(viewport.Width) ||
            !double.IsFinite(viewport.Height) ||
            viewport.Width <= 0 ||
            viewport.Height <= 0)
        {
            return;
        }
        _targetViewport = viewport;
        ResizeViewport(CanvasHost.RenderSize);
        UpdatePlacementProjection();
        UpdateEffects();
        UpdateMotionPreview();
    }

    internal void SetAdjustment(ThemeArtworkAdjustment adjustment)
    {
        SetComposition(
            adjustment,
            adjustment?.Placement ?? new ThemeArtworkPlacementSpec());
    }

    internal void SetComposition(
        ThemeArtworkAdjustment adjustment,
        ThemeArtworkPlacementSpec themeDefaultPlacement)
    {
        _adjustment = (adjustment ?? new ThemeArtworkAdjustment()).Normalize();
        _themeDefaultPlacement = (themeDefaultPlacement ?? new ThemeArtworkPlacementSpec()).Normalize();
        UpdatePlacementProjection();
        UpdateEffects();
        UpdateMotionPreview();
    }

    internal void SetSources(
        BitmapSource? original,
        BitmapSource? processed = null,
        BitmapSource? themeOriginal = null,
        ArtworkSize? originalPixelSize = null,
        ArtworkSize? themeOriginalPixelSize = null)
    {
        _originalSource = original;
        _processedSource = processed ?? original;
        _themeOriginalSource = themeOriginal ?? original;
        _sourcePixelSize = original is null
            ? default
            : originalPixelSize is { IsValid: true } sourceSize
                ? sourceSize
                : new ArtworkSize(original.PixelWidth, original.PixelHeight);
        _themeOriginalPixelSize = _themeOriginalSource is null
            ? default
            : themeOriginalPixelSize is { IsValid: true } themeSize
                ? themeSize
                : new ArtworkSize(
                    _themeOriginalSource.PixelWidth,
                    _themeOriginalSource.PixelHeight);
        if (_themeOriginalSource is null) _showOriginal = false;
        ArtworkImage.Source = _showOriginal ? _themeOriginalSource : _processedSource;
        // Full-source framing is deliberately neutral. Effects only belong to the
        // final-result view and never obscure the source while the crop is edited.
        FullSourceImage.Source = _showOriginal ? _themeOriginalSource : _originalSource;
        EmptyOverlay.Visibility = original is null ? Visibility.Visible : Visibility.Collapsed;
        FullSourceEmptyOverlay.Visibility = original is null ? Visibility.Visible : Visibility.Collapsed;
        UpdatePlacementProjection();
    }

    internal void SetProcessedSource(BitmapSource? processed)
    {
        _processedSource = processed ?? _originalSource;
        if (!_showOriginal) ArtworkImage.Source = _processedSource;
    }

    internal void SetShowOriginal(bool showOriginal)
    {
        _showOriginal = showOriginal && _themeOriginalSource is not null;
        CompareBadge.Visibility = _showOriginal ? Visibility.Visible : Visibility.Collapsed;
        OriginalBaselineBadge.Visibility = _showOriginal ? Visibility.Visible : Visibility.Collapsed;
        ArtworkImage.Source = _showOriginal ? _themeOriginalSource : _processedSource;
        FullSourceImage.Source = _showOriginal ? _themeOriginalSource : _originalSource;
        SetCropAdornerVisibility(!_showOriginal);
        UpdateFullSourceLayout();
        UpdateResultPlacement();
        UpdateEffects();
        UpdateMotionPreview();
    }

    internal void SetGuidesVisible(bool visible)
    {
        _showGuides = visible;
        GuideLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SetLoading(bool loading, string? message = null)
    {
        LoadingText.Text = string.IsNullOrWhiteSpace(message) ? "正在加载预览…" : message;
        FullSourceLoadingText.Text = string.IsNullOrWhiteSpace(message) ? "正在加载完整原图…" : message;
        LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        FullSourceLoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SetEmptyMessage(string message)
    {
        EmptyText.Text = message;
        FullSourceEmptyText.Text = message;
        EmptyOverlay.Visibility = Visibility.Visible;
        FullSourceEmptyOverlay.Visibility = Visibility.Visible;
    }

    internal void ShowBoundaryFeedback()
    {
        BoundaryFlash.Visibility = Visibility.Visible;
        FullSourceBoundaryFlash.Visibility = Visibility.Visible;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(240),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            BoundaryFlash.Visibility = Visibility.Collapsed;
            FullSourceBoundaryFlash.Visibility = Visibility.Collapsed;
        };
        timer.Start();
    }

    private void CanvasHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ResizeCanvas(e.NewSize);

    private void ResizeCanvas(Size size)
    {
        if (_viewMode == ArtworkCanvasViewMode.Result)
        {
            ResizeViewport(size);
        }
        else
        {
            UpdateFullSourceLayout();
        }
    }

    private void ResizeViewport(Size hostSize)
    {
        if (hostSize.Width <= 0 || hostSize.Height <= 0) return;
        var availableWidth = Math.Max(120, hostSize.Width - 22);
        var availableHeight = Math.Max(220, hostSize.Height - 22);
        ViewportBorder.HorizontalAlignment = HorizontalAlignment.Center;
        var reference = new ArtworkSize(_targetViewport.Width, _targetViewport.Height);
        var aspectRatio = reference.Width / reference.Height;

        var width = availableWidth;
        var height = width / aspectRatio;
        if (height > availableHeight)
        {
            height = availableHeight;
            width = height * aspectRatio;
        }
        PreviewStage.Width = Math.Max(1, width);
        PreviewStage.Height = Math.Max(1, height);
        ViewportBorder.Width = Math.Max(1, width);
        ViewportBorder.Height = Math.Max(1, height);
        UpdateResultPlacement();
    }

    private void UpdatePlacementProjection()
    {
        if (_originalSource is null)
        {
            _placementProjection = null;
            return;
        }
        _placementProjection = ArtworkPlacementMapper.ResolveEffectivePlacement(
            _adjustment,
            _themeDefaultPlacement,
            SourcePixelSize,
            TargetSize);
        UpdateFullSourceLayout();
        UpdateResultPlacement();
    }

    private void UpdateResultPlacement()
    {
        if (_placementProjection is null ||
            ViewportGrid.ActualWidth <= 0 ||
            ViewportGrid.ActualHeight <= 0) return;
        var scaleX = ViewportGrid.ActualWidth / _targetViewport.Width;
        var scaleY = ViewportGrid.ActualHeight / _targetViewport.Height;
        var rendered = _placementProjection.RenderedImage;
        ArtworkImage.Width = Math.Max(0.001d, rendered.Width * scaleX);
        ArtworkImage.Height = Math.Max(0.001d, rendered.Height * scaleY);
        Canvas.SetLeft(ArtworkImage, rendered.X * scaleX);
        Canvas.SetTop(ArtworkImage, rendered.Y * scaleY);
        ArtworkImage.RenderTransformOrigin = new Point(.5d, .5d);
        ArtworkImage.RenderTransform =
            _placementProjection.IsHorizontallyMirrored ||
            _placementProjection.IsVerticallyMirrored
                ? new ScaleTransform(
                    _placementProjection.IsHorizontallyMirrored ? -1d : 1d,
                    _placementProjection.IsVerticallyMirrored ? -1d : 1d)
                : Transform.Identity;
        ArtworkLayer.RenderTransform = Transform.Identity;
        CanvasStatusText.Text = _showOriginal
            ? "原始资源 · 中性效果"
            : $"size {_placementProjection.SizeCss}  ·  position {_placementProjection.PositionCss}";
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_originalSource is null || _showOriginal) return;
        _dragging = true;
        _dragOrigin = e.GetPosition(ViewportGrid);
        ViewportBorder.CaptureMouse();
        ViewportBorder.Cursor = Cursors.SizeAll;
        InteractionStarted?.Invoke(this, EventArgs.Empty);
        Focus();
        e.Handled = true;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(ViewportGrid);
        DragRequested?.Invoke(
            this,
            new ArtworkCanvasDragEventArgs(current - _dragOrigin, ViewportGrid.RenderSize));
        e.Handled = true;
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CompleteDrag();
        e.Handled = true;
    }

    private void Viewport_LostMouseCapture(object sender, MouseEventArgs e) => CompleteDrag();

    private void CompleteDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        ViewportBorder.ReleaseMouseCapture();
        ViewportBorder.Cursor = Cursors.SizeAll;
        InteractionCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_originalSource is null || _showOriginal) return;
        ZoomRequested?.Invoke(
            this,
            new ArtworkCanvasZoomEventArgs(
                Math.Sign(e.Delta),
                e.GetPosition(ViewportGrid),
                Rect.Empty,
                Rect.Empty));
        e.Handled = true;
    }

}
