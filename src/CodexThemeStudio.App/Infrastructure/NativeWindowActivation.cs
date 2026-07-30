using System.Runtime.InteropServices;

namespace CodexThemeStudio.App.Infrastructure;

internal static partial class NativeWindowActivation
{
    private const int RestoreWindow = 9;

    public static void TryActivate(string title)
    {
        var handle = FindWindow(null, title);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = ShowWindow(handle, RestoreWindow);
        _ = SetForegroundWindow(handle);
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? className, string windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr windowHandle, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr windowHandle);
}
