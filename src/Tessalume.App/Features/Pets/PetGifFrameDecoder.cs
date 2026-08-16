using System.IO;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tessalume.App.Features.Pets;

internal sealed record PetDecodedAnimation(
    IReadOnlyList<BitmapSource> Frames,
    IReadOnlyList<TimeSpan> FrameDurations,
    int PixelWidth,
    int PixelHeight,
    long EstimatedDecodedBytes,
    bool ReducedMotion);

internal static class PetGifFrameDecoder
{
    internal const long MaximumRetainedDecodedBytes = 24L * 1024 * 1024;
    internal const int MaximumDecodeDimension = 720;
    private const int BytesPerDecodedPixel = 4;
    private const int DefaultDelayMilliseconds = 100;

    public static PetDecodedAnimation Decode(
        PetPreviewFrame preview,
        int requestedPixelWidth,
        int requestedPixelHeight,
        bool reducedMotion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (string.IsNullOrWhiteSpace(preview.FilePath))
        {
            throw new InvalidDataException("动态预览路径为空。");
        }
        if (preview.ExpectedFrameCount is < 2 or > 24 ||
            preview.SourceWidth is <= 0 or > 1280 ||
            preview.SourceHeight is <= 0 or > 1280 ||
            preview.RepresentativeFrame < 0 ||
            preview.RepresentativeFrame >= preview.ExpectedFrameCount)
        {
            throw new InvalidDataException("动态预览元数据超出播放器边界。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(
            preview.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > 8L * 1024 * 1024)
        {
            throw new InvalidDataException("动态预览为空或超过 8 MiB 安全限制。");
        }

        var decoder = new GifBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != preview.ExpectedFrameCount)
        {
            throw new InvalidDataException(
                $"GIF 解码帧数与 catalog 不一致：{decoder.Frames.Count}/{preview.ExpectedFrameCount}。");
        }

        var retainedFrameCount = reducedMotion ? 1 : decoder.Frames.Count;
        var (targetWidth, targetHeight) = CalculateTargetSize(
            preview.SourceWidth,
            preview.SourceHeight,
            retainedFrameCount,
            requestedPixelWidth,
            requestedPixelHeight);
        var frames = new List<BitmapSource>(retainedFrameCount);
        var durations = new List<TimeSpan>(retainedFrameCount);
        var indexes = reducedMotion
            ? [preview.RepresentativeFrame]
            : Enumerable.Range(0, decoder.Frames.Count);

        foreach (var index in indexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = decoder.Frames[index];
            if (frame.PixelWidth != preview.SourceWidth || frame.PixelHeight != preview.SourceHeight)
            {
                throw new InvalidDataException(
                    $"GIF 第 {index + 1} 帧尺寸与逻辑画布不一致。");
            }

            var source = ResizeAndFreeze(frame, targetWidth, targetHeight);
            frames.Add(source);
            durations.Add(reducedMotion
                ? TimeSpan.Zero
                : ReadFrameDuration(frame));
        }

        var decodedBytes = checked((long)targetWidth * targetHeight * frames.Count * BytesPerDecodedPixel);
        if (decodedBytes > MaximumRetainedDecodedBytes)
        {
            throw new InvalidDataException("动态预览解码结果超过 24 MiB 内存预算。");
        }

        return new PetDecodedAnimation(
            frames,
            durations,
            targetWidth,
            targetHeight,
            decodedBytes,
            reducedMotion);
    }

    private static (int Width, int Height) CalculateTargetSize(
        int sourceWidth,
        int sourceHeight,
        int retainedFrameCount,
        int requestedPixelWidth,
        int requestedPixelHeight)
    {
        requestedPixelWidth = Math.Clamp(requestedPixelWidth, 1, MaximumDecodeDimension);
        requestedPixelHeight = Math.Clamp(requestedPixelHeight, 1, MaximumDecodeDimension);
        var displayScale = Math.Min(
            1d,
            Math.Min(
                (double)requestedPixelWidth / sourceWidth,
                (double)requestedPixelHeight / sourceHeight));
        var budgetPixels = MaximumRetainedDecodedBytes /
            (double)(BytesPerDecodedPixel * retainedFrameCount);
        var budgetScale = Math.Sqrt(budgetPixels / ((double)sourceWidth * sourceHeight));
        var scale = Math.Min(displayScale, budgetScale);
        return (
            Math.Max(1, (int)Math.Floor(sourceWidth * scale)),
            Math.Max(1, (int)Math.Floor(sourceHeight * scale)));
    }

    private static BitmapSource ResizeAndFreeze(BitmapSource frame, int width, int height)
    {
        BitmapSource result;
        if (frame.PixelWidth == width && frame.PixelHeight == height)
        {
            result = frame;
        }
        else
        {
            var transform = new ScaleTransform(
                (double)width / frame.PixelWidth,
                (double)height / frame.PixelHeight);
            transform.Freeze();
            result = new TransformedBitmap(frame, transform);
        }
        result.Freeze();
        return result;
    }

    private static TimeSpan ReadFrameDuration(BitmapFrame frame)
    {
        var delayMilliseconds = DefaultDelayMilliseconds;
        if (frame.Metadata is BitmapMetadata metadata &&
            metadata.GetQuery("/grctlext/Delay") is { } value)
        {
            delayMilliseconds = checked(Convert.ToInt32(value, CultureInfo.InvariantCulture) * 10);
        }
        if (delayMilliseconds is < 20 or > 2000)
        {
            throw new InvalidDataException("GIF 帧延时超出 20–2000 ms 播放边界。");
        }
        return TimeSpan.FromMilliseconds(delayMilliseconds);
    }
}
