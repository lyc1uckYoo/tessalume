using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessalume.App.Infrastructure;

internal static partial class TestSuite
{
    static Task<int> RenderShellSurfaceSnapshotsAsync(
        string dialogLightPath,
        string dialogDarkPath,
        string onboardingLightPath,
        string onboardingDarkPath,
        string quickLightPath,
        string quickDarkPath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            dispatcher.BeginInvoke(() =>
            {
                Window? current = null;
                try
                {
                    current = new ProductDialogWindow(
                        "发现 Tessalume v1.4.0",
                        "新版本已经准备好。下载后会校验完整性，并保留现有主题、收藏、图像参数和本地设置。",
                        ProductDialogKind.Confirmation,
                        darkMode: false,
                        confirmText: "下载并安装",
                        cancelText: "稍后再说",
                        dangerous: false);
                    SaveShellSurface(current, dialogLightPath);
                    current.Close();

                    current = new ProductDialogWindow(
                        "当前已经是最新版本",
                        "你正在使用最新版本，暂时没有可安装的更新。",
                        ProductDialogKind.Information,
                        darkMode: true,
                        confirmText: "知道了",
                        cancelText: null,
                        dangerous: false);
                    SaveShellSurface(current, dialogDarkPath);
                    current.Close();

                    current = (Window?)Activator.CreateInstance(
                        typeof(FirstRunWindow),
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        binder: null,
                        args: [false, true],
                        culture: null)
                        ?? throw new InvalidOperationException("Unable to create the onboarding window.");
                    SaveShellSurface(current, onboardingLightPath);
                    current.Close();

                    current = (Window?)Activator.CreateInstance(
                        typeof(FirstRunWindow),
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        binder: null,
                        args: [true, true],
                        culture: null)
                        ?? throw new InvalidOperationException("Unable to create the dark onboarding window.");
                    SaveShellSurface(current, onboardingDarkPath);
                    current.Close();

                    var quick = new ThemeQuickSwitchWindow(
                        _ => Task.FromResult(true),
                        () => Task.FromResult(true),
                        () => Task.FromResult<bool?>(false),
                        () => Task.FromResult<bool?>(false),
                        () => { },
                        () => Task.FromResult<CodexUsageSnapshot?>(null));
                    current = quick;
                    quick.Refresh("snapshot", "爱弥斯 · 星海远航", false, []);
                    quick.SetShellTheme(false);
                    SaveShellSurface(quick, quickLightPath);
                    quick.SetShellTheme(true);
                    SaveShellSurface(quick, quickDarkPath);
                    quick.Close();
                }
                catch (Exception exception)
                {
                    failure = exception is TargetInvocationException invocation
                        ? invocation.InnerException ?? invocation
                        : exception;
                }
                finally
                {
                    current?.Close();
                    application.Shutdown();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            Console.Error.WriteLine(failure);
            return Task.FromResult(1);
        }

        Console.WriteLine($"Dialog light snapshot: {Path.GetFullPath(dialogLightPath)}");
        Console.WriteLine($"Dialog dark snapshot: {Path.GetFullPath(dialogDarkPath)}");
        Console.WriteLine($"Onboarding light snapshot: {Path.GetFullPath(onboardingLightPath)}");
        Console.WriteLine($"Onboarding dark snapshot: {Path.GetFullPath(onboardingDarkPath)}");
        Console.WriteLine($"Quick switch light snapshot: {Path.GetFullPath(quickLightPath)}");
        Console.WriteLine($"Quick switch dark snapshot: {Path.GetFullPath(quickDarkPath)}");
        return Task.FromResult(0);
    }

    private static void SaveShellSurface(Window window, string path)
    {
        var surface = window.Content as FrameworkElement
            ?? throw new InvalidOperationException("Window content is unavailable.");
        var requestedWidth = double.IsNaN(window.Width) ? double.PositiveInfinity : window.Width;
        var requestedHeight = double.IsNaN(window.Height) ? double.PositiveInfinity : window.Height;
        surface.Measure(new Size(requestedWidth, requestedHeight));
        var width = double.IsInfinity(requestedWidth)
            ? Math.Max(1, Math.Ceiling(surface.DesiredSize.Width))
            : Math.Max(1, requestedWidth);
        var height = double.IsInfinity(requestedHeight)
            ? Math.Max(1, Math.Ceiling(surface.DesiredSize.Height))
            : Math.Max(1, requestedHeight);
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width),
            (int)Math.Ceiling(height),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var output = File.Create(fullPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(output);
    }
}
