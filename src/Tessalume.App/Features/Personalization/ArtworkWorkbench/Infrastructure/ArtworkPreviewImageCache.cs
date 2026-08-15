using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal sealed record ArtworkPreviewBitmap(
    BitmapSource Bitmap,
    int SourcePixelWidth,
    int SourcePixelHeight);

/// <summary>
/// Loads preview images away from the UI thread and retains a small, thread-safe
/// least-recently-used cache of fully decoded, frozen bitmap sources.
/// </summary>
internal sealed class ArtworkPreviewImageCache
{
    internal const int DefaultCapacity = 12;
    internal const long DefaultByteBudget = 96L * 1024 * 1024;

    private const int MaximumDecodePixelWidth = 16_384;
    private const long MaximumSourcePixels = 120_000_000;
    private const long MaximumPreviewPixels = 12_000_000;
    private const int MaximumFingerprintRetries = 3;

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly long _byteBudget;
    private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _entries =
        new(CacheKeyComparer.Instance);
    private readonly LinkedList<CacheEntry> _leastRecentlyUsed = [];
    private long _cachedBytes;

    public ArtworkPreviewImageCache(
        int capacity = DefaultCapacity,
        long byteBudget = DefaultByteBudget)
    {
        if (capacity is <= 0 or > DefaultCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                $"Preview cache capacity must be between 1 and {DefaultCapacity} entries.");
        }
        if (byteBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteBudget),
                byteBudget,
                "Preview cache byte budget must be positive.");
        }
        _capacity = capacity;
        _byteBudget = byteBudget;
    }

    internal long CachedBytes
    {
        get
        {
            lock (_gate)
            {
                return _cachedBytes;
            }
        }
    }

    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public async Task<BitmapSource> LoadAsync(
        string path,
        int decodePixelWidth,
        CancellationToken cancellationToken = default) =>
        (await LoadWithMetadataAsync(path, decodePixelWidth, cancellationToken)).Bitmap;

    public async Task<ArtworkPreviewBitmap> LoadWithMetadataAsync(
        string path,
        int decodePixelWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (decodePixelWidth is <= 0 or > MaximumDecodePixelWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decodePixelWidth),
                decodePixelWidth,
                $"Preview decode width must be between 1 and {MaximumDecodePixelWidth} pixels.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var absolutePath = Path.GetFullPath(path);
        for (var attempt = 0; attempt < MaximumFingerprintRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = CreateKey(absolutePath, decodePixelWidth);
            if (TryGet(key, out var cached))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return cached;
            }

            RemoveObsoleteVersions(key);
            var bytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var decoded = await Task.Run(
                    () => Decode(bytes, decodePixelWidth, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            CacheKey currentKey;
            try
            {
                currentKey = CreateKey(absolutePath, decodePixelWidth);
            }
            catch (FileNotFoundException) when (attempt + 1 < MaximumFingerprintRetries)
            {
                continue;
            }

            if (!CacheKeyComparer.Instance.Equals(key, currentKey))
            {
                continue;
            }

            return AddOrGetExisting(key, decoded);
        }

        throw new IOException("The preview image changed repeatedly while it was being loaded.");
    }

    public void Invalidate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var absolutePath = Path.GetFullPath(path);
        lock (_gate)
        {
            var nodes = _entries
                .Where(pair => PathEquals(pair.Key.AbsolutePath, absolutePath))
                .Select(pair => pair.Value)
                .ToArray();
            foreach (var node in nodes)
            {
                RemoveNode(node);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _leastRecentlyUsed.Clear();
            _cachedBytes = 0;
        }
    }

    private static CacheKey CreateKey(string absolutePath, int decodePixelWidth)
    {
        var file = new FileInfo(absolutePath);
        file.Refresh();
        if (!file.Exists)
        {
            throw new FileNotFoundException("The preview image does not exist.", absolutePath);
        }

        return new CacheKey(
            absolutePath,
            file.Length,
            file.LastWriteTimeUtc.Ticks,
            decodePixelWidth);
    }

    private bool TryGet(CacheKey key, out ArtworkPreviewBitmap source)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                source = null!;
                return false;
            }

            _leastRecentlyUsed.Remove(node);
            _leastRecentlyUsed.AddFirst(node);
            source = node.Value.Source;
            return true;
        }
    }

    private ArtworkPreviewBitmap AddOrGetExisting(CacheKey key, ArtworkPreviewBitmap source)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _leastRecentlyUsed.Remove(existing);
                _leastRecentlyUsed.AddFirst(existing);
                return existing.Value.Source;
            }

            RemoveObsoleteVersionsCore(key);
            var estimatedBytes = EstimateDecodedBytes(source);
            if (estimatedBytes > _byteBudget) return source;
            var node = new LinkedListNode<CacheEntry>(
                new CacheEntry(key, source, estimatedBytes));
            _leastRecentlyUsed.AddFirst(node);
            _entries.Add(key, node);
            _cachedBytes += estimatedBytes;
            while ((_entries.Count > _capacity || _cachedBytes > _byteBudget) &&
                   _leastRecentlyUsed.Last is { } oldest)
            {
                RemoveNode(oldest);
            }
            return source;
        }
    }

    private void RemoveObsoleteVersions(CacheKey key)
    {
        lock (_gate)
        {
            RemoveObsoleteVersionsCore(key);
        }
    }

    private void RemoveObsoleteVersionsCore(CacheKey key)
    {
        var obsolete = _entries
            .Where(pair =>
                PathEquals(pair.Key.AbsolutePath, key.AbsolutePath) &&
                (pair.Key.Length != key.Length ||
                 pair.Key.LastWriteTimeUtcTicks != key.LastWriteTimeUtcTicks))
            .Select(pair => pair.Value)
            .ToArray();
        foreach (var node in obsolete)
        {
            RemoveNode(node);
        }
    }

    private void RemoveNode(LinkedListNode<CacheEntry> node)
    {
        _leastRecentlyUsed.Remove(node);
        _entries.Remove(node.Value.Key);
        _cachedBytes = Math.Max(0, _cachedBytes - node.Value.EstimatedBytes);
    }

    private static ArtworkPreviewBitmap Decode(
        byte[] bytes,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceDimensions = ValidateDimensions(bytes, decodePixelWidth);
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();

        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            throw new InvalidDataException("The preview image has no decodable pixels.");
        }

        // OnLoad owns the stream, while CopyPixels forces WIC to materialize image
        // data now instead of surfacing a corrupt frame later during WPF rendering.
        var probeStride = Math.Max(1, (image.Format.BitsPerPixel + 7) / 8);
        var probe = new byte[probeStride];
        image.CopyPixels(new Int32Rect(0, 0, 1, 1), probe, probeStride, 0);
        cancellationToken.ThrowIfCancellationRequested();
        image.Freeze();
        return new ArtworkPreviewBitmap(
            image,
            sourceDimensions.Width,
            sourceDimensions.Height);
    }

    private static (int Width, int Height) ValidateDimensions(
        byte[] bytes,
        int decodePixelWidth)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("The preview image has no decodable frames.");
        }

        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
        {
            throw new InvalidDataException("The preview image has invalid dimensions.");
        }
        var sourcePixels = (long)frame.PixelWidth * frame.PixelHeight;
        if (sourcePixels > MaximumSourcePixels)
        {
            throw new InvalidDataException("The image dimensions are too large for a safe local preview.");
        }

        var previewWidth = decodePixelWidth;
        var previewHeight = (long)Math.Ceiling(
            frame.PixelHeight * (previewWidth / (double)frame.PixelWidth));
        if ((long)previewWidth * previewHeight > MaximumPreviewPixels)
        {
            throw new InvalidDataException("The image aspect ratio would create an unsafe preview bitmap.");
        }
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static long EstimateDecodedBytes(ArtworkPreviewBitmap source) => checked(
        (long)source.Bitmap.PixelWidth * source.Bitmap.PixelHeight *
        Math.Max(4, (source.Bitmap.Format.BitsPerPixel + 7) / 8));

    private readonly record struct CacheKey(
        string AbsolutePath,
        long Length,
        long LastWriteTimeUtcTicks,
        int DecodePixelWidth);

    private sealed record CacheEntry(
        CacheKey Key,
        ArtworkPreviewBitmap Source,
        long EstimatedBytes);

    private sealed class CacheKeyComparer : IEqualityComparer<CacheKey>
    {
        public static CacheKeyComparer Instance { get; } = new();

        public bool Equals(CacheKey x, CacheKey y) =>
            x.Length == y.Length &&
            x.LastWriteTimeUtcTicks == y.LastWriteTimeUtcTicks &&
            x.DecodePixelWidth == y.DecodePixelWidth &&
            PathEquals(x.AbsolutePath, y.AbsolutePath);

        public int GetHashCode(CacheKey key)
        {
            var hash = new HashCode();
            hash.Add(key.AbsolutePath, StringComparer.OrdinalIgnoreCase);
            hash.Add(key.Length);
            hash.Add(key.LastWriteTimeUtcTicks);
            hash.Add(key.DecodePixelWidth);
            return hash.ToHashCode();
        }
    }
}
