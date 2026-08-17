using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Tessalume.Core.Runtime;

[SupportedOSPlatform("windows")]
public sealed class CodexPackageLauncher(LoopbackCdpDiscovery discovery)
{
    public static bool IsCodexRunning() => Process.GetProcessesByName("ChatGPT").Length > 0;

    public static async Task<bool> IsCodexInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await FindAppUserModelIdAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> FindInstalledVersionAsync(
        CancellationToken cancellationToken = default) =>
        (await FindPackageInfoAsync(cancellationToken)).Version;

    /// <summary>
    /// Opens the installed Codex package without adding theme-runtime debugging
    /// arguments. Windows activates the existing app instance when one is running.
    /// </summary>
    public static async Task OpenCodexAsync(CancellationToken cancellationToken = default)
    {
        var appUserModelId = await FindAppUserModelIdAsync(cancellationToken);
        _ = PackagedAppActivation.Launch(appUserModelId, string.Empty);
    }

    public static int FindFreePort()
    {
        for (var port = CodexDebugPortPolicy.ManagedPortStart;
             port <= CodexDebugPortPolicy.ManagedPortEnd;
             port++)
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
            }
            finally
            {
                listener?.Stop();
            }
        }

        throw new InvalidOperationException(
            $"本机 {CodexDebugPortPolicy.ManagedPortStart}-{CodexDebugPortPolicy.ManagedPortEnd} " +
            "端口范围内没有可用端口。");
    }

    public static async Task CloseCodexAsync(CancellationToken cancellationToken = default)
    {
        var processes = Process.GetProcessesByName("ChatGPT");
        foreach (var process in processes.Where(process => process.MainWindowHandle != IntPtr.Zero))
        {
            process.CloseMainWindow();
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline && Process.GetProcessesByName("ChatGPT").Length > 0)
        {
            await Task.Delay(250, cancellationToken);
        }

        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            process.Kill(entireProcessTree: true);
        }
    }

    public async Task LaunchAndWaitAsync(int port, CancellationToken cancellationToken = default)
    {
        var appUserModelId = await FindAppUserModelIdAsync(cancellationToken);
        var arguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={port}";
        _ = PackagedAppActivation.Launch(appUserModelId, arguments);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await discovery.DiscoverAsync(port, cancellationToken)).Count > 0)
            {
                return;
            }

            await Task.Delay(350, cancellationToken);
        }

        throw new TimeoutException($"Codex 已启动，但 30 秒内没有打开本机调试端口 {port}。");
    }

    public async Task<bool> IsDebugPortReadyAsync(int port, CancellationToken cancellationToken = default) =>
        (await discovery.DiscoverAsync(port, cancellationToken)).Count > 0;

    public async Task<int?> FindRunningDebugPortAsync(
        IEnumerable<int?>? preferredPorts = null,
        CancellationToken cancellationToken = default)
    {
        var checkedPorts = new HashSet<int>();
        foreach (var port in preferredPorts ?? [])
        {
            if (port is not { } value ||
                !CodexDebugPortPolicy.IsValid(value) ||
                !checkedPorts.Add(value))
            {
                continue;
            }

            if (await IsDebugPortReadyAsync(value, cancellationToken))
            {
                return value;
            }
        }

        if (checkedPorts.Add(CodexDebugPortPolicy.CodexPlusPlusPort) &&
            await IsDebugPortReadyAsync(
                CodexDebugPortPolicy.CodexPlusPlusPort,
                cancellationToken))
        {
            return CodexDebugPortPolicy.CodexPlusPlusPort;
        }

        var managedPorts = Enumerable
            .Range(
                CodexDebugPortPolicy.ManagedPortStart,
                CodexDebugPortPolicy.ManagedPortEnd - CodexDebugPortPolicy.ManagedPortStart + 1)
            .Where(checkedPorts.Add)
            .ToArray();
        return await FindFirstReadyPortAsync(managedPorts, cancellationToken);
    }

    public Task<int?> FindRunningDebugPortAsync(CancellationToken cancellationToken) =>
        FindRunningDebugPortAsync(null, cancellationToken);

    private async Task<int?> FindFirstReadyPortAsync(
        IReadOnlyCollection<int> ports,
        CancellationToken cancellationToken)
    {
        if (ports.Count == 0) return null;

        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var probes = ports
            .Select(port => ProbePortAsync(port, probeCancellation.Token))
            .ToList();
        while (probes.Count > 0)
        {
            var completed = await Task.WhenAny(probes);
            probes.Remove(completed);
            var result = await completed;
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Ready) continue;

            probeCancellation.Cancel();
            await ObserveCancelledProbesAsync(probes, cancellationToken);
            return result.Port;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private async Task<(int Port, bool Ready)> ProbePortAsync(
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            return (port, await IsDebugPortReadyAsync(port, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (port, false);
        }
    }

    private static async Task ObserveCancelledProbesAsync(
        IReadOnlyCollection<Task<(int Port, bool Ready)>> probes,
        CancellationToken callerCancellation)
    {
        if (probes.Count == 0) return;
        await Task.WhenAll(probes);
        callerCancellation.ThrowIfCancellationRequested();
    }

    private static async Task<string> FindAppUserModelIdAsync(CancellationToken cancellationToken)
        => (await FindPackageInfoAsync(cancellationToken)).AppUserModelId;

    private static async Task<CodexPackageInfo> FindPackageInfoAsync(CancellationToken cancellationToken)
    {
        const string command = "$p=Get-AppxPackage OpenAI.Codex|Sort-Object Version -Descending|Select-Object -First 1;" +
            "if(-not $p){throw 'OpenAI Codex Store package is not installed.'};" +
            "$m=Get-AppxPackageManifest -Package $p.PackageFullName;" +
            "$a=@($m.Package.Applications.Application)|Where-Object{$_.Executable -match 'ChatGPT\\.exe$'}|Select-Object -First 1;" +
            "if(-not $a){throw 'Codex application entry was not found.'};" +
            "[pscustomobject]@{aumid=\"$($p.PackageFamilyName)!$($a.Id)\";version=\"$($p.Version)\"}|ConvertTo-Json -Compress";

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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动本机 PowerShell 查询 Codex。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "未找到 Codex Store 安装。" : stderr.Trim());
        }

        using var document = JsonDocument.Parse(stdout);
        var appUserModelId = document.RootElement.GetProperty("aumid").GetString()
            ?? throw new InvalidOperationException("Codex 应用标识为空。");
        var version = document.RootElement.GetProperty("version").GetString()
            ?? throw new InvalidOperationException("Codex 版本为空。");
        return new CodexPackageInfo(appUserModelId, version);
    }

    private sealed record CodexPackageInfo(string AppUserModelId, string Version);

    [Flags]
    private enum ActivateOptions
    {
        None = 0,
        NoErrorUi = 2,
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ActivateOptions options,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager;

    private static class PackagedAppActivation
    {
        public static uint Launch(string appUserModelId, string arguments)
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            var result = manager.ActivateApplication(appUserModelId, arguments, ActivateOptions.NoErrorUi, out var processId);
            Marshal.ThrowExceptionForHR(result);
            return processId;
        }
    }
}
