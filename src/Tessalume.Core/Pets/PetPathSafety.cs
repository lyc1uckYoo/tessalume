using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Tessalume.Core.Pets;

internal static class PetPathSafety
{
    public static string NormalizeDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value)) return false;
        if (value.Contains(':') ||
            value.StartsWith('/') ||
            value.StartsWith('\\'))
        {
            return false;
        }

        var segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 &&
               segments.All(segment => segment is not "." and not ".." && segment.Length > 0);
    }

    public static bool IsSimpleDirectoryName(string? value) =>
        IsSafeRelativePath(value) &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
        !value!.Contains(Path.AltDirectorySeparatorChar) &&
        !value.Contains(Path.DirectorySeparatorChar);

    public static bool IsValidPetId(string? value) =>
        value is { Length: >= 3 and <= 64 } &&
        value.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-');

    public static string ResolveContainedPath(string root, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
        {
            throw new InvalidDataException($"宠物包包含无效或远程资源路径：{relativePath}");
        }

        root = NormalizeDirectory(root);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        EnsureContained(root, candidate);
        return candidate;
    }

    public static void EnsureContained(string root, string candidate)
    {
        root = NormalizeDirectory(root);
        candidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(root, candidate);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("宠物文件路径超出了允许的目录。");
        }
    }

    public static bool IsContainedOrEqual(string root, string candidate)
    {
        root = NormalizeDirectory(root);
        candidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." ||
               (!relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relative));
    }

    public static void EnsureNoReparsePoints(string root, string path)
    {
        root = NormalizeDirectory(root);
        path = Path.GetFullPath(path);
        EnsureContained(root, path);

        CheckExistingPath(root);
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".") return;

        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current)) break;
            CheckExistingPath(current);
        }
    }

    public static void EnsureRegularFile(string root, string path)
    {
        EnsureNoReparsePoints(root, path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("宠物包缺少已声明的文件。", path);
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("宠物文件不能是符号链接或重解析点。");
        }
        if (OperatingSystem.IsWindows() && GetHardLinkCount(path) > 1)
        {
            throw new InvalidDataException("宠物文件不能是指向其他位置的硬链接。");
        }
    }

    public static void EnsureRegularDirectory(string root, string path)
    {
        EnsureNoReparsePoints(root, path);
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("宠物目录无效或是重解析点。");
        }
    }

    private static void CheckExistingPath(string candidate)
    {
        if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"宠物文件操作不能穿过符号链接或重解析点：{candidate}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static uint GetHardLinkCount(string path)
    {
        using var handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileOptions.None);
        if (!GetFileInformationByHandle(handle, out var information))
        {
            throw new IOException(
                $"无法验证宠物文件的硬链接边界，Windows 错误码：{Marshal.GetLastPInvokeError()}。");
        }
        return information.NumberOfLinks;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
