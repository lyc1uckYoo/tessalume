using System.IO;

namespace Tessalume.App.Creator;

internal sealed record ThemeProjectChangeBatch(
    string ProjectDirectory,
    IReadOnlyList<string> ChangedPaths,
    bool ProjectExists,
    DateTimeOffset DetectedAt);

internal sealed class ThemeProjectWatcher : IThemeProjectWatcher
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".legacy", ".references", ".sources", "bin", "obj", "node_modules",
    };

    private readonly string _projectDirectory;
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _stabilityInterval;
    private readonly TimeSpan _stabilityTimeout;
    private readonly object _gate = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _watchedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private FileSystemWatcher? _watcher;
    private Timer? _existenceTimer;
    private CancellationTokenSource? _debounceCancellation;
    private bool _lastKnownExists;
    private bool _disposed;

    public ThemeProjectWatcher(
        string projectDirectory,
        IEnumerable<string>? watchedFiles = null,
        TimeSpan? debounceDelay = null,
        TimeSpan? stabilityInterval = null,
        TimeSpan? stabilityTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        _projectDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory));
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(480);
        _stabilityInterval = stabilityInterval ?? TimeSpan.FromMilliseconds(140);
        _stabilityTimeout = stabilityTimeout ?? TimeSpan.FromSeconds(6);
        if (_debounceDelay < TimeSpan.Zero || _stabilityInterval <= TimeSpan.Zero || _stabilityTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceDelay), "Watcher timing values must be positive.");
        }
        UpdateWatchedFiles(watchedFiles ?? []);
    }

    public event EventHandler<ThemeProjectChangeBatch>? Changed;

    public event EventHandler<string>? Faulted;

    public string ProjectDirectory => _projectDirectory;

    public bool IsRunning => _watcher?.EnableRaisingEvents == true;

    public void UpdateWatchedFiles(IEnumerable<string> watchedFiles)
    {
        ArgumentNullException.ThrowIfNull(watchedFiles);
        lock (_gate)
        {
            _watchedFiles.Clear();
            _watchedFiles.Add(Path.Combine(_projectDirectory, "manifest.json"));
            foreach (var path in watchedFiles)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var fullPath = Path.GetFullPath(path);
                var relativePath = Path.GetRelativePath(_projectDirectory, fullPath);
                if (!relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath))
                {
                    _watchedFiles.Add(fullPath);
                }
            }
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_watcher is not null) return;
        if (!Directory.Exists(_projectDirectory))
        {
            throw new DirectoryNotFoundException($"主题项目目录不存在：{_projectDirectory}");
        }

        _lastKnownExists = true;
        _watcher = new FileSystemWatcher(_projectDirectory)
        {
            IncludeSubdirectories = true,
            Filter = "*",
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.Size |
                           NotifyFilters.CreationTime,
            InternalBufferSize = 32 * 1024,
        };
        _watcher.Changed += Watcher_Changed;
        _watcher.Created += Watcher_Changed;
        _watcher.Deleted += Watcher_Changed;
        _watcher.Renamed += Watcher_Renamed;
        _watcher.Error += Watcher_Error;
        _watcher.EnableRaisingEvents = true;
        _existenceTimer = new Timer(
            CheckProjectExistence,
            null,
            TimeSpan.FromMilliseconds(350),
            TimeSpan.FromMilliseconds(350));
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e) => QueuePath(e.FullPath);

    private void Watcher_Renamed(object sender, RenamedEventArgs e)
    {
        QueuePath(e.OldFullPath);
        QueuePath(e.FullPath);
    }

    private void Watcher_Error(object sender, ErrorEventArgs e)
    {
        if (_disposed) return;
        Faulted?.Invoke(this, $"文件监控需要重新刷新：{e.GetException().Message}");
        QueuePath(_projectDirectory);
    }

    private void CheckProjectExistence(object? state)
    {
        if (_disposed) return;
        var exists = Directory.Exists(_projectDirectory);
        if (exists == _lastKnownExists) return;
        _lastKnownExists = exists;
        QueuePath(_projectDirectory);
    }

    private void QueuePath(string path)
    {
        if (_disposed || !IsRelevantPath(path)) return;
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_disposed) return;
            _pendingPaths.Add(Path.GetFullPath(path));
            _debounceCancellation?.Cancel();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            _debounceCancellation = cancellation;
        }

        _ = ProcessPendingChangesAsync(cancellation);
    }

    private async Task ProcessPendingChangesAsync(CancellationTokenSource debounceCancellation)
    {
        var cancellationToken = debounceCancellation.Token;
        try
        {
            await Task.Delay(_debounceDelay, cancellationToken);
            string[] paths;
            lock (_gate)
            {
                paths = _pendingPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            }

            foreach (var path in paths)
            {
                await WaitForStableFileAsync(path, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                foreach (var path in paths)
                {
                    _pendingPaths.Remove(path);
                }
            }

            Changed?.Invoke(this, new ThemeProjectChangeBatch(
                _projectDirectory,
                paths,
                Directory.Exists(_projectDirectory),
                DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_disposed)
            {
                Faulted?.Invoke(this, $"文件仍在写入，Tessalume 将等待下一次稳定变化：{exception.Message}");
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_debounceCancellation, debounceCancellation))
                {
                    _debounceCancellation = null;
                }
                debounceCancellation.Dispose();
            }
        }
    }

    private async Task WaitForStableFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return;
        var deadline = DateTimeOffset.UtcNow + _stabilityTimeout;
        FileStamp? previous = null;
        var stableSamples = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return;

            var current = ReadFileStamp(path);
            if (current == previous)
            {
                stableSamples++;
                if (stableSamples >= 2) return;
            }
            else
            {
                stableSamples = 0;
                previous = current;
            }
            await Task.Delay(_stabilityInterval, cancellationToken);
        }

        throw new IOException($"文件在 {_stabilityTimeout.TotalSeconds:0.#} 秒内没有稳定：{path}");
    }

    private static FileStamp ReadFileStamp(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var info = new FileInfo(path);
        return new FileStamp(stream.Length, info.LastWriteTimeUtc.Ticks);
    }

    private bool IsRelevantPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (string.Equals(fullPath, _projectDirectory, StringComparison.OrdinalIgnoreCase)) return true;
        var relativePath = Path.GetRelativePath(_projectDirectory, fullPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath)) return false;
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(IgnoredDirectoryNames.Contains)) return false;

        var fileName = Path.GetFileName(relativePath);
        if (fileName.StartsWith('~') || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".temp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".swp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase)) return true;
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".css", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".js", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        lock (_gate)
        {
            return _watchedFiles.Contains(fullPath);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        lock (_gate)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = null;
            _pendingPaths.Clear();
        }
        _existenceTimer?.Dispose();
        _existenceTimer = null;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= Watcher_Changed;
            _watcher.Created -= Watcher_Changed;
            _watcher.Deleted -= Watcher_Changed;
            _watcher.Renamed -= Watcher_Renamed;
            _watcher.Error -= Watcher_Error;
            _watcher.Dispose();
            _watcher = null;
        }
        _lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record FileStamp(long Length, long LastWriteTicks);
}
