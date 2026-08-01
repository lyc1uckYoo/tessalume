using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Tessalume.App.Infrastructure;

internal static partial class IdleMemoryTrimmer
{
    private static int _scheduled;

    public static void Schedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800);
                GCSettings.LargeObjectHeapCompactionMode =
                    GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(
                    GC.MaxGeneration,
                    GCCollectionMode.Optimized,
                    blocking: true,
                    compacting: true);
                using var process = Process.GetCurrentProcess();
                _ = EmptyWorkingSet(process.Handle);
            }
            finally
            {
                Volatile.Write(ref _scheduled, 0);
            }
        });
    }

    [LibraryImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(IntPtr process);
}
