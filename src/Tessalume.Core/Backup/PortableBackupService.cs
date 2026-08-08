using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tessalume.Core.Backup;

public sealed partial class PortableBackupService
{
    public const int CurrentSchemaVersion = 1;

    private const int MaximumFiles = 6000;
    private const long MaximumFileBytes = 100L * 1024 * 1024;
    private const long MaximumTotalBytes = 1024L * 1024 * 1024;
    private const string ManifestEntryName = "tessalume-backup.json";
    private static readonly DateTimeOffset StableArchiveTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<string> DataFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ui-settings.json",
        "state.json",
        "deleted-built-in-themes.txt",
    };

    private static readonly HashSet<string> PersonalImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp",
        };

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".legacy", ".references", ".sources", "logs", "updates", "downloads",
        "cache", "temp", "tmp", "backups", "node_modules", "bin", "obj",
    };

    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".log", ".bak", ".download", ".partial", ".psd", ".psb",
        ".kra", ".xcf", ".blend", ".aep", ".ai",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _rootDirectory;
    private readonly string _dataDirectory;
    private readonly string _themesDirectory;
    private readonly IReadOnlySet<string> _builtInThemeIds;

    public PortableBackupService(
        string rootDirectory,
        string dataDirectory,
        string themesDirectory,
        IReadOnlySet<string>? builtInThemeIds = null)
    {
        _rootDirectory = NormalizeDirectory(rootDirectory);
        _dataDirectory = NormalizeDirectory(dataDirectory);
        _themesDirectory = NormalizeDirectory(themesDirectory);
        EnsureContained(_rootDirectory, _dataDirectory);
        EnsureContained(_rootDirectory, _themesDirectory);
        _builtInThemeIds = builtInThemeIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PortableBackupResult> CreateAsync(
        string archivePath,
        PortableBackupOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        options ??= new PortableBackupOptions();
        var destination = Path.GetFullPath(archivePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + $".{Guid.NewGuid():N}.tmp";
        var sources = await CollectSourcesAsync(options, cancellationToken);
        if (sources.Count == 0)
        {
            throw new InvalidDataException("当前没有可备份的 Tessalume 用户数据。");
        }

        try
        {
            var manifest = await BuildManifestAsync(sources, options.IncludeImportedThemes, cancellationToken);
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var source in sources.OrderBy(item => item.ArchivePath, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(source.ArchivePath, CompressionLevel.Optimal);
                    entry.LastWriteTime = StableArchiveTimestamp;
                    await using var input = OpenSourceRead(source.SourcePath);
                    await using var entryStream = entry.Open();
                    await input.CopyToAsync(entryStream, cancellationToken);
                }

                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                manifestEntry.LastWriteTime = StableArchiveTimestamp;
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
            }

            _ = await InspectAsync(temporaryPath, cancellationToken);
            File.Move(temporaryPath, destination, overwrite: true);
            var sha256 = await ComputeFileHashAsync(destination, cancellationToken);
            return new PortableBackupResult(
                destination,
                sha256,
                new FileInfo(destination).Length,
                ToSummary(manifest));
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    public static async Task<PortableBackupSummary> InspectAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        var inspection = await InspectArchiveAsync(archivePath, cancellationToken);
        return ToSummary(inspection.Manifest);
    }

    public async Task<PortableRestoreResult> RestoreAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        var inspection = await InspectArchiveAsync(archivePath, cancellationToken);
        await ValidateRestorePolicyAsync(inspection.Manifest, cancellationToken);
        Directory.CreateDirectory(_dataDirectory);
        var backupsDirectory = Path.Combine(_dataDirectory, "backups");
        Directory.CreateDirectory(backupsDirectory);
        var snapshotPath = CreateUniqueSnapshotPath(backupsDirectory);
        await CreateAsync(
            snapshotPath,
            new PortableBackupOptions
            {
                IncludeImportedThemes = inspection.Manifest.IncludesImportedThemes,
            },
            cancellationToken);

        var transactionRoot = Path.Combine(_rootDirectory, $".tessalume-restore-{Guid.NewGuid():N}");
        var stagedRoot = Path.Combine(transactionRoot, "staged");
        var rollbackRoot = Path.Combine(transactionRoot, "rollback");
        Directory.CreateDirectory(stagedRoot);
        Directory.CreateDirectory(rollbackRoot);
        var applied = new List<RestoreTarget>();
        try
        {
            await ExtractVerifiedAsync(archivePath, inspection, stagedRoot, cancellationToken);
            var targets = BuildRestoreTargets(inspection.Manifest, stagedRoot, rollbackRoot);
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                applied.Add(target);
                ApplyTarget(target);
            }
            return new PortableRestoreResult(ToSummary(inspection.Manifest), snapshotPath);
        }
        catch
        {
            RollBack(applied);
            throw;
        }
        finally
        {
            TryDeleteDirectory(transactionRoot);
        }
    }

    private async Task<List<SourceFile>> CollectSourcesAsync(
        PortableBackupOptions options,
        CancellationToken cancellationToken)
    {
        var result = new List<SourceFile>();
        foreach (var fileName in DataFileNames.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(_dataDirectory, fileName);
            if (File.Exists(path))
            {
                AddSource(result, path, $"data/{fileName}", themeDirectoryName: null);
            }
        }

        var personalImagesDirectory = Path.Combine(
            _dataDirectory,
            "personalization",
            "images");
        if (Directory.Exists(personalImagesDirectory) && !IsReparsePoint(personalImagesDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(personalImagesDirectory)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(file) ||
                    !PersonalImageExtensions.Contains(Path.GetExtension(file))) continue;
                AddSource(
                    result,
                    file,
                    $"data/personalization/images/{Path.GetFileName(file)}",
                    themeDirectoryName: null);
            }
        }

        if (!options.IncludeImportedThemes || !Directory.Exists(_themesDirectory)) return result;
        foreach (var directory in Directory.EnumerateDirectories(_themesDirectory)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(directory)) continue;
            var metadata = await ReadThemeMetadataAsync(directory, cancellationToken);
            if (metadata.ThemeId is not null && _builtInThemeIds.Contains(metadata.ThemeId)) continue;
            var directoryName = Path.GetFileName(directory);
            foreach (var file in EnumerateThemeFiles(directory))
            {
                var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                AddSource(result, file, $"themes/{directoryName}/{relative}", directoryName);
            }
        }
        return result;
    }

    private static IEnumerable<string> EnumerateThemeFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if (ExcludedDirectoryNames.Contains(Path.GetFileName(directory)) || IsReparsePoint(directory)) continue;
                pending.Push(directory);
            }
            foreach (var file in Directory.EnumerateFiles(current))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith('~') || ExcludedExtensions.Contains(Path.GetExtension(file)) ||
                    IsReparsePoint(file) ||
                    fileName.Equals(".optimizer-cache.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                yield return file;
            }
        }
    }

    private static void AddSource(
        ICollection<SourceFile> sources,
        string sourcePath,
        string archivePath,
        string? themeDirectoryName)
    {
        var info = new FileInfo(sourcePath);
        if (info.Length > MaximumFileBytes)
        {
            throw new InvalidDataException($"备份文件超过 100 MiB 限制：{sourcePath}");
        }
        if (sources.Count >= MaximumFiles)
        {
            throw new InvalidDataException($"备份文件数量超过 {MaximumFiles} 个限制。");
        }
        if (sources.Sum(item => item.Size) + info.Length > MaximumTotalBytes)
        {
            throw new InvalidDataException("备份内容超过 1 GiB 限制。");
        }
        sources.Add(new SourceFile(sourcePath, NormalizeArchivePath(archivePath), info.Length, themeDirectoryName));
    }

    private async Task<BackupManifest> BuildManifestAsync(
        IReadOnlyList<SourceFile> sources,
        bool includesImportedThemes,
        CancellationToken cancellationToken)
    {
        var files = new List<BackupFileManifest>(sources.Count);
        foreach (var source in sources.OrderBy(item => item.ArchivePath, StringComparer.Ordinal))
        {
            files.Add(new BackupFileManifest(
                source.ArchivePath,
                source.Size,
                await ComputeFileHashAsync(source.SourcePath, cancellationToken)));
        }

        var themes = new List<BackupThemeManifest>();
        foreach (var group in sources.Where(item => item.ThemeDirectoryName is not null)
                     .GroupBy(item => item.ThemeDirectoryName!, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var metadata = await ReadThemeMetadataAsync(
                Path.Combine(_themesDirectory, group.Key),
                cancellationToken);
            themes.Add(new BackupThemeManifest(
                group.Key,
                metadata.ThemeId,
                metadata.DisplayName,
                metadata.Version,
                group.Count(),
                group.Sum(item => item.Size)));
        }

        return new BackupManifest(
            CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            includesImportedThemes,
            files,
            themes);
    }

    private static async Task<ArchiveInspection> InspectArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var path = Path.GetFullPath(archivePath);
        if (!File.Exists(path)) throw new FileNotFoundException("找不到 Tessalume 备份文件。", path);

        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > MaximumFiles + 1)
        {
            throw new InvalidDataException("备份文件数量超过安全限制。");
        }
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
            {
                throw new InvalidDataException("备份包不能包含目录占位条目。");
            }
            var normalized = NormalizeArchivePath(entry.FullName);
            if (!normalized.Equals(entry.FullName.Replace('\\', '/'), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"备份包包含非规范路径：{entry.FullName}");
            }
            if (!entries.TryAdd(normalized, entry))
            {
                throw new InvalidDataException($"备份包包含重复路径：{normalized}");
            }
            if (entry.Length > MaximumFileBytes)
            {
                throw new InvalidDataException($"备份条目超过 100 MiB 限制：{normalized}");
            }
        }

        if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry))
        {
            throw new InvalidDataException("这不是有效的 Tessalume 用户数据备份。");
        }
        BackupManifest manifest;
        try
        {
            await using var stream = manifestEntry.Open();
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("备份清单为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("备份清单不是有效的 JSON。", exception);
        }
        ValidateManifest(manifest, entries);

        long totalBytes = 0;
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[file.Path];
            if (entry.Length != file.Size)
            {
                throw new InvalidDataException($"备份条目大小不匹配：{file.Path}");
            }
            totalBytes += entry.Length;
            if (totalBytes > MaximumTotalBytes)
            {
                throw new InvalidDataException("备份解压后超过 1 GiB 安全限制。");
            }
            await using var stream = entry.Open();
            var actualHash = await ComputeHashAsync(stream, cancellationToken);
            if (!actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"备份条目校验失败：{file.Path}");
            }
        }
        return new ArchiveInspection(manifest);
    }

    private static void ValidateManifest(
        BackupManifest manifest,
        Dictionary<string, ZipArchiveEntry> entries)
    {
        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"不支持的备份格式版本：{manifest.SchemaVersion}");
        }
        if (manifest.Files is null || manifest.Themes is null ||
            manifest.Files.Count == 0 || manifest.Files.Count > MaximumFiles)
        {
            throw new InvalidDataException("备份清单没有有效文件，或文件数量超过限制。");
        }
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Path) ||
                string.IsNullOrWhiteSpace(file.Sha256))
            {
                throw new InvalidDataException("备份清单包含空文件记录。");
            }
            var path = NormalizeArchivePath(file.Path);
            if (!path.Equals(file.Path.Replace('\\', '/'), StringComparison.Ordinal) ||
                !IsAllowedContentPath(path) || !declared.Add(path) ||
                file.Size < 0 || file.Size > MaximumFileBytes ||
                file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"备份清单包含无效条目：{file.Path}");
            }
            if (!entries.ContainsKey(path))
            {
                throw new InvalidDataException($"备份清单声明的文件不存在：{path}");
            }
        }
        var contentEntries = entries.Keys.Where(path => !path.Equals(ManifestEntryName, StringComparison.OrdinalIgnoreCase));
        if (!contentEntries.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(declared))
        {
            throw new InvalidDataException("备份包包含未声明文件，已拒绝恢复。");
        }
        foreach (var dataPath in declared.Where(path => path.StartsWith("data/", StringComparison.OrdinalIgnoreCase)))
        {
            var segments = dataPath.Split('/');
            var isRootDataFile = segments.Length == 2 && DataFileNames.Contains(segments[1]);
            var isPersonalImage = segments.Length == 4 &&
                segments[1].Equals("personalization", StringComparison.OrdinalIgnoreCase) &&
                segments[2].Equals("images", StringComparison.OrdinalIgnoreCase) &&
                PersonalImageExtensions.Contains(Path.GetExtension(segments[3]));
            if (!isRootDataFile && !isPersonalImage)
            {
                throw new InvalidDataException($"备份包含不允许恢复的数据文件：{dataPath}");
            }
        }
        if (!manifest.IncludesImportedThemes && declared.Any(path => path.StartsWith("themes/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("备份清单未声明包含用户主题，但包内存在主题文件。");
        }

        ValidateThemeManifest(manifest);
    }

    private static void ValidateThemeManifest(BackupManifest manifest)
    {
        var themeFiles = manifest.Files
            .Where(file => file.Path.StartsWith("themes/", StringComparison.OrdinalIgnoreCase))
            .GroupBy(file => file.Path.Split('/')[1], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, StringComparer.OrdinalIgnoreCase);
        if (manifest.Themes.Count != themeFiles.Count)
        {
            throw new InvalidDataException("备份中的用户主题摘要与实际文件不一致。");
        }

        var declaredThemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in manifest.Themes)
        {
            if (theme is null || string.IsNullOrWhiteSpace(theme.DirectoryName) ||
                string.IsNullOrWhiteSpace(theme.DisplayName))
            {
                throw new InvalidDataException("备份包含空用户主题摘要。");
            }
            var directoryName = NormalizeArchivePath(theme.DirectoryName);
            if (!directoryName.Equals(theme.DirectoryName.Replace('\\', '/'), StringComparison.Ordinal) ||
                directoryName.Length > 255 || theme.DisplayName.Length > 256 ||
                theme.ThemeId?.Length > 256 || theme.Version?.Length > 128 ||
                directoryName.Contains('/', StringComparison.Ordinal) ||
                !declaredThemes.Add(directoryName) ||
                string.IsNullOrWhiteSpace(theme.DisplayName) ||
                !themeFiles.TryGetValue(directoryName, out var files) ||
                theme.FileCount != files.Count() ||
                theme.TotalBytes != files.Sum(file => file.Size))
            {
                throw new InvalidDataException($"备份包含无效的用户主题摘要：{theme.DirectoryName}");
            }
        }
    }

    private async Task ValidateRestorePolicyAsync(
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var theme in manifest.Themes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_builtInThemeIds.Contains(theme.DirectoryName) ||
                (theme.ThemeId is not null && _builtInThemeIds.Contains(theme.ThemeId)))
            {
                throw new InvalidDataException($"备份不能替换内置主题：{theme.DisplayName}");
            }

            var currentDirectory = Path.Combine(_themesDirectory, theme.DirectoryName);
            if (!Directory.Exists(currentDirectory)) continue;
            var current = await ReadThemeMetadataAsync(currentDirectory, cancellationToken);
            if (current.ThemeId is not null && _builtInThemeIds.Contains(current.ThemeId))
            {
                throw new InvalidDataException($"备份目标与内置主题冲突：{theme.DirectoryName}");
            }
        }
    }

    private static async Task ExtractVerifiedAsync(
        string archivePath,
        ArchiveInspection inspection,
        string stagedRoot,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.ToDictionary(
            entry => NormalizeArchivePath(entry.FullName),
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in inspection.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(stagedRoot, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            EnsureContained(stagedRoot, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entries[file.Path].Open();
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyAndVerifyAsync(input, output, file, cancellationToken);
        }
    }

    private static async Task CopyAndVerifyAsync(
        Stream input,
        Stream output,
        BackupFileManifest expected,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaximumFileBytes || total > expected.Size)
            {
                throw new InvalidDataException($"备份条目在恢复期间发生变化：{expected.Path}");
            }
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != expected.Size || !actualHash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"备份条目在恢复期间校验失败：{expected.Path}");
        }
    }

    private List<RestoreTarget> BuildRestoreTargets(
        BackupManifest manifest,
        string stagedRoot,
        string rollbackRoot)
    {
        var targets = new List<RestoreTarget>();
        foreach (var file in manifest.Files.Where(file =>
                     file.Path.StartsWith("data/", StringComparison.OrdinalIgnoreCase) &&
                     file.Path.Count(character => character == '/') == 1))
        {
            var name = Path.GetFileName(file.Path);
            targets.Add(new RestoreTarget(
                Path.Combine(stagedRoot, "data", name),
                Path.Combine(_dataDirectory, name),
                Path.Combine(rollbackRoot, "data", name),
                isDirectory: false));
        }
        if (manifest.Files.Any(file => file.Path.StartsWith(
                "data/personalization/images/",
                StringComparison.OrdinalIgnoreCase)))
        {
            targets.Add(new RestoreTarget(
                Path.Combine(stagedRoot, "data", "personalization"),
                Path.Combine(_dataDirectory, "personalization"),
                Path.Combine(rollbackRoot, "data", "personalization"),
                isDirectory: true));
        }
        foreach (var directoryName in manifest.Files
                     .Where(file => file.Path.StartsWith("themes/", StringComparison.OrdinalIgnoreCase))
                     .Select(file => file.Path.Split('/')[1])
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            targets.Add(new RestoreTarget(
                Path.Combine(stagedRoot, "themes", directoryName),
                Path.Combine(_themesDirectory, directoryName),
                Path.Combine(rollbackRoot, "themes", directoryName),
                isDirectory: true));
        }
        return targets;
    }

    private static void ApplyTarget(RestoreTarget target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target.TargetPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(target.RollbackPath)!);
        if (target.IsDirectory)
        {
            if (File.Exists(target.TargetPath))
            {
                throw new IOException($"目标主题路径被文件占用：{target.TargetPath}");
            }
            if (Directory.Exists(target.TargetPath))
            {
                Directory.Move(target.TargetPath, target.RollbackPath);
                target.HadOriginal = true;
            }
            Directory.Move(target.StagedPath, target.TargetPath);
        }
        else
        {
            if (Directory.Exists(target.TargetPath))
            {
                throw new IOException($"目标数据路径被文件夹占用：{target.TargetPath}");
            }
            if (File.Exists(target.TargetPath))
            {
                File.Move(target.TargetPath, target.RollbackPath);
                target.HadOriginal = true;
            }
            File.Move(target.StagedPath, target.TargetPath);
        }
        target.Installed = true;
    }

    private static void RollBack(IReadOnlyList<RestoreTarget> applied)
    {
        Exception? rollbackFailure = null;
        foreach (var target in applied.Reverse())
        {
            try
            {
                if (target.Installed)
                {
                    if (target.IsDirectory && Directory.Exists(target.TargetPath))
                    {
                        Directory.Delete(target.TargetPath, recursive: true);
                    }
                    else if (!target.IsDirectory && File.Exists(target.TargetPath))
                    {
                        File.Delete(target.TargetPath);
                    }
                }
                if (!target.HadOriginal) continue;
                if (target.IsDirectory)
                {
                    Directory.Move(target.RollbackPath, target.TargetPath);
                }
                else
                {
                    File.Move(target.RollbackPath, target.TargetPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                rollbackFailure ??= exception;
            }
        }
        if (rollbackFailure is not null)
        {
            throw new IOException("恢复失败，并且自动回滚未能完整完成。请保留自动快照并检查数据目录。", rollbackFailure);
        }
    }

    private static string NormalizeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("备份包含空路径。");
        var normalized = path.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (normalized.Length > 1024 || segments.Length == 0 || segments.Any(segment =>
                segment.Length > 255 ||
                segment is "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.Contains(':', StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"备份包含不安全路径：{path}");
        }
        return string.Join('/', segments);
    }

    private static bool IsAllowedContentPath(string path) =>
        path.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("themes/", StringComparison.OrdinalIgnoreCase) && path.Count(character => character == '/') >= 2;

    private static string NormalizeDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"路径超出便携目录边界：{path}");
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string CreateUniqueSnapshotPath(string directory)
    {
        var baseName = $"auto-before-restore-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
        for (var index = 0; index < 100; index++)
        {
            var suffix = index == 0 ? string.Empty : $"-{index}";
            var path = Path.Combine(directory, $"{baseName}{suffix}.zip");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("无法为恢复前自动快照分配文件名。");
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

}
