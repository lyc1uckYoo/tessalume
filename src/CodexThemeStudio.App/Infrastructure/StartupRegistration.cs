using System.IO;
using Microsoft.Win32;

namespace CodexThemeStudio.App.Infrastructure;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string ValueName = BrandInfo.ProductName;
    private const string LegacyValueName = "CodexThemeStudio";
    private const string StartupArgument = "--startup";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var command = key?.GetValue(ValueName) as string;
        return IsCurrentExecutableCommand(command);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的开机启动设置。");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException($"无法确定 {BrandInfo.ProductName} 的可执行文件路径。");
        }

        key.SetValue(ValueName, $"\"{Path.GetFullPath(executablePath)}\" {StartupArgument}", RegistryValueKind.String);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    private static bool IsCurrentExecutableCommand(string? command)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var registeredPath = command.TrimStart().StartsWith('"')
            ? command.Split('"', StringSplitOptions.None).ElementAtOrDefault(1)
            : command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(registeredPath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(registeredPath),
                Path.GetFullPath(executablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
