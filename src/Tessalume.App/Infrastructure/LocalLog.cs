using System.IO;
using System.Globalization;
using System.Text;

namespace Tessalume.App.Infrastructure;

internal static class LocalLog
{
    private const long MaximumLogBytes = 1024 * 1024;
    private static readonly object Sync = new();
    private static string? _logDirectory;
    private static string? _logPath;

    public static string LogDirectory => _logDirectory ?? string.Empty;

    public static void Initialize(string dataDirectory)
    {
        lock (Sync)
        {
            _logDirectory = Path.Combine(dataDirectory, "logs");
            _logPath = Path.Combine(_logDirectory, "tessalume.log");
            Directory.CreateDirectory(_logDirectory);
            if (File.Exists(_logPath) && new FileInfo(_logPath).Length > MaximumLogBytes)
            {
                var previousPath = Path.Combine(_logDirectory, "tessalume.previous.log");
                File.Move(_logPath, previousPath, overwrite: true);
            }
        }

        Write("Tessalume started.");
    }

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Sync)
            {
                if (string.IsNullOrWhiteSpace(_logPath)) return;
                var line = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                    .Append("  ")
                    .Append(message.ReplaceLineEndings(" "));
                if (exception is not null)
                {
                    line.Append("  |  ")
                        .Append(exception.GetType().Name)
                        .Append(": ")
                        .Append(exception.Message.ReplaceLineEndings(" "));
                    var inner = exception.InnerException;
                    for (var depth = 0; inner is not null && depth < 3; depth++)
                    {
                        line.Append("  <-  ")
                            .Append(inner.GetType().Name)
                            .Append(": ")
                            .Append(inner.Message.ReplaceLineEndings(" "));
                        inner = inner.InnerException;
                    }
                }
                File.AppendAllText(_logPath, line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            // Logging must never block theme management or application shutdown.
        }
    }

    public static IReadOnlyList<string> ReadTail(int maximumLines = 30)
    {
        try
        {
            lock (Sync)
            {
                if (string.IsNullOrWhiteSpace(_logPath) || !File.Exists(_logPath)) return [];
                return File.ReadLines(_logPath).TakeLast(Math.Max(1, maximumLines)).ToArray();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [$"日志读取失败：{exception.Message}"];
        }
    }
}
