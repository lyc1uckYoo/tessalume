using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

/// <summary>
/// Produces a frozen BGRA preview using the same filter order as the runtime:
/// brightness, contrast, saturation, grayscale, then hue rotation. Blur remains
/// a presentation-layer WPF effect so interactive changes do not re-rasterize.
/// </summary>
internal static class ArtworkPreviewPixelEffectProcessor
{
    public static Task<BitmapSource> ProcessAsync(
        BitmapSource source,
        ThemeArtworkAdjustment adjustment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adjustment);
        cancellationToken.ThrowIfCancellationRequested();

        var frozenSource = FreezeForBackgroundUse(source);
        var settings = EffectSettings.From(adjustment.Normalize());
        return Task.Run(
            () => ProcessCore(frozenSource, settings, cancellationToken),
            cancellationToken);
    }

    private static BitmapSource FreezeForBackgroundUse(BitmapSource source)
    {
        if (source.IsFrozen)
        {
            return source;
        }

        var clone = (BitmapSource)source.CloneCurrentValue();
        if (!clone.CanFreeze)
        {
            throw new InvalidOperationException(
                "The preview bitmap must be fully loaded before pixel effects are applied.");
        }
        clone.Freeze();
        return clone;
    }

    private static BitmapSource ProcessCore(
        BitmapSource source,
        EffectSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BitmapSource bgraSource;
        if (source.Format == PixelFormats.Bgra32)
        {
            bgraSource = source;
        }
        else
        {
            var converted = new FormatConvertedBitmap(
                source,
                PixelFormats.Bgra32,
                destinationPalette: null,
                alphaThreshold: 0d);
            converted.Freeze();
            bgraSource = converted;
        }

        var width = bgraSource.PixelWidth;
        var height = bgraSource.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The preview bitmap has no pixels to process.");
        }

        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        bgraSource.CopyPixels(pixels, stride, 0);
        cancellationToken.ThrowIfCancellationRequested();

        for (var y = 0; y < height; y++)
        {
            if ((y & 7) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var offset = row + (x * 4);
                var blue = pixels[offset] / 255d;
                var green = pixels[offset + 1] / 255d;
                var red = pixels[offset + 2] / 255d;

                ApplyBrightnessAndContrast(ref red, ref green, ref blue, settings);
                ApplySaturation(ref red, ref green, ref blue, settings.Saturation);
                ApplyGrayscale(ref red, ref green, ref blue, settings.Grayscale);
                ApplyHueRotation(ref red, ref green, ref blue, settings.Hue);

                pixels[offset] = ToByte(blue);
                pixels[offset + 1] = ToByte(green);
                pixels[offset + 2] = ToByte(red);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var dpiX = bgraSource.DpiX > 0d ? bgraSource.DpiX : 96d;
        var dpiY = bgraSource.DpiY > 0d ? bgraSource.DpiY : 96d;
        var result = BitmapSource.Create(
            width,
            height,
            dpiX,
            dpiY,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    private static void ApplyBrightnessAndContrast(
        ref double red,
        ref double green,
        ref double blue,
        EffectSettings settings)
    {
        var contrastOffset = 0.5d * (1d - settings.Contrast);
        red = (red * settings.Brightness * settings.Contrast) + contrastOffset;
        green = (green * settings.Brightness * settings.Contrast) + contrastOffset;
        blue = (blue * settings.Brightness * settings.Contrast) + contrastOffset;
    }

    private static void ApplySaturation(
        ref double red,
        ref double green,
        ref double blue,
        double saturation)
    {
        var sourceRed = red;
        var sourceGreen = green;
        var sourceBlue = blue;
        red = ((0.213d + (0.787d * saturation)) * sourceRed) +
              ((0.715d - (0.715d * saturation)) * sourceGreen) +
              ((0.072d - (0.072d * saturation)) * sourceBlue);
        green = ((0.213d - (0.213d * saturation)) * sourceRed) +
                ((0.715d + (0.285d * saturation)) * sourceGreen) +
                ((0.072d - (0.072d * saturation)) * sourceBlue);
        blue = ((0.213d - (0.213d * saturation)) * sourceRed) +
               ((0.715d - (0.715d * saturation)) * sourceGreen) +
               ((0.072d + (0.928d * saturation)) * sourceBlue);
    }

    private static void ApplyGrayscale(
        ref double red,
        ref double green,
        ref double blue,
        double amount)
    {
        if (amount <= 0d)
        {
            return;
        }

        var luminance = (0.2126d * red) + (0.7152d * green) + (0.0722d * blue);
        var retained = 1d - amount;
        red = (red * retained) + (luminance * amount);
        green = (green * retained) + (luminance * amount);
        blue = (blue * retained) + (luminance * amount);
    }

    private static void ApplyHueRotation(
        ref double red,
        ref double green,
        ref double blue,
        HueMatrix hue)
    {
        var sourceRed = red;
        var sourceGreen = green;
        var sourceBlue = blue;
        red = (hue.M11 * sourceRed) + (hue.M12 * sourceGreen) + (hue.M13 * sourceBlue);
        green = (hue.M21 * sourceRed) + (hue.M22 * sourceGreen) + (hue.M23 * sourceBlue);
        blue = (hue.M31 * sourceRed) + (hue.M32 * sourceGreen) + (hue.M33 * sourceBlue);
    }

    private static byte ToByte(double value)
    {
        var scaled = Math.Round(Math.Clamp(value, 0d, 1d) * 255d);
        return (byte)scaled;
    }

    private readonly record struct EffectSettings(
        double Brightness,
        double Contrast,
        double Saturation,
        double Grayscale,
        HueMatrix Hue)
    {
        public static EffectSettings From(ThemeArtworkAdjustment adjustment) => new(
            adjustment.Brightness / 100d,
            adjustment.Contrast / 100d,
            adjustment.Saturation / 100d,
            adjustment.Grayscale / 100d,
            HueMatrix.FromDegrees(adjustment.HueRotation));
    }

    private readonly record struct HueMatrix(
        double M11,
        double M12,
        double M13,
        double M21,
        double M22,
        double M23,
        double M31,
        double M32,
        double M33)
    {
        public static HueMatrix FromDegrees(double degrees)
        {
            var radians = degrees * Math.PI / 180d;
            var cosine = Math.Cos(radians);
            var sine = Math.Sin(radians);
            return new HueMatrix(
                0.213d + (cosine * 0.787d) - (sine * 0.213d),
                0.715d - (cosine * 0.715d) - (sine * 0.715d),
                0.072d - (cosine * 0.072d) + (sine * 0.928d),
                0.213d - (cosine * 0.213d) + (sine * 0.143d),
                0.715d + (cosine * 0.285d) + (sine * 0.140d),
                0.072d - (cosine * 0.072d) - (sine * 0.283d),
                0.213d - (cosine * 0.213d) - (sine * 0.787d),
                0.715d - (cosine * 0.715d) + (sine * 0.715d),
                0.072d + (cosine * 0.928d) + (sine * 0.072d));
        }
    }
}
