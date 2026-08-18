using System.IO;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Pets;

namespace Tessalume.App.Features.Pets;

internal sealed class PetGalleryService : IDisposable
{
    private readonly PortableLayout _layout;
    private readonly PetGalleryServiceOptions _options;
    private readonly PetPackageLoader _packageLoader = new();
    private readonly PetDevelopmentProjectLoader _projectLoader = new();
    private readonly PetProjectWatcher _projectWatcher;
    private readonly Dictionary<string, PetGalleryEntry> _lastGoodDevelopmentEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public PetGalleryService(
        PortableLayout layout,
        PetGalleryServiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _options = options ?? PetGalleryServiceOptions.ForLayout(layout);
        _options = _options with
        {
            OfficialPackagesRoot = Path.GetFullPath(_options.OfficialPackagesRoot),
            DevelopmentProjectsRoot = Path.GetFullPath(_options.DevelopmentProjectsRoot),
        };
        _projectWatcher = new PetProjectWatcher(_options.DevelopmentProjectsRoot);
        _projectWatcher.Changed += ProjectWatcher_Changed;
    }

    public event EventHandler? DevelopmentProjectsChanged;

    public string DevelopmentProjectsRoot => _options.DevelopmentProjectsRoot;

    public void SetWatching(bool active)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _projectWatcher.SetActive(active);
    }

    public async Task<PetGallerySnapshot> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BuiltInAssetInstaller.EnsurePetsInstalled(_layout);
        var entries = new List<PetGalleryEntry>();

        var scanner = new PetCatalogScanner(_packageLoader);
        foreach (var candidate in await scanner.ScanAsync(
                     _options.OfficialPackagesRoot,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(CreateOfficialEntry(candidate));
        }

        if (Directory.Exists(_options.DevelopmentProjectsRoot))
        {
            foreach (var directory in Directory
                         .EnumerateDirectories(
                             _options.DevelopmentProjectsRoot,
                             "*",
                             SearchOption.TopDirectoryOnly)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PetDevelopmentLoadResult result;
                try
                {
                    result = await _projectLoader.LoadAsync(directory, cancellationToken);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException or
                    ArgumentException or NotSupportedException)
                {
                    var validation = new PetValidationResult();
                    validation.AddError("project.scan.failed", exception.Message, directory);
                    result = new PetDevelopmentLoadResult(null, validation);
                }

                var entry = CreateDevelopmentEntry(directory, result);
                if (entry.IsValid)
                {
                    _lastGoodDevelopmentEntries[directory] = entry;
                }
                else if (_lastGoodDevelopmentEntries.TryGetValue(directory, out var previous))
                {
                    entry = previous with
                    {
                        UsesLastGoodPreview = true,
                        HealthMessage = "候选文件正在更新，暂时保留上一组完整预览。",
                    };
                }
                entries.Add(entry);
            }
        }

        return new PetGallerySnapshot(
            entries
                .OrderByDescending(entry => entry.IsDevelopment)
                .ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            _options.DevelopmentProjectsRoot,
            DateTimeOffset.Now);
    }

    private static PetGalleryEntry CreateOfficialEntry(PetPackageCandidate candidate)
    {
        var package = candidate.Package;
        if (package is null)
        {
            var directoryName = Path.GetFileName(candidate.DirectoryPath);
            return new PetGalleryEntry
            {
                EntryKey = $"official:{directoryName}",
                PetId = directoryName,
                DisplayName = directoryName,
                Description = "正式宠物包未通过校验，暂时不能预览或安装。",
                Version = "—",
                Author = "—",
                LicenseSummary = "—",
                ProtocolSummary = "校验失败",
                RootDirectory = candidate.DirectoryPath,
                SourceBadge = "正式包异常",
                HealthMessage = SummarizeValidation(candidate.Validation),
                LastUpdated = GetDirectoryLastUpdated(candidate.DirectoryPath),
                SourceKind = PetGallerySourceKind.Official,
                PreviewFrames = [],
                IsValid = false,
            };
        }

        var catalog = package.Catalog;
        return new PetGalleryEntry
        {
            EntryKey = $"official:{package.Manifest.Id}",
            PetId = package.Manifest.Id,
            DisplayName = package.Manifest.DisplayName,
            Description = package.Manifest.Description,
            Version = catalog.ProductVersion,
            Author = catalog.Author.Name,
            LicenseSummary = catalog.License.Name ?? catalog.License.Spdx ?? catalog.License.Kind,
            ProtocolSummary = FormatProtocol(catalog.Protocol),
            RootDirectory = package.RootDirectory,
            SourceBadge = "官方宠物",
            HealthMessage = "已封版并通过完整哈希校验，可以安全安装。",
            LastUpdated = GetDirectoryLastUpdated(package.RootDirectory),
            SourceKind = PetGallerySourceKind.Official,
            PreviewFrames = package.PreviewFiles
                .Select(preview => CreatePreviewFrame(
                    preview.Metadata,
                    preview.FullPath,
                    preview.GifInfo,
                    package.Catalog.Files.First(file =>
                        string.Equals(file.Path, preview.Metadata.Path, StringComparison.OrdinalIgnoreCase)).Sha256))
                .ToArray(),
            RecommendedThemeId = catalog.RecommendedThemeIds.Count == 0
                ? string.Empty
                : catalog.RecommendedThemeIds[0],
            RecommendedThemeName = GetRecommendedThemeName(
                catalog.RecommendedThemeIds.Count == 0
                    ? null
                    : catalog.RecommendedThemeIds[0]),
            IsValid = candidate.Validation.IsValid,
            Package = package,
        };
    }

    private static PetGalleryEntry CreateDevelopmentEntry(
        string directory,
        PetDevelopmentLoadResult result)
    {
        var project = result.Project;
        if (project is null)
        {
            var directoryName = Path.GetFileName(directory);
            return new PetGalleryEntry
            {
                EntryKey = $"development:{directoryName}",
                PetId = directoryName,
                DisplayName = directoryName,
                Description = "Codex 开发项目尚未提供可读取的 pet-project.json。",
                Version = "草稿",
                Author = "—",
                LicenseSummary = "—",
                ProtocolSummary = "等待候选输出",
                RootDirectory = directory,
                SourceBadge = "开发预览",
                HealthMessage = SummarizeValidation(result.Validation),
                LastUpdated = GetDirectoryLastUpdated(directory),
                SourceKind = PetGallerySourceKind.Development,
                PreviewFrames = [],
                IsValid = false,
            };
        }

        var manifest = project.Manifest;
        var frames = project.PreviewFiles
            .Select(preview => CreatePreviewFrame(
                preview.Metadata,
                preview.FullPath,
                preview.GifInfo,
                GetFileRevision(preview.FullPath)))
            .ToArray();
        return new PetGalleryEntry
        {
            EntryKey = $"development:{manifest.Id}",
            PetId = manifest.Id,
            DisplayName = manifest.DisplayName,
            Description = manifest.Description,
            Version = manifest.ProjectVersion,
            Author = manifest.Author.Name,
            LicenseSummary = manifest.License.Name ?? manifest.License.Spdx ?? manifest.License.Kind,
            ProtocolSummary = FormatProtocol(manifest.Protocol),
            RootDirectory = project.RootDirectory,
            SourceBadge = "开发预览",
            HealthMessage = result.Validation.IsValid
                ? $"正在监看 Codex 候选输出 · {project.LastUpdated.ToLocalTime():MM-dd HH:mm:ss}"
                : SummarizeValidation(result.Validation),
            LastUpdated = project.LastUpdated,
            SourceKind = PetGallerySourceKind.Development,
            PreviewFrames = frames,
            RecommendedThemeId = manifest.RecommendedThemeIds.Count == 0
                ? string.Empty
                : manifest.RecommendedThemeIds[0],
            RecommendedThemeName = GetRecommendedThemeName(
                manifest.RecommendedThemeIds.Count == 0
                    ? null
                    : manifest.RecommendedThemeIds[0]),
            IsValid = result.Validation.IsValid &&
                frames.Length == PetDevelopmentProjectContract.RequiredPreviewActionKeys.Count,
            DevelopmentProject = project,
        };
    }

    private static PetPreviewFrame CreatePreviewFrame(
        PetPreviewMetadata metadata,
        string fullPath,
        PetGifInfo gifInfo,
        string revision) =>
        new(
            metadata.ActionKey,
            metadata.Label ?? metadata.ActionKey,
            fullPath,
            metadata.Kind,
            gifInfo.FrameCount,
            gifInfo.Width,
            gifInfo.Height,
            metadata.RepresentativeFrame,
            revision);

    private static string GetFileRevision(string path)
    {
        var info = new FileInfo(path);
        return $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}";
    }

    private static DateTimeOffset GetDirectoryLastUpdated(string directory)
    {
        try
        {
            var latest = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(directory))
                .Max();
            return new DateTimeOffset(DateTime.SpecifyKind(latest, DateTimeKind.Utc));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static string FormatProtocol(PetProtocolMetadata protocol)
    {
        var actionCount = Math.Max(0, protocol.States.Count - 2);
        var directionalFrames = protocol.States.Skip(actionCount).Sum(state => state.Frames);
        return $"协议 v{protocol.SpriteVersionNumber} · {actionCount} 种动作 · " +
               $"{directionalFrames} 向转身 · {protocol.UsedFrameCount} 有效格";
    }

    private static string SummarizeValidation(PetValidationResult validation)
    {
        var messages = validation.Issues
            .Where(issue => issue.Severity == PetValidationSeverity.Error)
            .Select(issue => issue.Message)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return messages.Length == 0 ? "项目暂时不可用。" : string.Join("；", messages);
    }

    private static string GetRecommendedThemeName(string? themeId) =>
        string.Equals(themeId, PetApplicationService.RecommendedThemeId, StringComparison.OrdinalIgnoreCase)
            ? "爱弥斯 · 星海远航"
            : string.IsNullOrWhiteSpace(themeId)
                ? string.Empty
                : themeId;

    private void ProjectWatcher_Changed(object? sender, EventArgs e) =>
        DevelopmentProjectsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _projectWatcher.Changed -= ProjectWatcher_Changed;
        _projectWatcher.Dispose();
        GC.SuppressFinalize(this);
    }
}
