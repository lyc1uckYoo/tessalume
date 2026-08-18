using System.IO;

namespace Tessalume.App.Features.Pets;

internal sealed class PetProjectWatcher : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(650);
    private readonly object _gate = new();
    private readonly string _root;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private bool _disposed;

    public PetProjectWatcher(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
    }

    public event EventHandler? Changed;

    public void SetActive(bool active)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!active)
            {
                StopCore();
                return;
            }
            if (_watcher is not null || !Directory.Exists(_root))
            {
                return;
            }
            if ((File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += Watcher_Changed;
            _watcher.Created += Watcher_Changed;
            _watcher.Deleted += Watcher_Changed;
            _watcher.Renamed += Watcher_Changed;
            _watcher.Error += Watcher_Error;
        }
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e)
    {
        var extension = Path.GetExtension(e.FullPath);
        var fileName = Path.GetFileName(e.FullPath);
        if (!string.IsNullOrEmpty(extension) &&
            !extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("VERSION", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ScheduleChange();
    }

    private void Watcher_Error(object sender, ErrorEventArgs e) => ScheduleChange();

    private void ScheduleChange()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is null)
            {
                return;
            }
            _debounce ??= new Timer(DebounceElapsed);
            _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void DebounceElapsed(object? state)
    {
        lock (_gate)
        {
            if (_disposed || _watcher is null)
            {
                return;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void StopCore()
    {
        _debounce?.Dispose();
        _debounce = null;
        if (_watcher is null)
        {
            return;
        }
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= Watcher_Changed;
        _watcher.Created -= Watcher_Changed;
        _watcher.Deleted -= Watcher_Changed;
        _watcher.Renamed -= Watcher_Changed;
        _watcher.Error -= Watcher_Error;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            StopCore();
        }
        GC.SuppressFinalize(this);
    }
}
