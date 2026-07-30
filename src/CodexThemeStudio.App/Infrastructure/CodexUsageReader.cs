using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexThemeStudio.App.Infrastructure;

internal sealed record CodexUsageWindow(
    string Label,
    double RemainingPercent,
    DateTimeOffset? ResetsAt,
    int WindowDurationMinutes);

internal sealed record CodexUsageSnapshot(IReadOnlyList<CodexUsageWindow> Windows)
{
    public CodexUsageWindow? MostConstrained =>
        Windows.OrderBy(window => window.RemainingPercent).FirstOrDefault();
}

internal sealed class CodexUsageReader
{
    private string? _codexPath;

    public async Task<CodexUsageSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    {
        _codexPath ??= FindCodexExecutable();
        if (_codexPath is null) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _codexPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add("app-server");

        var started = false;
        try
        {
            if (!process.Start()) return null;
            started = true;
            await SendAsync(process, new
            {
                method = "initialize",
                id = 1,
                @params = new
                {
                    clientInfo = new
                    {
                        name = BrandInfo.ProtocolClientName,
                        title = BrandInfo.ProductName,
                        version = "1.0.0",
                    },
                },
            }, timeout.Token);
            using var initializeResponse = await ReadResponseAsync(process, 1, timeout.Token);
            if (initializeResponse is null) return null;

            await SendAsync(process, new { method = "initialized", @params = new { } }, timeout.Token);
            await SendAsync(process, new { method = "account/rateLimits/read", id = 2 }, timeout.Token);
            using var usageResponse = await ReadResponseAsync(process, 2, timeout.Token);
            return usageResponse is null ? null : ParseSnapshot(usageResponse.RootElement);
        }
        catch (Exception exception) when (exception is IOException or JsonException or OperationCanceledException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task SendAsync(Process process, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<JsonDocument?> ReadResponseAsync(
        Process process,
        int responseId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null) return null;

            var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.Number &&
                id.GetInt32() == responseId)
            {
                return document;
            }

            document.Dispose();
        }
    }

    private static CodexUsageSnapshot? ParseSnapshot(JsonElement response)
    {
        if (!response.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("rateLimits", out var rateLimits))
        {
            return null;
        }

        var windows = new List<CodexUsageWindow>(2);
        AddWindow(rateLimits, "primary", windows);
        AddWindow(rateLimits, "secondary", windows);
        return windows.Count == 0 ? null : new CodexUsageSnapshot(windows);
    }

    private static void AddWindow(JsonElement rateLimits, string propertyName, List<CodexUsageWindow> windows)
    {
        if (!rateLimits.TryGetProperty(propertyName, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var usedPercentElement) ||
            !usedPercentElement.TryGetDouble(out var usedPercent))
        {
            return;
        }

        var durationMinutes = window.TryGetProperty("windowDurationMins", out var durationElement) &&
                              durationElement.TryGetInt32(out var duration)
            ? duration
            : 0;
        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resetsAt", out var resetElement) &&
            resetElement.TryGetInt64(out var resetUnixSeconds))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetUnixSeconds).ToLocalTime();
        }

        windows.Add(new CodexUsageWindow(
            FormatWindowLabel(durationMinutes),
            Math.Clamp(100d - usedPercent, 0d, 100d),
            resetsAt,
            durationMinutes));
    }

    private static string FormatWindowLabel(int durationMinutes) => durationMinutes switch
    {
        300 => "5 小时额度",
        1440 => "每日额度",
        10080 => "每周额度",
        _ when durationMinutes > 0 && durationMinutes % 1440 == 0 => $"{durationMinutes / 1440} 天额度",
        _ when durationMinutes > 0 && durationMinutes % 60 == 0 => $"{durationMinutes / 60} 小时额度",
        _ => "Codex 额度",
    };

    private static string? FindCodexExecutable()
    {
        var binRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "Codex",
            "bin");
        if (!Directory.Exists(binRoot)) return null;

        return Directory.EnumerateFiles(binRoot, "codex.exe", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }
}
