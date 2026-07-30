using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexThemeStudio.App.Infrastructure;

internal static class NativeTitleBar
{
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var darkMode = dark ? 1 : 0;
        var background = ToColorRef((Color)ColorConverter.ConvertFromString(dark ? "#101116" : "#F7F8FA"));
        var foreground = ToColorRef((Color)ColorConverter.ConvertFromString(dark ? "#F3F4F7" : "#191B20"));
        var border = ToColorRef((Color)ColorConverter.ConvertFromString(dark ? "#30333A" : "#DFE2E7"));
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref darkMode, sizeof(int));
        _ = DwmSetWindowAttribute(handle, CaptionColor, ref background, sizeof(uint));
        _ = DwmSetWindowAttribute(handle, TextColor, ref foreground, sizeof(uint));
        _ = DwmSetWindowAttribute(handle, BorderColor, ref border, sizeof(uint));
    }

    private static uint ToColorRef(Color color) =>
        (uint)(color.R | color.G << 8 | color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref uint value,
        int valueSize);
}
