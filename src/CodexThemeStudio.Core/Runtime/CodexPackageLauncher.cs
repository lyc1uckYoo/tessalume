using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace CodexThemeStudio.Core.Runtime;

[SupportedOSPlatform("windows")]
public sealed class CodexPackageLauncher(LoopbackCdpDiscovery discovery)
{
    public static bool IsCodexRunning() => Process.GetProcessesByName("ChatGPT").Length > 0;

    public static int FindFreePort()
    {
        for (var port = 9340; port <= 9399; port++)
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

        throw new InvalidOperationException("本机 9340-9399 端口范围内没有可用端口。");
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

    public async Task<int?> FindRunningDebugPortAsync(CancellationToken cancellationToken = default)
    {
        var probes = Enumerable.Range(9340, 60).Select(async port =>
            (Port: port, Ready: await IsDebugPortReadyAsync(port, cancellationToken)));
        var results = await Task.WhenAll(probes);
        return results.Where(result => result.Ready).Select(result => (int?)result.Port).FirstOrDefault();
    }

    private static async Task<string> FindAppUserModelIdAsync(CancellationToken cancellationToken)
    {
        const string command = "$p=Get-AppxPackage OpenAI.Codex|Sort-Object Version -Descending|Select-Object -First 1;" +
            "if(-not $p){throw 'OpenAI Codex Store package is not installed.'};" +
            "$m=Get-AppxPackageManifest -Package $p.PackageFullName;" +
            "$a=@($m.Package.Applications.Application)|Where-Object{$_.Executable -match 'ChatGPT\\.exe$'}|Select-Object -First 1;" +
            "if(-not $a){throw 'Codex application entry was not found.'};" +
            "[pscustomobject]@{aumid=\"$($p.PackageFamilyName)!$($a.Id)\"}|ConvertTo-Json -Compress";

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
        return document.RootElement.GetProperty("aumid").GetString()
            ?? throw new InvalidOperationException("Codex 应用标识为空。");
    }

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
