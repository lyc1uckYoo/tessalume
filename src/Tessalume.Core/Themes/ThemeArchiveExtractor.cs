using System.IO.Compression;

namespace Tessalume.Core.Themes;

public static class ThemeArchiveExtractor
{
    private const int MaximumEntries = 1000;
    private const long MaximumArchiveBytes = 120L * 1024 * 1024;
    private const long MaximumEntryBytes = 30L * 1024 * 1024;
    private const long MaximumExtractedBytes = 120L * 1024 * 1024;
    private const string WorkingDirectoryPrefix = "theme-import-";

    public static async Task<ThemeArchiveExtraction> ExtractAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        archivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("找不到所选 ZIP 主题包。", archivePath);
        }
        if (!Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("只支持 .zip 格式的主题压缩包。");
        }
        if (new FileInfo(archivePath).Length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("ZIP 主题包不能超过 120 MiB。");
        }

        var workingRoot = GetWorkingRoot();
        Directory.CreateDirectory(workingRoot);
        var extractionRoot = Path.Combine(workingRoot, WorkingDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionRoot);

        try
        {
            await ExtractEntriesAsync(archivePath, extractionRoot, cancellationToken);
            var manifests = Directory
                .EnumerateFiles(extractionRoot, ThemePackageLoader.ManifestFileName, SearchOption.AllDirectories)
                .Where(path => !HasMacMetadataSegment(extractionRoot, path))
                .ToArray();
            if (manifests.Length == 0)
            {
                throw new InvalidDataException("ZIP 中没有找到 manifest.json；请压缩完整主题文件夹后重试。");
            }
            if (manifests.Length > 1)
            {
                throw new InvalidDataException("一个 ZIP 只能包含一个主题，但当前找到了多个 manifest.json。");
            }

            return new ThemeArchiveExtraction(extractionRoot, Path.GetDirectoryName(manifests[0])!);
        }
        catch
        {
            DeleteWorkingDirectory(extractionRoot);
            throw;
        }
    }

    private static async Task ExtractEntriesAsync(
        string archivePath,
        string extractionRoot,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count == 0)
        {
            throw new InvalidDataException("ZIP 主题包为空。");
        }
        if (archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException($"ZIP 主题包最多允许 {MaximumEntries} 个文件和文件夹。");
        }

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinkEntry(entry);
            var relativePath = NormalizeEntryPath(entry.FullName);
            if (relativePath.Length == 0) continue;

            var destination = ResolveContainedPath(extractionRoot, relativePath);
            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            if (isDirectory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            if (entry.Length > MaximumEntryBytes)
            {
                throw new InvalidDataException($"ZIP 中的单个文件不能超过 30 MiB：{entry.FullName}");
            }
            totalBytes += entry.Length;
            if (totalBytes > MaximumExtractedBytes)
            {
                throw new InvalidDataException("ZIP 解压后的总大小不能超过 120 MiB。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true);
            await CopyWithLimitAsync(input, output, entry.FullName, cancellationToken);
        }
    }

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        string entryName,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long copied = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            copied += read;
            if (copied > MaximumEntryBytes)
            {
                throw new InvalidDataException($"ZIP 中的单个文件不能超过 30 MiB：{entryName}");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string NormalizeEntryPath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return string.Empty;
        if (entryName.StartsWith('/') || entryName.StartsWith('\\'))
        {
            throw new InvalidDataException($"ZIP 包含绝对路径：{entryName}");
        }
        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"ZIP 包含无效路径：{entryName}");
        }
        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var destination = Path.GetFullPath(Path.Combine(root, relativePath));
        var relative = Path.GetRelativePath(root, destination);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"ZIP 包含越界路径：{relativePath}");
        }
        return destination;
    }

    private static void RejectLinkEntry(ZipArchiveEntry entry)
    {
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        if ((windowsAttributes & FileAttributes.ReparsePoint) != 0 || unixFileType == 0xA000)
        {
            throw new InvalidDataException($"ZIP 不能包含符号链接或重解析点：{entry.FullName}");
        }
    }

    private static bool HasMacMetadataSegment(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase));
    }

    internal static string GetWorkingRoot() =>
        Path.Combine(Path.GetTempPath(), "Tessalume", "theme-imports");

    internal static void DeleteWorkingDirectory(string path)
    {
        try
        {
            var workingRoot = Path.GetFullPath(GetWorkingRoot());
            var target = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(workingRoot, target);
            if (!relative.StartsWith(WorkingDirectoryPrefix, StringComparison.Ordinal) ||
                relative.Contains(Path.DirectorySeparatorChar) ||
                Path.IsPathRooted(relative))
            {
                return;
            }
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Temporary extraction cleanup must not change a completed import result.
        }
    }
}

public sealed class ThemeArchiveExtraction : IDisposable
{
    private readonly string _workingDirectory;
    private bool _disposed;

    internal ThemeArchiveExtraction(string workingDirectory, string themeDirectory)
    {
        _workingDirectory = workingDirectory;
        ThemeDirectory = themeDirectory;
    }

    public string ThemeDirectory { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ThemeArchiveExtractor.DeleteWorkingDirectory(_workingDirectory);
    }
}
