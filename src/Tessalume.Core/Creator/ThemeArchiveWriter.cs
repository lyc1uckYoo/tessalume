using System.IO.Compression;
using System.Security.Cryptography;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Creator;

public sealed record ThemeArchiveExportResult(
    string ThemeId,
    string ThemeVersion,
    string ArchivePath,
    int FileCount,
    long UncompressedBytes,
    long CompressedBytes,
    string Sha256,
    string RevisionHash);

public sealed class ThemeArchiveWriter
{
    private static readonly DateTimeOffset StableEntryTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ThemePackageLoader _loader;
    private readonly ThemeProjectScanner _scanner;

    public ThemeArchiveWriter(ThemePackageLoader loader)
    {
        _loader = loader;
        _scanner = new ThemeProjectScanner(loader);
    }

    public async Task<ThemeArchiveExportResult> ExportAsync(
        string themeDirectory,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var source = Path.GetFullPath(themeDirectory);
        var destination = Path.GetFullPath(archivePath);
        if (!Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("主题分享包必须使用 .zip 扩展名。");
        }

        var health = await _scanner.ScanProjectAsync(source, cancellationToken);
        if (!health.Health.CanExport)
        {
            var errors = health.Health.Checks
                .Where(check => check.Severity == ThemeProjectHealthSeverity.Error)
                .Select(check => $"{check.Title}：{check.Message}");
            throw new InvalidDataException(
                $"主题项目未通过创作体检，无法导出：{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }

        // Export always performs its own fresh package validation instead of trusting a previous UI scan.
        var loadResult = await _loader.LoadAsync(source, cancellationToken);
        var package = loadResult.Package;
        if (package is null)
        {
            var details = string.Join(
                Environment.NewLine,
                loadResult.Validation.Issues.Select(issue => issue.Message));
            throw new InvalidDataException($"导出前二次校验失败：{Environment.NewLine}{details}");
        }

        var files = CollectPackageFiles(package);
        var revisionHash = await ThemeFingerprintCalculator.CalculateAsync(package, cancellationToken);
        var destinationDirectory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileNameWithoutExtension(destination)}.{Guid.NewGuid():N}.tmp.zip");

        try
        {
            await WriteArchiveAsync(package, files, temporaryPath, cancellationToken);
            await VerifyArchiveAsync(temporaryPath, revisionHash, cancellationToken);

            var compressedBytes = new FileInfo(temporaryPath).Length;
            var sha256 = await CalculateFileSha256Async(temporaryPath, cancellationToken);
            ReplaceAtomically(temporaryPath, destination);

            return new ThemeArchiveExportResult(
                package.Manifest.Id,
                package.Manifest.Version,
                destination,
                files.Length,
                files.Sum(file => new FileInfo(file.AbsolutePath).Length),
                compressedBytes,
                sha256,
                revisionHash);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A completed export has already been moved to its final path.
            }
        }
    }

    private static PackageFile[] CollectPackageFiles(ThemePackage package)
    {
        var files = new Dictionary<string, PackageFile>(StringComparer.OrdinalIgnoreCase);
        AddPackageFile(files, package, package.ManifestPath);
        if (package.CssPath is not null) AddPackageFile(files, package, package.CssPath);
        if (package.ScriptPath is not null) AddPackageFile(files, package, package.ScriptPath);
        if (package.ArtworkDefaultsPath is not null)
        {
            AddPackageFile(files, package, package.ArtworkDefaultsPath);
        }
        if (package.PreviewLightPath is not null) AddPackageFile(files, package, package.PreviewLightPath);
        if (package.PreviewDarkPath is not null) AddPackageFile(files, package, package.PreviewDarkPath);
        foreach (var path in package.AssetPaths.Values)
        {
            AddPackageFile(files, package, path);
        }

        return files.Values
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddPackageFile(
        Dictionary<string, PackageFile> files,
        ThemePackage package,
        string absolutePath)
    {
        var relativePath = Path.GetRelativePath(package.RootDirectory, absolutePath);
        if (relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"主题文件越过了项目目录：{absolutePath}");
        }
        if ((File.GetAttributes(absolutePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"主题分享包不能包含符号链接或重解析文件：{relativePath}");
        }

        relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        files.TryAdd(relativePath, new PackageFile(relativePath, absolutePath));
    }

    private static async Task WriteArchiveAsync(
        ThemePackage package,
        IReadOnlyList<PackageFile> files,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = $"{package.Manifest.Id}/{file.RelativePath}";
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = StableEntryTimestamp;
            await using var input = new FileStream(
                file.AbsolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);
            await using var entryStream = entry.Open();
            await input.CopyToAsync(entryStream, cancellationToken);
        }
    }

    private async Task VerifyArchiveAsync(
        string archivePath,
        string expectedRevisionHash,
        CancellationToken cancellationToken)
    {
        using var extraction = await ThemeArchiveExtractor.ExtractAsync(archivePath, cancellationToken);
        var loadResult = await _loader.LoadAsync(extraction.ThemeDirectory, cancellationToken);
        var reloaded = loadResult.Package
            ?? throw new InvalidDataException("导出的 ZIP 无法被 Tessalume 重新读取。");
        var actualRevisionHash = await ThemeFingerprintCalculator.CalculateAsync(reloaded, cancellationToken);
        if (!string.Equals(expectedRevisionHash, actualRevisionHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("导出复验失败：ZIP 中的主题内容与源项目不一致。");
        }
    }

    private static async Task<string> CalculateFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void ReplaceAtomically(string temporaryPath, string destination)
    {
        if (File.Exists(destination))
        {
            File.Replace(temporaryPath, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporaryPath, destination);
    }

    private sealed record PackageFile(string RelativePath, string AbsolutePath);
}
