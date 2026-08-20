using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Tessalume.App.Infrastructure;

internal sealed record CodexUsageWindow(
    string Label,
    double RemainingPercent,
    DateTimeOffset? ResetsAt,
    int WindowDurationMinutes,
    string? LimitId = null,
    string? LimitName = null);

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

    internal static CodexUsageSnapshot? ParseSnapshot(JsonElement response)
    {
        if (!response.TryGetProperty("result", out var result))
        {
            return null;
        }

        var windows = new List<CodexUsageWindow>(4);
        var seenWindows = new HashSet<string>(StringComparer.Ordinal);
        if (result.TryGetProperty("rateLimits", out var rateLimits) &&
            rateLimits.ValueKind == JsonValueKind.Object)
        {
            AddLimitGroup(rateLimits, null, windows, seenWindows);
        }

        // Newer Codex app-server builds also expose model-specific limits here.
        // Only read the canonical Codex group: a Spark or other model window
        // must never be presented as the account's original five-hour quota.
        if (result.TryGetProperty("rateLimitsByLimitId", out var groupedLimits) &&
            groupedLimits.ValueKind == JsonValueKind.Object)
        {
            foreach (var group in groupedLimits.EnumerateObject())
            {
                if (group.Value.ValueKind == JsonValueKind.Object &&
                    IsCanonicalCodexLimit(group.Name, group.Value))
                {
                    AddLimitGroup(group.Value, group.Name, windows, seenWindows);
                }
            }
        }

        return windows.Count == 0 ? null : new CodexUsageSnapshot(windows);
    }

    private static bool IsCanonicalCodexLimit(string propertyName, JsonElement group)
    {
        if (string.Equals(propertyName, "codex", StringComparison.OrdinalIgnoreCase)) return true;
        return group.TryGetProperty("limitId", out var limitId) &&
               limitId.ValueKind == JsonValueKind.String &&
               string.Equals(limitId.GetString(), "codex", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddLimitGroup(
        JsonElement rateLimits,
        string? fallbackLimitId,
        List<CodexUsageWindow> windows,
        HashSet<string> seenWindows)
    {
        var limitId = rateLimits.TryGetProperty("limitId", out var limitIdElement) &&
                      limitIdElement.ValueKind == JsonValueKind.String
            ? limitIdElement.GetString()
            : fallbackLimitId;
        var limitName = rateLimits.TryGetProperty("limitName", out var limitNameElement) &&
                        limitNameElement.ValueKind == JsonValueKind.String
            ? limitNameElement.GetString()
            : null;
        AddWindow(rateLimits, "primary", limitId, limitName, windows, seenWindows);
        AddWindow(rateLimits, "secondary", limitId, limitName, windows, seenWindows);
    }

    private static void AddWindow(
        JsonElement rateLimits,
        string propertyName,
        string? limitId,
        string? limitName,
        List<CodexUsageWindow> windows,
        HashSet<string> seenWindows)
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
        var windowKey = $"{limitId ?? "<default>"}\u001f{durationMinutes}";
        if (!seenWindows.Add(windowKey)) return;

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resetsAt", out var resetElement) &&
            resetElement.TryGetInt64(out var resetUnixSeconds))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetUnixSeconds).ToLocalTime();
        }

        windows.Add(new CodexUsageWindow(
            FormatWindowLabel(durationMinutes, limitName),
            Math.Clamp(100d - usedPercent, 0d, 100d),
            resetsAt,
            durationMinutes,
            limitId,
            limitName));
    }

    private static string FormatWindowLabel(int durationMinutes, string? limitName)
    {
        var durationLabel = durationMinutes switch
        {
            300 => "5 小时额度",
            1440 => "每日额度",
            10080 => "每周额度",
            _ when durationMinutes > 0 && durationMinutes % 1440 == 0 => $"{durationMinutes / 1440} 天额度",
            _ when durationMinutes > 0 && durationMinutes % 60 == 0 => $"{durationMinutes / 60} 小时额度",
            _ => "Codex 额度",
        };
        return string.IsNullOrWhiteSpace(limitName)
            ? durationLabel
            : $"{limitName} · {durationLabel}";
    }

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
