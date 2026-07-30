using System.Threading;
using System.Windows;
using CodexThemeStudio.App.Infrastructure;

namespace CodexThemeStudio.App;

public partial class App : Application, IDisposable
{
    private const string SingleInstanceName = "Local\\CodexThemeStudio.Singleton.v1";
    private const string ActivationEventName = "Local\\CodexThemeStudio.Activate.v1";
    private Mutex? _singleInstance;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationCancellation;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        BuiltInAssetInstaller.EnsureInstalled(PortableLayout.Create());
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        StartActivationListener();
        try
        {
            await mainWindow.StartInQuickModeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, $"{BrandInfo.ProductName} 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            NativeWindowActivation.TryActivate(BrandInfo.WindowTitle);
        }
    }

    private void StartActivationListener()
    {
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _activationCancellation = new CancellationTokenSource();
        var cancellationToken = _activationCancellation.Token;
        _ = Task.Run(() =>
        {
            var handles = new WaitHandle[] { _activationEvent, cancellationToken.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.ShowMainInterface();
                    }
                });
            }
        }, cancellationToken);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        _activationCancellation?.Cancel();
        _activationEvent?.Set();
        _activationCancellation?.Dispose();
        _activationCancellation = null;
        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_singleInstance is null)
        {
            return;
        }

        _singleInstance.ReleaseMutex();
        _singleInstance.Dispose();
        _singleInstance = null;
        GC.SuppressFinalize(this);
    }
}
