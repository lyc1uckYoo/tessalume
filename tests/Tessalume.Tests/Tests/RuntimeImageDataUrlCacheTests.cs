using Tessalume.Core.Runtime;

internal static partial class TestSuite
{
    static async Task RuntimeImageDataUrlCacheIsBoundedAndInvalidatesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-runtime-image-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var imagePath = Path.Combine(root, "shared.png");
            await File.WriteAllBytesAsync(imagePath, Enumerable.Repeat((byte)0x31, 64).ToArray());
            var readCount = 0;
            using var cache = new ImageDataUrlCache(
                readAllBytesAsync: async (path, cancellationToken) =>
                {
                    Interlocked.Increment(ref readCount);
                    await Task.Delay(25, cancellationToken);
                    return await File.ReadAllBytesAsync(path, cancellationToken);
                });

            var concurrent = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => cache.GetAsync(imagePath)));
            var completedHit = await cache.GetAsync(imagePath);
            Ensure(readCount == 1 && concurrent.Distinct(StringComparer.Ordinal).Count() == 1,
                "Concurrent artwork slots and repeated serialization must share one file read and Base64 encoding.");
            Ensure(completedHit == concurrent[0] && readCount == 1,
                "A completed cache hit must reuse the existing data URL without reading the local image again.");
            Ensure(cache.CachedEntryCount == 1 &&
                   cache.CachedBytes > 0 &&
                   cache.CachedBytes <= cache.MemoryBudgetBytes,
                "The runtime image cache must account for cached data under its explicit memory budget.");

            var firstDataUrl = concurrent[0];
            var previousWriteTime = File.GetLastWriteTimeUtc(imagePath);
            await File.WriteAllBytesAsync(imagePath, Enumerable.Repeat((byte)0x72, 64).ToArray());
            File.SetLastWriteTimeUtc(imagePath, previousWriteTime.AddSeconds(2));
            var changedDataUrl = await cache.GetAsync(imagePath);
            Ensure(readCount == 2 && changedDataUrl != firstDataUrl,
                "A local image change with the same length must invalidate the cache through its modification time.");

            var firstLruPath = Path.Combine(root, "first.jpg");
            var secondLruPath = Path.Combine(root, "second.jpg");
            await File.WriteAllBytesAsync(firstLruPath, Enumerable.Repeat((byte)0x18, 128).ToArray());
            await File.WriteAllBytesAsync(secondLruPath, Enumerable.Repeat((byte)0x29, 128).ToArray());
            var lruReads = 0;
            using var bounded = new ImageDataUrlCache(
                memoryBudgetBytes: 1024,
                readAllBytesAsync: async (path, cancellationToken) =>
                {
                    Interlocked.Increment(ref lruReads);
                    return await File.ReadAllBytesAsync(path, cancellationToken);
                });
            _ = await bounded.GetAsync(firstLruPath);
            _ = await bounded.GetAsync(secondLruPath);
            _ = await bounded.GetAsync(firstLruPath);
            Ensure(bounded.CachedBytes <= bounded.MemoryBudgetBytes && lruReads == 3,
                "The least-recently-used image must be evicted before the cache exceeds its memory budget.");

            var oversizedFirstPath = Path.Combine(root, "oversized-first.jpg");
            var oversizedSecondPath = Path.Combine(root, "oversized-second.jpg");
            await File.WriteAllBytesAsync(
                oversizedFirstPath,
                Enumerable.Repeat((byte)0x61, 2048).ToArray());
            await File.WriteAllBytesAsync(
                oversizedSecondPath,
                Enumerable.Repeat((byte)0x62, 2048).ToArray());
            var oversizedReads = 0;
            using var oversized = new ImageDataUrlCache(
                memoryBudgetBytes: 1024,
                readAllBytesAsync: async (path, cancellationToken) =>
                {
                    Interlocked.Increment(ref oversizedReads);
                    return await File.ReadAllBytesAsync(path, cancellationToken);
                });
            _ = await oversized.GetPayloadAsync(oversizedFirstPath);
            _ = await oversized.GetPayloadAsync(oversizedSecondPath);
            Ensure(oversizedReads == 2 && oversized.CachedEntryCount == 0,
                "Oversized Data URLs may be evicted without losing lightweight file fingerprint support.");
            for (var index = 0; index < 20; index++)
            {
                _ = ImageDataUrlCache.GetFingerprint(oversizedFirstPath);
                _ = ImageDataUrlCache.GetFingerprint(oversizedSecondPath);
            }
            Ensure(oversizedReads == 2,
                "Parameter-only fingerprint checks must not reread or re-encode multiple images that exceed the Data URL budget.");

            bounded.Dispose();
            Ensure(bounded.CachedEntryCount == 0 && bounded.CachedBytes == 0,
                "Disposing the runtime must release all cached data URL references.");

            var cancelledPath = Path.Combine(root, "cancelled.png");
            await File.WriteAllBytesAsync(cancelledPath, Enumerable.Repeat((byte)0x4A, 1024).ToArray());
            var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationCache = new ImageDataUrlCache(
                memoryBudgetBytes: 512,
                readAllBytesAsync: async (path, cancellationToken) =>
                {
                    readStarted.TrySetResult();
                    await releaseRead.Task.WaitAsync(cancellationToken);
                    return await File.ReadAllBytesAsync(path, cancellationToken);
                });
            using var cancellation = new CancellationTokenSource();
            var cancelledWait = cancellationCache.GetAsync(cancelledPath, cancellation.Token);
            await readStarted.Task;
            cancellation.Cancel();
            try
            {
                _ = await cancelledWait;
                throw new InvalidOperationException("The sole cache waiter should have observed cancellation.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }

            releaseRead.TrySetResult();
            var cleanupDeadline = DateTime.UtcNow.AddSeconds(2);
            while (cancellationCache.TrackedEntryCount != 0 && DateTime.UtcNow < cleanupDeadline)
            {
                await Task.Delay(10);
            }
            Ensure(cancellationCache.TrackedEntryCount == 0 &&
                   cancellationCache.CachedEntryCount == 0 &&
                   cancellationCache.CachedBytes == 0,
                "A load completed after its sole waiter cancels must still obey the budget and release an oversized data URL.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
