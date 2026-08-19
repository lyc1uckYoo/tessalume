using System.IO;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Pets;

namespace Tessalume.App.Features.Pets;

internal sealed class PetGalleryService : IDisposable
{
    private readonly PetGalleryServiceOptions _options;
    private readonly PetPackageLoader _packageLoader = new();
    private readonly PetLibraryWatcher _libraryWatcher;
    private readonly Dictionary<string, PetGalleryEntry> _lastGoodEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public PetGalleryService(
        PortableLayout layout,
        PetGalleryServiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _options = options ?? PetGalleryServiceOptions.ForLayout(layout);
        _options = _options with
        {
            PackagesRoot = Path.GetFullPath(_options.PackagesRoot),
        };
        _libraryWatcher = new PetLibraryWatcher(_options.PackagesRoot);
        _libraryWatcher.Changed += LibraryWatcher_Changed;
    }

    public event EventHandler? PackagesChanged;

    public string PackagesRoot => _options.PackagesRoot;

    public void SetWatching(bool active)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _libraryWatcher.SetActive(active);
    }

    public async Task<PetGallerySnapshot> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entries = new List<PetGalleryEntry>();

        var scanner = new PetCatalogScanner(_packageLoader);
        foreach (var candidate in await scanner.ScanAsync(
                     _options.PackagesRoot,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = CreateEntry(candidate);
            if (entry.IsValid)
            {
                _lastGoodEntries[candidate.DirectoryPath] = entry;
            }
            else if (_lastGoodEntries.TryGetValue(candidate.DirectoryPath, out var previous))
            {
                entry = previous with
                {
                    UsesLastGoodPreview = true,
                    HealthMessage = "宠物资源正在更新，暂时保留上一组完整预览。",
                };
            }
            entries.Add(entry);
        }

        return new PetGallerySnapshot(
            entries
                .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            _options.PackagesRoot,
            DateTimeOffset.Now);
    }

    private static PetGalleryEntry CreateEntry(PetPackageCandidate candidate)
    {
        var package = candidate.Package;
        if (package is null)
        {
            var directoryName = Path.GetFileName(candidate.DirectoryPath);
            return new PetGalleryEntry
            {
                EntryKey = $"package:{directoryName}",
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
                PreviewFrames = [],
                IsValid = false,
            };
        }

        var catalog = package.Catalog;
        var runtimeSpritesheetPath = package.ResolvedFiles[package.Manifest.SpritesheetPath];
        var runtimeSpritesheetRevision = package.Catalog.Files.First(file =>
            string.Equals(
                file.Path,
                package.Manifest.SpritesheetPath,
                StringComparison.OrdinalIgnoreCase)).Sha256;
        return new PetGalleryEntry
        {
            EntryKey = $"package:{package.Manifest.Id}",
            PetId = package.Manifest.Id,
            DisplayName = package.Manifest.DisplayName,
            Description = package.Manifest.Description,
            Version = catalog.ProductVersion,
            Author = catalog.Author.Name,
            LicenseSummary = catalog.License.Name ?? catalog.License.Spdx ?? catalog.License.Kind,
            ProtocolSummary = FormatProtocol(catalog.Protocol),
            RootDirectory = package.RootDirectory,
            SourceBadge = "正式宠物",
            HealthMessage = "资源已通过完整哈希校验，可以预览和安全安装。",
            LastUpdated = GetDirectoryLastUpdated(package.RootDirectory),
            PreviewFrames = package.PreviewFiles
                .Select(preview => CreatePreviewFrame(
                    preview.Metadata,
                    preview.FullPath,
                    preview.GifInfo,
                    package.Catalog.Files.First(file =>
                        string.Equals(file.Path, preview.Metadata.Path, StringComparison.OrdinalIgnoreCase)).Sha256,
                    runtimeSpritesheetPath,
                    runtimeSpritesheetRevision))
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

    private static PetPreviewFrame CreatePreviewFrame(
        PetPreviewMetadata metadata,
        string fullPath,
        PetGifInfo? gifInfo,
        string revision,
        string? runtimeSpritesheetPath,
        string runtimeSpritesheetRevision) =>
        new(
            metadata.ActionKey,
            metadata.Label ?? metadata.ActionKey,
            fullPath,
            metadata.Kind,
            gifInfo?.FrameCount ?? metadata.ExpectedFrameCount,
            gifInfo?.Width ?? metadata.Width,
            gifInfo?.Height ?? metadata.Height,
            metadata.RepresentativeFrame,
            revision,
            runtimeSpritesheetPath,
            runtimeSpritesheetRevision);

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
        return messages.Length == 0 ? "宠物资源暂时不可用。" : string.Join("；", messages);
    }

    private static string GetRecommendedThemeName(string? themeId) =>
        string.Equals(themeId, PetApplicationService.RecommendedThemeId, StringComparison.OrdinalIgnoreCase)
            ? "爱弥斯 · 星海远航"
            : string.IsNullOrWhiteSpace(themeId)
                ? string.Empty
                : themeId;

    private void LibraryWatcher_Changed(object? sender, EventArgs e) =>
        PackagesChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _libraryWatcher.Changed -= LibraryWatcher_Changed;
        _libraryWatcher.Dispose();
        GC.SuppressFinalize(this);
    }
}
