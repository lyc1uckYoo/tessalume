using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexThemeStudio.App.Infrastructure;

internal static class LegacyInjectorMigrator
{
    public static async Task<bool> TryStopAsync(CancellationToken cancellationToken = default)
    {
        var statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexDreamSkin",
            "state.json");
        if (!File.Exists(statePath))
        {
            return false;
        }

        try
        {
            await using var stream = File.OpenRead(statePath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("injectorPid", out var pidElement) ||
                !root.TryGetProperty("injectorPath", out var pathElement) ||
                !pidElement.TryGetInt32(out var processId) ||
                pathElement.GetString() is not { Length: > 0 } injectorPath)
            {
                return false;
            }

            using var process = Process.GetProcessById(processId);
            if (!string.Equals(process.ProcessName, "node", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var commandLine = await ReadCommandLineAsync(processId, cancellationToken);
            if (commandLine?.Contains(injectorPath, StringComparison.OrdinalIgnoreCase) != true)
            {
                return false;
            }

            process.Kill();
            await process.WaitForExitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<string?> ReadCommandLineAsync(int processId, CancellationToken cancellationToken)
    {
        var command = $"(Get-CimInstance Win32_Process -Filter 'ProcessId = {processId}').CommandLine";
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 ? output.Trim() : null;
    }
}
