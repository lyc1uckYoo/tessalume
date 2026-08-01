using System.IO;
using Microsoft.Win32;

namespace Tessalume.App.Infrastructure;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string ValueName = BrandInfo.ProductName;
    private const string LegacyValueName = "CodexThemeStudio";
    private const string StartupArgument = "--startup";

    public static bool TryMigrateLegacyRegistration()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            var currentCommand = key.GetValue(ValueName) as string;
            var legacyCommand = key.GetValue(LegacyValueName) as string;
            if (string.IsNullOrWhiteSpace(currentCommand) && string.IsNullOrWhiteSpace(legacyCommand))
            {
                return true;
            }

            key.SetValue(ValueName, BuildCurrentExecutableCommand(), RegistryValueKind.String);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

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

        key.SetValue(ValueName, BuildCurrentExecutableCommand(), RegistryValueKind.String);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    private static string BuildCurrentExecutableCommand()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException($"无法确定 {BrandInfo.ProductName} 的可执行文件路径。");
        }

        return $"\"{Path.GetFullPath(executablePath)}\" {StartupArgument}";
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
