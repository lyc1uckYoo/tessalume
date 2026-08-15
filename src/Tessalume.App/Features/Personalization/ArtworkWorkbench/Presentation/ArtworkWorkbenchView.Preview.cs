using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkWorkbenchView
{
    private void QueuePreviewReload()
    {
        if (_disposed) return;
        _resolvedSource = null;
        ClearPreviewFailure();
        RenderSourceSummary();
        _ = ObservePreviewReloadAsync();
    }

    private async Task ObservePreviewReloadAsync()
    {
        try
        {
            await ReloadPreviewAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PreviewCanvas.SetLoading(false);
            PreviewCanvas.SetEmptyMessage("预览发生未预期错误；参数仍已保存在本机。");
            SetPreviewFailure($"预览失败：{exception.Message}");
        }
    }

    private async Task ReloadPreviewAsync()
    {
        var version = ++_previewVersion;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var cancellationToken = _previewCancellation.Token;
        _effectCancellation?.Cancel();
        _effectCancellation?.Dispose();
        _effectCancellation = null;
        _effectVersion++;
        _effectTimer.Stop();
        EndOriginalComparison();

        if (!CanEdit() || _package is null || _personalImageStore is null)
        {
            _resolvedSource = null;
            _originalPreview = null;
            _themeOriginalPreview = null;
            PreviewCanvas.SetSources(null);
            PreviewCanvas.SetLoading(false);
            PreviewCanvas.SetEmptyMessage("选择有效主题后，这里会显示当前区域的实际素材。");
            CompareButton.IsEnabled = false;
            return;
        }

        var adjustment = CurrentAdjustment;
        var source = ArtworkImageSourceResolver.Resolve(
            _package,
            _personalImageStore,
            _region,
            _mode,
            adjustment);
        var themeOriginalSource = ArtworkImageSourceResolver.Resolve(
            _package,
            _personalImageStore,
            _region,
            _mode,
            adjustment with { CustomImagePath = null });
        _resolvedSource = source;
        RenderSourceSummary();
        if (source is null)
        {
            _originalPreview = null;
            _themeOriginalPreview = null;
            PreviewCanvas.SetSources(null);
            PreviewCanvas.SetEmptyMessage("当前主题没有声明这个区域与模式的可用图片。");
            PreviewCanvas.SetLoading(false);
            CompareButton.IsEnabled = false;
            return;
        }

        _themeOriginalPreview = null;
        CompareButton.IsEnabled = false;
        PreviewCanvas.SetLoading(true, "正在解码预览图片…");
        try
        {
            var decodeWidth = CalculatePreviewDecodeWidth();
            var bitmapTask = _imageCache.LoadWithMetadataAsync(
                source.AbsolutePath,
                decodeWidth,
                cancellationToken);
            var preview = await bitmapTask;
            var bitmap = preview.Bitmap;
            cancellationToken.ThrowIfCancellationRequested();
            if (version != _previewVersion) return;

            ArtworkPreviewBitmap? themeOriginal = null;
            if (themeOriginalSource is not null)
            {
                themeOriginal = string.Equals(
                    source.AbsolutePath,
                    themeOriginalSource.AbsolutePath,
                    StringComparison.OrdinalIgnoreCase)
                    ? preview
                    : await TryLoadThemeOriginalPreviewAsync(
                        themeOriginalSource.AbsolutePath,
                        decodeWidth,
                        cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (version != _previewVersion) return;

            _originalPreview = bitmap;
            _themeOriginalPreview = themeOriginal?.Bitmap;
            _lastDecodeWidth = decodeWidth;
            PreviewCanvas.SetSources(
                bitmap,
                themeOriginal: themeOriginal?.Bitmap,
                originalPixelSize: new ArtworkSize(
                    preview.SourcePixelWidth,
                    preview.SourcePixelHeight),
                themeOriginalPixelSize: themeOriginal is null
                    ? null
                    : new ArtworkSize(
                        themeOriginal.SourcePixelWidth,
                        themeOriginal.SourcePixelHeight));
            CompareButton.IsEnabled = themeOriginal is not null;
            CompareButton.ToolTip = themeOriginal is null
                ? "主题原始资产不可用；当前素材仍可正常取景与预览"
                : "按住临时查看 manifest 所指主题原始资产的完整中性图像；不会保存变化";
            RenderPlacementEditor(CurrentAdjustment);
            RenderMappingHint();
            var effectRevision = ++_effectVersion;
            _effectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await ProcessPreviewEffectsAsync(
                version,
                effectRevision,
                _effectCancellation.Token);
            if (version == _previewVersion && effectRevision == _effectVersion)
            {
                PreviewCanvas.SetLoading(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or
                InvalidOperationException or FileFormatException or FormatException or COMException)
        {
            if (version != _previewVersion) return;
            _originalPreview = null;
            _themeOriginalPreview = null;
            PreviewCanvas.SetSources(null);
            PreviewCanvas.SetLoading(false);
            PreviewCanvas.SetEmptyMessage($"无法加载预览：{exception.Message}");
            CompareButton.IsEnabled = false;
            SetPreviewFailure($"预览图片加载失败：{exception.Message}");
        }
    }

    private async Task<ArtworkPreviewBitmap?> TryLoadThemeOriginalPreviewAsync(
        string absolutePath,
        int decodeWidth,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _imageCache.LoadWithMetadataAsync(
                absolutePath,
                decodeWidth,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or
                InvalidOperationException or FileFormatException or FormatException or COMException)
        {
            return null;
        }
    }

    private void ScheduleEffectProcessing()
    {
        if (_disposed || _originalPreview is null) return;
        _effectVersion++;
        _effectCancellation?.Cancel();
        _effectTimer.Stop();
        _effectTimer.Start();
    }

    private async void EffectTimer_Tick(object? sender, EventArgs e)
    {
        _effectTimer.Stop();
        var version = _previewVersion;
        _effectCancellation?.Cancel();
        _effectCancellation?.Dispose();
        _effectCancellation = _previewCancellation is { } previewCancellation
            ? CancellationTokenSource.CreateLinkedTokenSource(previewCancellation.Token)
            : new CancellationTokenSource();
        var effectRevision = _effectVersion;
        try
        {
            await ProcessPreviewEffectsAsync(
                version,
                effectRevision,
                _effectCancellation.Token);
            if (version == _previewVersion && effectRevision == _effectVersion)
            {
                // A parameter edit can cancel the initial effect pass after the
                // bitmap itself has loaded. The latest pass owns completion of
                // the loading state so the result view cannot remain veiled.
                PreviewCanvas.SetLoading(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessPreviewEffectsAsync(
        int version,
        int effectRevision,
        CancellationToken cancellationToken)
    {
        if (_originalPreview is null ||
            version != _previewVersion ||
            effectRevision != _effectVersion) return;
        try
        {
            var processed = await ArtworkPreviewPixelEffectProcessor.ProcessAsync(
                _originalPreview,
                CurrentAdjustment,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (version != _previewVersion || effectRevision != _effectVersion) return;
            PreviewCanvas.SetProcessedSource(processed);
            ClearPreviewFailure();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or OverflowException)
        {
            if (version != _previewVersion || effectRevision != _effectVersion) return;
            PreviewCanvas.SetProcessedSource(_originalPreview);
            SetPreviewFailure($"预览效果失败：{exception.Message}");
        }
    }

    private int CalculatePreviewDecodeWidth()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var logicalWidth = Math.Max(420d, PreviewCanvas.ActualWidth);
        return (int)Math.Clamp(
            Math.Ceiling(logicalWidth * Math.Max(1d, dpi.DpiScaleX) * 1.35d),
            480d,
            1800d);
    }

    private void ArtworkWorkbenchView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_previewReloadRequired)
        {
            _previewReloadRequired = false;
            QueuePreviewReload();
            return;
        }
        ScheduleLayoutAwarePreviewReload();
    }

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ScheduleLayoutAwarePreviewReload();

    private void ScheduleLayoutAwarePreviewReload()
    {
        if (!CanEdit() || !IsLoaded) return;
        var requiredWidth = CalculatePreviewDecodeWidth();
        if (_originalPreview is not null && requiredWidth <= _lastDecodeWidth * 1.12d) return;
        _previewLayoutTimer.Stop();
        _previewLayoutTimer.Start();
    }

    private void PreviewLayoutTimer_Tick(object? sender, EventArgs e)
    {
        _previewLayoutTimer.Stop();
        if (CanEdit()) QueuePreviewReload();
    }

    private void SetPreviewFailure(string detail)
    {
        PreviewFailureBadge.Visibility = Visibility.Visible;
        PreviewFailureBadge.ToolTip = detail;
        AutomationProperties.SetName(PreviewFailureBadge, detail);
    }

    private void ClearPreviewFailure()
    {
        if (PreviewFailureBadge is null) return;
        PreviewFailureBadge.Visibility = Visibility.Collapsed;
        PreviewFailureBadge.ToolTip = null;
        AutomationProperties.SetName(PreviewFailureBadge, "预览正常");
    }
}
