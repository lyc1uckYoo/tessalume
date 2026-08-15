namespace Tessalume.Core.Runtime;

internal sealed record ImageDataUrlPayload(string Key, string DataUrl);

/// <summary>
/// Keeps encoded local artwork out of the hot visual-settings path. Entries are
/// identified by absolute path, file length, and UTC modification time, and are
/// evicted least-recently-used under a conservative UTF-16 memory estimate.
/// </summary>
internal sealed class ImageDataUrlCache : IDisposable
{
    internal const long DefaultMemoryBudgetBytes = 64L * 1024 * 1024;

    private const int EntryOverheadBytes = 256;
    private const int MaximumStabilityAttempts = 3;

    private readonly object _sync = new();
    private readonly long _memoryBudgetBytes;
    private readonly Func<string, CancellationToken, Task<byte[]>> _readAllBytesAsync;
    private readonly Dictionary<CacheKey, CacheEntry> _entries = new(CacheKeyComparer.Instance);
    private readonly Dictionary<string, CacheKey> _currentKeyByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheKey> _lru = new();
    private long _cachedBytes;
    private bool _disposed;

    internal ImageDataUrlCache(
        long memoryBudgetBytes = DefaultMemoryBudgetBytes,
        Func<string, CancellationToken, Task<byte[]>>? readAllBytesAsync = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memoryBudgetBytes);

        _memoryBudgetBytes = memoryBudgetBytes;
        _readAllBytesAsync = readAllBytesAsync ?? File.ReadAllBytesAsync;
    }

    internal long MemoryBudgetBytes => _memoryBudgetBytes;

    internal int CachedEntryCount
    {
        get
        {
            lock (_sync)
            {
                return _lru.Count;
            }
        }
    }

    internal int TrackedEntryCount
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    internal long CachedBytes
    {
        get
        {
            lock (_sync)
            {
                return _cachedBytes;
            }
        }
    }

    internal static string GetFingerprint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return CreateFingerprint(ReadKey(Path.GetFullPath(path)));
    }

    internal async Task<string> GetAsync(string path, CancellationToken cancellationToken = default) =>
        (await GetPayloadAsync(path, cancellationToken)).DataUrl;

    internal async Task<ImageDataUrlPayload> GetPayloadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var absolutePath = Path.GetFullPath(path);

        for (var attempt = 0; attempt < MaximumStabilityAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = ReadKey(absolutePath);
            CacheEntry entry;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_entries.TryGetValue(key, out entry!))
                {
                    Touch(entry);
                }
                else
                {
                    RemovePriorVersion(absolutePath, key);
                    entry = new CacheEntry(
                        key,
                        new Lazy<Task<ImageDataUrlPayload>>(
                            () => ReadStablePayloadAsync(key),
                            LazyThreadSafetyMode.ExecutionAndPublication));
                    _entries.Add(key, entry);
                    _currentKeyByPath[absolutePath] = key;
                }
            }

            try
            {
                // The shared read is deliberately independent of an individual caller.
                // Cancellation stops that caller's wait without poisoning other callers.
                return await ObserveCompletion(entry).WaitAsync(cancellationToken);
            }
            catch (FileChangedWhileReadingException) when (attempt + 1 < MaximumStabilityAttempts)
            {
                RemoveFailedEntry(entry);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                RemoveFailedEntry(entry);
                throw;
            }
        }

        throw new IOException($"本地图片在读取期间持续变化，无法生成稳定预览：{absolutePath}");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _entries.Clear();
            _currentKeyByPath.Clear();
            _lru.Clear();
            _cachedBytes = 0;
        }
    }

    private async Task<ImageDataUrlPayload> ReadStablePayloadAsync(CacheKey expectedKey)
    {
        var bytes = await _readAllBytesAsync(expectedKey.AbsolutePath, CancellationToken.None);
        var actualKey = ReadKey(expectedKey.AbsolutePath);
        if (!CacheKeyComparer.Instance.Equals(expectedKey, actualKey))
        {
            throw new FileChangedWhileReadingException();
        }

        var dataUrl = ThemePayloadBuilder.CreateDataUrl(expectedKey.AbsolutePath, bytes);
        return new ImageDataUrlPayload(CreateFingerprint(expectedKey), dataUrl);
    }

    private Task<ImageDataUrlPayload> ObserveCompletion(CacheEntry entry)
    {
        var task = entry.Value.Value;
        var attachContinuation = false;
        lock (_sync)
        {
            if (!entry.CompletionObserved)
            {
                entry.CompletionObserved = true;
                attachContinuation = true;
            }
        }

        if (attachContinuation)
        {
            _ = task.ContinueWith(
                completed => CompleteLoad(entry, completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return task;
    }

    private void CompleteLoad(CacheEntry entry, Task<ImageDataUrlPayload> completed)
    {
        if (completed.Status == TaskStatus.RanToCompletion)
        {
            Commit(entry, completed.Result);
            return;
        }

        // Observe shared loader faults even if every waiting caller was cancelled.
        _ = completed.Exception;
        RemoveFailedEntry(entry);
    }

    private void Commit(CacheEntry entry, ImageDataUrlPayload payload)
    {
        lock (_sync)
        {
            if (_disposed || entry.IsCommitted ||
                !_entries.TryGetValue(entry.Key, out var current) ||
                !ReferenceEquals(current, entry))
            {
                return;
            }

            entry.IsCommitted = true;
            entry.EstimatedBytes = EstimateBytes(entry.Key, payload);
            if (entry.EstimatedBytes > _memoryBudgetBytes)
            {
                RemoveEntry(entry);
                return;
            }

            entry.LruNode = _lru.AddFirst(entry.Key);
            _cachedBytes += entry.EstimatedBytes;
            EvictToBudget();
        }
    }

    private void Touch(CacheEntry entry)
    {
        if (entry.LruNode is null) return;
        _lru.Remove(entry.LruNode);
        _lru.AddFirst(entry.LruNode);
    }

    private void EvictToBudget()
    {
        while (_cachedBytes > _memoryBudgetBytes && _lru.Last is { } last)
        {
            if (_entries.TryGetValue(last.Value, out var entry))
            {
                RemoveEntry(entry);
            }
            else
            {
                _lru.RemoveLast();
            }
        }
    }

    private void RemovePriorVersion(string absolutePath, CacheKey nextKey)
    {
        if (!_currentKeyByPath.TryGetValue(absolutePath, out var previousKey) ||
            CacheKeyComparer.Instance.Equals(previousKey, nextKey))
        {
            return;
        }

        if (_entries.TryGetValue(previousKey, out var previousEntry))
        {
            RemoveEntry(previousEntry);
        }
        else
        {
            _currentKeyByPath.Remove(absolutePath);
        }
    }

    private void RemoveFailedEntry(CacheEntry entry)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            {
                RemoveEntry(entry);
            }
        }
    }

    private void RemoveEntry(CacheEntry entry)
    {
        _entries.Remove(entry.Key);
        if (entry.LruNode is not null)
        {
            _lru.Remove(entry.LruNode);
            entry.LruNode = null;
            _cachedBytes -= entry.EstimatedBytes;
        }

        if (_currentKeyByPath.TryGetValue(entry.Key.AbsolutePath, out var currentKey) &&
            CacheKeyComparer.Instance.Equals(currentKey, entry.Key))
        {
            _currentKeyByPath.Remove(entry.Key.AbsolutePath);
        }
    }

    private static CacheKey ReadKey(string absolutePath)
    {
        var info = new FileInfo(absolutePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("找不到本地图像。", absolutePath);
        }

        return new CacheKey(absolutePath, info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private static string CreateFingerprint(CacheKey key)
    {
        var descriptor = string.Join(
            '\n',
            key.AbsolutePath.ToUpperInvariant(),
            key.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            key.LastWriteTimeUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(descriptor));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static long EstimateBytes(CacheKey key, ImageDataUrlPayload payload) =>
        checked((long)(key.AbsolutePath.Length + payload.Key.Length + payload.DataUrl.Length) *
            sizeof(char) + EntryOverheadBytes);

    private readonly record struct CacheKey(string AbsolutePath, long Length, long LastWriteTimeUtcTicks);

    private sealed class CacheEntry(CacheKey key, Lazy<Task<ImageDataUrlPayload>> value)
    {
        internal CacheKey Key { get; } = key;
        internal Lazy<Task<ImageDataUrlPayload>> Value { get; } = value;
        internal LinkedListNode<CacheKey>? LruNode { get; set; }
        internal long EstimatedBytes { get; set; }
        internal bool IsCommitted { get; set; }
        internal bool CompletionObserved { get; set; }
    }

    private sealed class CacheKeyComparer : IEqualityComparer<CacheKey>
    {
        internal static CacheKeyComparer Instance { get; } = new();

        public bool Equals(CacheKey x, CacheKey y) =>
            x.Length == y.Length &&
            x.LastWriteTimeUtcTicks == y.LastWriteTimeUtcTicks &&
            StringComparer.OrdinalIgnoreCase.Equals(x.AbsolutePath, y.AbsolutePath);

        public int GetHashCode(CacheKey obj) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.AbsolutePath),
            obj.Length,
            obj.LastWriteTimeUtcTicks);
    }

    private sealed class FileChangedWhileReadingException : IOException;
}
