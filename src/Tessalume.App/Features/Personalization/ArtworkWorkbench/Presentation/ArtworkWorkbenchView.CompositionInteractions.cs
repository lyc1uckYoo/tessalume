using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkWorkbenchView
{
    private void PreviewCanvas_InteractionStarted(object? sender, EventArgs e)
    {
        if (!CanEdit()) return;
        EndWheelGesture();
        _compositionGestureStart = PreviewCanvas.PlacementProjection;
        if (_compositionGestureStart is null) return;
        SetCompositionEditing(true);
        _session.BeginGesture(_themeId!, _settings);
    }

    private void PreviewCanvas_DragRequested(object? sender, ArtworkCanvasDragEventArgs e)
    {
        if (!CanEdit() ||
            _compositionGestureStart is not { } start ||
            e.ViewportSize.Width <= 0 ||
            e.ViewportSize.Height <= 0) return;
        var deltaX = -e.TotalDelta.X / e.ViewportSize.Width * start.SourceViewport.Width;
        var deltaY = -e.TotalDelta.Y / e.ViewportSize.Height * start.SourceViewport.Height;
        if (start.IsHorizontallyMirrored) deltaX = -deltaX;
        if (start.IsVerticallyMirrored) deltaY = -deltaY;
        var mutation = ArtworkPlacementMapper.MoveCrop(
            start.SourceProjection,
            deltaX,
            deltaY,
            PreviewCanvas.SourcePixelSize,
            PreviewCanvas.TargetSize);
        ApplyCropMutation(
            mutation,
            start,
            "拖动区域效果调整最终取景",
            IsBlockedInDirection(mutation, deltaX, deltaY));
    }

    private void PreviewCanvas_CropFrameChanged(
        object? sender,
        ArtworkCropFrameChangedEventArgs e)
    {
        if (!CanEdit() ||
            _compositionGestureStart is not { } start ||
            e.SourceImageBounds.Width <= 0 ||
            e.SourceImageBounds.Height <= 0) return;
        ArtworkCropMutationResult mutation;
        bool showBoundary;
        if (e.Handle == "move")
        {
            var deltaX = e.TotalDelta.X / e.SourceImageBounds.Width;
            var deltaY = e.TotalDelta.Y / e.SourceImageBounds.Height;
            mutation = ArtworkPlacementMapper.MoveCrop(
                start.SourceProjection,
                deltaX,
                deltaY,
                PreviewCanvas.SourcePixelSize,
                PreviewCanvas.TargetSize);
            showBoundary = IsBlockedInDirection(mutation, deltaX, deltaY);
        }
        else
        {
            var fromLeft = e.Handle is "topLeft" or "bottomLeft";
            var fromTop = e.Handle is "topLeft" or "topRight";
            var widthFactor = 1d +
                ((fromLeft ? -e.TotalDelta.X : e.TotalDelta.X) /
                 Math.Max(1d, e.CropFrameBounds.Width));
            var heightFactor = 1d +
                ((fromTop ? -e.TotalDelta.Y : e.TotalDelta.Y) /
                 Math.Max(1d, e.CropFrameBounds.Height));
            var factor = (widthFactor + heightFactor) / 2d;
            mutation = ArtworkPlacementMapper.ResizeCrop(
                start.SourceProjection,
                factor,
                fromLeft ? 1d : 0d,
                fromTop ? 1d : 0d,
                PreviewCanvas.SourcePixelSize,
                PreviewCanvas.TargetSize);
            showBoundary = factor > 1d && HasBoundary(mutation);
        }
        ApplyCropMutation(mutation, start, "调整最终取景框", showBoundary);
    }

    private void ApplyCropMutation(
        ArtworkCropMutationResult mutation,
        ArtworkPlacementProjection projection,
        string description,
        bool showBoundary)
    {
        var placement = ArtworkPlacementMapper.CommitCrop(
            mutation.Crop,
            PreviewCanvas.SourcePixelSize,
            PreviewCanvas.TargetSize,
            projection.IsHorizontallyMirrored,
            projection.IsVerticallyMirrored);
        var next = ArtworkWorkbenchSession.UpdateGesture(
            _settings,
            settings => ArtworkSettingsReducer.SetCustomPlacement(
                settings,
                _mode,
                _region,
                placement));
        if (ThemeVisualSettingsSemanticComparer.Instance.Equals(next, _settings))
        {
            if (showBoundary) PreviewCanvas.ShowBoundaryFeedback();
            return;
        }
        _settings = next;
        CompleteSettingsChange(description, recordAlreadyCreated: true);
        if (showBoundary) PreviewCanvas.ShowBoundaryFeedback();
    }

    private void PreviewCanvas_InteractionCompleted(object? sender, EventArgs e)
    {
        if (!CanEdit()) return;
        _session.EndGesture(_themeId!, _settings);
        _compositionGestureStart = null;
        SetCompositionEditing(false);
        UpdateHistoryActions();
    }

    private void PreviewCanvas_ZoomRequested(object? sender, ArtworkCanvasZoomEventArgs e)
    {
        if (!CanEdit()) return;
        if (PreviewCanvas.PlacementProjection is not { } projection) return;
        SetCompositionEditing(true);
        var status = _session.History.GetStatus(_themeId!);
        if (!status.GestureActive)
        {
            _session.BeginGesture(_themeId!, _settings);
        }
        var focalX = projection.SourceProjection.SourceX +
                     (projection.SourceProjection.SourceWidth / 2d);
        var focalY = projection.SourceProjection.SourceY +
                     (projection.SourceProjection.SourceHeight / 2d);
        if (!e.SourceImageBounds.IsEmpty)
        {
            focalX = (e.Anchor.X - e.SourceImageBounds.Left) /
                     e.SourceImageBounds.Width;
            focalY = (e.Anchor.Y - e.SourceImageBounds.Top) /
                     e.SourceImageBounds.Height;
        }
        else if (PreviewCanvas.ViewportSize.Width > 0 && PreviewCanvas.ViewportSize.Height > 0)
        {
            focalX = projection.SourceViewport.X +
                     (e.Anchor.X / PreviewCanvas.ViewportSize.Width *
                      projection.SourceViewport.Width);
            focalY = projection.SourceViewport.Y +
                     (e.Anchor.Y / PreviewCanvas.ViewportSize.Height *
                      projection.SourceViewport.Height);
        }
        var mutation = ArtworkPlacementMapper.ZoomAt(
            projection.SourceProjection,
            Math.Pow(1.08d, e.Detents),
            focalX,
            focalY,
            PreviewCanvas.SourcePixelSize,
            PreviewCanvas.TargetSize);
        var placement = ArtworkPlacementMapper.CommitCrop(
            mutation.Crop,
            PreviewCanvas.SourcePixelSize,
            PreviewCanvas.TargetSize,
            projection.IsHorizontallyMirrored,
            projection.IsVerticallyMirrored);
        var next = ArtworkWorkbenchSession.UpdateGesture(
            _settings,
            settings => ArtworkSettingsReducer.SetCustomPlacement(
                settings,
                _mode,
                _region,
                placement));
        if (!ThemeVisualSettingsSemanticComparer.Instance.Equals(next, _settings))
        {
            _settings = next;
            CompleteSettingsChange("滚轮调整缩放", recordAlreadyCreated: true);
        }
        if (e.Detents < 0 && HasBoundary(mutation)) PreviewCanvas.ShowBoundaryFeedback();
        _wheelTimer.Stop();
        _wheelTimer.Start();
    }

    private void WheelTimer_Tick(object? sender, EventArgs e)
    {
        _wheelTimer.Stop();
        EndWheelGesture();
    }

    private void EndWheelGesture()
    {
        if (_themeId is null) return;
        _wheelTimer.Stop();
        _session.EndGesture(_themeId, _settings);
        SetCompositionEditing(false);
        UpdateHistoryActions();
    }

    private void GuideToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (PreviewCanvas is null) return;
        PreviewCanvas.SetGuidesVisible(GuideToggleButton.IsChecked == true);
    }

    private void CanvasView_Click(object sender, RoutedEventArgs e)
    {
        if (_showOriginal) return;
        _canvasViewMode = sender is ToggleButton { CommandParameter: "result" }
            ? ArtworkCanvasViewMode.Result
            : ArtworkCanvasViewMode.FullSource;
        RenderCanvasViewMode();
    }

    private void RenderCanvasViewMode()
    {
        if (PreviewCanvas is null) return;
        FullSourceViewButton.IsChecked = _canvasViewMode == ArtworkCanvasViewMode.FullSource;
        ResultViewButton.IsChecked = _canvasViewMode == ArtworkCanvasViewMode.Result;
        PreviewCanvas.SetViewMode(_canvasViewMode);
        UpdateResponsiveLayout(ActualWidth);
        CanvasViewHintText.Text = _canvasViewMode == ArtworkCanvasViewMode.FullSource
            ? "完整原图始终可见；金色框是目标区域最终取景。"
            : "这里只显示目标区域的最终裁切、滤镜与可读性效果。";
        RenderMotionPreviewState();
    }

    private void MotionPreview_Click(object sender, RoutedEventArgs e)
    {
        _motionPreviewRequested = MotionPreviewToggleButton.IsChecked == true;
        RenderMotionPreviewState();
    }

    private void RenderMotionPreviewState()
    {
        if (MotionPreviewToggleButton is null || PreviewCanvas is null) return;
        var motion = CurrentAdjustment.Motion?.Normalize();
        var hasMotion = motion is { Mode: "loop", Keyframes.Count: > 0 };
        var displayAllowsMotion = !string.Equals(
            _settings.Display.MotionIntensity,
            "off",
            StringComparison.OrdinalIgnoreCase);
        var reducedMotion = string.Equals(
            _settings.Display.MotionIntensity,
            "reduced",
            StringComparison.OrdinalIgnoreCase);
        var active = hasMotion &&
            displayAllowsMotion &&
            _motionPreviewRequested &&
            _canvasViewMode == ArtworkCanvasViewMode.Result &&
            !_showOriginal &&
            !_compositionEditing;
        MotionPreviewToggleButton.IsEnabled = CanEdit() && hasMotion && displayAllowsMotion;
        MotionPreviewToggleButton.IsChecked = _motionPreviewRequested && displayAllowsMotion;
        MotionPreviewToggleButton.Content = active
            ? "暂停动效"
            : _motionPreviewRequested &&
              _canvasViewMode == ArtworkCanvasViewMode.Result &&
              _compositionEditing
                ? "动效已暂停"
                : "预览动效";
        var status = !hasMotion
            ? "当前槽位没有图片动效"
            : !displayAllowsMotion
                ? "全局动效设置已关闭"
                : active
                    ? reducedMotion
                        ? "区域效果正在预览轻量图片动效"
                        : "区域效果正在预览图片动效"
                    : _canvasViewMode == ArtworkCanvasViewMode.FullSource
                        ? "全图取景始终暂停动效"
                        : _compositionEditing
                            ? "构图编辑期间已暂停动效"
                            : "图片动效已暂停";
        MotionPreviewToggleButton.ToolTip = status + "；此开关不写入参数或撤销历史";
        AutomationProperties.SetItemStatus(MotionPreviewToggleButton, status);
        PreviewCanvas.SetMotionPreview(active, reducedMotion);
    }

    private void Inspector_PlacementEditingStarted(object? sender, EventArgs e) =>
        SetCompositionEditing(true);

    private void Inspector_PlacementEditingCompleted(object? sender, EventArgs e) =>
        SetCompositionEditing(false);

    private void SetCompositionEditing(bool editing)
    {
        if (_compositionEditing == editing) return;
        _compositionEditing = editing;
        RenderMotionPreviewState();
    }

    private void Compare_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!BeginOriginalComparison()) return;
        CompareButton.CaptureMouse();
        e.Handled = true;
    }

    private void Compare_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndOriginalComparison();
        e.Handled = true;
    }

    private void Compare_LostMouseCapture(object sender, MouseEventArgs e) =>
        EndOriginalComparison();

    private void Compare_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter) || e.IsRepeat) return;
        if (BeginOriginalComparison()) e.Handled = true;
    }

    private void Compare_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        EndOriginalComparison();
        e.Handled = true;
    }

    private void Compare_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        EndOriginalComparison();

    private bool BeginOriginalComparison()
    {
        if (_themeOriginalPreview is null) return false;
        if (_showOriginal) return true;
        _showOriginal = true;
        _comparisonReturnViewMode = _canvasViewMode;
        _canvasViewMode = ArtworkCanvasViewMode.FullSource;
        RenderCanvasViewMode();
        CompareButton.Content = "正在显示主题原图";
        AutomationProperties.SetItemStatus(CompareButton, "正在显示主题原图");
        PreviewCanvas.SetShowOriginal(true);
        RenderMotionPreviewState();
        return true;
    }

    private void EndOriginalComparison()
    {
        if (!_showOriginal) return;
        _showOriginal = false;
        CompareButton.ReleaseMouseCapture();
        CompareButton.Content = "按住看主题原图";
        AutomationProperties.SetItemStatus(CompareButton, "显示当前调整");
        PreviewCanvas.SetShowOriginal(false);
        if (_comparisonReturnViewMode is { } returnMode)
        {
            _canvasViewMode = returnMode;
            _comparisonReturnViewMode = null;
            RenderCanvasViewMode();
        }
        else
        {
            RenderMotionPreviewState();
        }
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewCanvas.PlacementProjection is not { } projection) return;
        ApplyCustomPlacement(
            ArtworkPlacementMapper.Contain(
                projection.SourceProjection.AlignmentX,
                projection.SourceProjection.AlignmentY,
                projection.IsHorizontallyMirrored,
                projection.IsVerticallyMirrored),
            "显示完整图片");
    }

    private void Fill_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewCanvas.PlacementProjection is not { } projection) return;
        ApplyCustomPlacement(
            ArtworkPlacementMapper.Fill(
                PreviewCanvas.SourcePixelSize,
                PreviewCanvas.TargetSize,
                mirrorX: projection.IsHorizontallyMirrored,
                mirrorY: projection.IsVerticallyMirrored),
            "填满区域");
    }

    private void Center_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentSlotResolution?.ThemeDefaultAdjustment.Placement is not { } themeDefault ||
            !PreviewCanvas.SourcePixelSize.IsValid ||
            !PreviewCanvas.TargetSize.IsValid) return;
        var custom = ArtworkPlacementMapper.ConvertToCustomEquivalent(
            CurrentAdjustment,
            themeDefault,
            PreviewCanvas.SourcePixelSize,
            PreviewCanvas.TargetSize);
        ApplyCustomPlacement(
            ArtworkPlacementMapper.Center(custom.Placement ?? themeDefault),
            "居中图片");
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ChangeZoom(zoomIn: false);

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ChangeZoom(zoomIn: true);

    private void ChangeZoom(bool zoomIn)
    {
        if (!CanEdit() || PreviewCanvas.PlacementProjection is not { } projection) return;
        var mutation = ArtworkPlacementMapper.ResizeCrop(
            projection.SourceProjection,
            zoomIn ? .92d : 1.08d,
            .5d,
            .5d,
            PreviewCanvas.SourcePixelSize,
            PreviewCanvas.TargetSize);
        ApplyCustomPlacement(
            ArtworkPlacementMapper.CommitCrop(
                mutation.Crop,
                PreviewCanvas.SourcePixelSize,
                PreviewCanvas.TargetSize,
                projection.IsHorizontallyMirrored,
                projection.IsVerticallyMirrored),
            zoomIn ? "放大图片" : "缩小图片");
        if (!zoomIn && HasBoundary(mutation)) PreviewCanvas.ShowBoundaryFeedback();
    }

    private void ApplyCustomPlacement(
        ThemeArtworkPlacementSpec placement,
        string description)
    {
        if (!CanEdit()) return;
        if (!ApplyDiscrete(
                settings => ArtworkSettingsReducer.SetCustomPlacement(
                    settings,
                    _mode,
                    _region,
                    placement),
                description)) return;
        Notify($"已{description} · 可撤销");
    }

    private static bool HasBoundary(ArtworkCropMutationResult mutation) =>
        mutation.HitLeft || mutation.HitTop || mutation.HitRight || mutation.HitBottom;

    private static bool IsBlockedInDirection(
        ArtworkCropMutationResult mutation,
        double deltaX,
        double deltaY) =>
        (deltaX < 0d && mutation.HitLeft) ||
        (deltaX > 0d && mutation.HitRight) ||
        (deltaY < 0d && mutation.HitTop) ||
        (deltaY > 0d && mutation.HitBottom);
}
