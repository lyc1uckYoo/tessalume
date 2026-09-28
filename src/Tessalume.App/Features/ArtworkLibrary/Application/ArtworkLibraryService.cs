using System.IO;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.ArtworkLibrary.Infrastructure;
using Tessalume.App.Features.Personalization;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.App.Features.ArtworkLibrary.Application;

/// <summary>
/// Owns portable picture metadata, never theme selection or visual settings.
/// Preview remains a caller-owned draft; preparing a picture copies its original
/// bytes into the existing personal store only when the caller chooses to apply.
/// </summary>
internal sealed partial class ArtworkLibraryService : IAsyncDisposable
{
    private readonly string _dataDirectory;
    private readonly string _catalogPath;
    private readonly PersonalImageStore _imageStore;
    private readonly ArtworkThemeDefaultsStore _defaultsStore = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ArtworkLibraryDocument _document = new();
    private IReadOnlyList<ArtworkLibraryItem> _items = [];
    private bool _loaded;
    private string? _readOnlyReason;
    private int _disposeStarted;

    public ArtworkLibraryService(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _catalogPath = Path.Combine(_dataDirectory, "personalization", "library.json");
        _imageStore = new PersonalImageStore(_dataDirectory);
    }

    internal ArtworkImageSource? ResolveTargetSource(ThemePackage package, ArtworkRegion region,
        ArtworkColorMode mode, ThemeArtworkAdjustment adjustment) =>
        ArtworkImageSourceResolver.Resolve(package, _imageStore, region, mode, adjustment);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        // New operations are rejected before joining the queue. Operations
        // already queued finish before the semaphore itself is released.
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<ArtworkLibrarySnapshot> LoadAsync(
        IReadOnlyList<ThemePackage> packages,
        IReadOnlyDictionary<string, ThemeVisualSettings> themeSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(themeSettings);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var diagnostics = new List<string>();
            EnsureSafePersonalizationRoot();
            _document = await ReadDocumentAsync(diagnostics, cancellationToken);
            _loaded = true;
            var fingerprintsBefore = _document.SourceFingerprints;
            var builtIns = new List<ArtworkLibraryItem>();
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builtIns.AddRange(await IndexPackageAsync(package, diagnostics, cancellationToken));
            }

            var document = RecoverPreparedOriginals(_document, builtIns, diagnostics);
            var hiddenPaths = document.HiddenImagePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var known = document.Images.Select(image => image.StoredPath)
                .Concat(document.PreparedImages.Values).Concat(hiddenPaths).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var imagesDirectory = Path.Combine(_dataDirectory, "personalization", "images");
            if (Directory.Exists(imagesDirectory) && !IsReparsePoint(imagesDirectory))
            {
                var discovered = new List<ArtworkLibraryStoredImage>();
                foreach (var path in Directory.EnumerateFiles(imagesDirectory).Order(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stored = ToStoredPath(path);
                    if (known.Contains(stored) || ResolvePersonal(stored) is null) continue;
                    var owner = FindExistingOwner(stored, packages, themeSettings);
                    discovered.Add(new ArtworkLibraryStoredImage
                    {
                        Id = PersonalId(stored),
                        Name = Path.GetFileNameWithoutExtension(path),
                        StoredPath = stored,
                        CharacterId = owner.Id,
                        CharacterName = owner.Name,
                        Regions = owner.Regions,
                        Modes = owner.Modes,
                        AddedAt = File.GetCreationTimeUtc(path),
                    });
                }
                if (discovered.Count > 0)
                {
                    document = document with { Images = [.. document.Images, .. discovered] };
                }
            }
            if (!ReferenceEquals(document, _document) || !ReferenceEquals(fingerprintsBefore, document.SourceFingerprints))
            {
                if (_readOnlyReason is null) await WriteDocumentAsync(document, cancellationToken);
                else _document = document;
            }
            var builtinIds = builtIns.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var personalItems = new List<ArtworkLibraryItem>();
            var missing = 0;
            foreach (var image in document.Images)
            {
                if (builtinIds.Contains(image.Id) ||
                    (IsImportedIdentity(image.Id, document) && hiddenPaths.Contains(image.StoredPath))) continue;
                var path = ResolvePersonal(image.StoredPath);
                if (path is null) { missing++; continue; }
                personalItems.Add(CreatePersonalItem(image, path));
            }
            if (missing > 0) diagnostics.Add($"有 {missing} 张个人图片暂时找不到原文件，已保留其分类和收藏信息。");
            var favorites = document.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _items = builtIns.Concat(personalItems).Select(item => item with
            {
                IsFavorite = favorites.Contains(item.Id),
                Usages = FindUsages(item, packages, themeSettings),
            }).ToArray();
            return new ArtworkLibrarySnapshot(_items, _items.GroupBy(item => item.CharacterId, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ArtworkLibraryCharacter(group.Key, group.First().CharacterName, group.Count()))
                .OrderBy(character => character.Name, StringComparer.CurrentCulture).ToArray(), diagnostics);
        }
        finally { _gate.Release(); }
    }

    public async Task<ArtworkLibraryItem> ImportAsync(
        string sourcePath, string characterId, string characterName,
        ArtworkRegion? region, ArtworkColorMode? mode, CancellationToken cancellationToken = default)
    {
        if (region.HasValue && !Enum.IsDefined(region.Value)) throw new ArgumentOutOfRangeException(nameof(region));
        if (mode.HasValue && !Enum.IsDefined(mode.Value)) throw new ArgumentOutOfRangeException(nameof(mode));
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            var stored = await _imageStore.ImportAsync(sourcePath, cancellationToken);
            var existing = _document.Images.FirstOrDefault(image =>
                IsImportedIdentity(image.Id, _document) &&
                string.Equals(image.StoredPath, stored, StringComparison.OrdinalIgnoreCase));
            var record = existing is null ? new ArtworkLibraryStoredImage
            {
                Id = PersonalId(stored),
                StoredPath = stored,
                Name = CleanText(Path.GetFileNameWithoutExtension(sourcePath), "个人图片"),
                CharacterId = CleanText(characterId, "personal"),
                CharacterName = CleanText(characterName, "我的图片"),
                Regions = region.HasValue ? [region.Value] : [],
                Modes = mode.HasValue ? [mode.Value] : [],
                AddedAt = DateTimeOffset.UtcNow,
            } : existing with
            {
                Regions = region.HasValue ? existing.Regions.Append(region.Value).Distinct().ToList() : existing.Regions,
                Modes = mode.HasValue ? existing.Modes.Append(mode.Value).Distinct().ToList() : existing.Modes,
            };
            var images = _document.Images.Where(image => image.Id != record.Id).Append(record).ToList();
            var hiddenPaths = _document.HiddenImagePaths.Where(path =>
                !string.Equals(path, stored, StringComparison.OrdinalIgnoreCase)).ToList();
            await WriteDocumentAsync(_document with { Images = images, HiddenImagePaths = hiddenPaths }, cancellationToken);
            var item = CreatePersonalItem(record, ResolvePersonal(stored)!) with
            { IsFavorite = _document.Favorites.Contains(record.Id, StringComparer.OrdinalIgnoreCase) };
            _items = _items.Where(candidate => candidate.Id != item.Id).Append(item).ToArray();
            return item;
        }
        finally { _gate.Release(); }
    }

    public async Task SetFavoriteAsync(string itemId, bool favorite, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            EnsureKnownItem(itemId);
            var favorites = _document.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (favorite) favorites.Add(itemId); else favorites.Remove(itemId);
            await WriteDocumentAsync(_document with { Favorites = favorites.Order(StringComparer.Ordinal).ToList() }, cancellationToken);
            _items = _items.Select(item => item.Id == itemId ? item with { IsFavorite = favorite } : item).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<string> PrepareImageAsync(ArtworkLibraryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            var knownItem = EnsureKnownItem(item.Id);
            if (!knownItem.IsBuiltIn)
            {
                var path = ResolvePersonal(knownItem.StoredPath);
                if (path is null) throw new FileNotFoundException("图库原图已被移动或删除，请重新导入。");
                return ToStoredPath(path);
            }
            var stored = await _imageStore.ImportAsync(knownItem.AbsolutePath, cancellationToken);
            if (!knownItem.Id.EndsWith(":" + Path.GetFileNameWithoutExtension(stored), StringComparison.OrdinalIgnoreCase))
                throw new IOException("主题原图在预览后发生变化，请刷新图库后重新选择。");
            var prepared = new Dictionary<string, string>(_document.PreparedImages, StringComparer.OrdinalIgnoreCase)
            { [knownItem.Id] = stored };
            var metadata = new Dictionary<string, ArtworkLibraryStoredImage>(_document.PreparedMetadata, StringComparer.OrdinalIgnoreCase)
            {
                [knownItem.Id] = new()
                {
                    Id = knownItem.Id,
                    Name = knownItem.Name,
                    CharacterId = knownItem.CharacterId,
                    CharacterName = knownItem.CharacterName,
                    StoredPath = stored,
                    Regions = knownItem.Regions.ToList(),
                    Modes = knownItem.Modes.ToList(),
                    AddedAt = DateTimeOffset.UtcNow,
                },
            };
            await WriteDocumentAsync(_document with { PreparedImages = prepared, PreparedMetadata = metadata }, cancellationToken);
            _items = _items.Select(candidate => candidate.Id == item.Id ? candidate with { StoredPath = stored } : candidate).ToArray();
            return stored;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveCompositionAsync(string itemId, string themeId, ArtworkRegion region,
        ArtworkColorMode mode, ThemeArtworkAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        ArgumentNullException.ThrowIfNull(adjustment);
        EnsureTarget(region, mode);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            EnsureKnownItem(itemId);
            var composition = new ArtworkLibraryComposition(itemId, CleanText(themeId, string.Empty), region, mode,
                adjustment.Normalize() with { CustomImagePath = null, ThemeAssetKey = null });
            var compositions = _document.Compositions.Where(saved => !SameTarget(saved, itemId, themeId, region, mode))
                .Append(composition).ToList();
            await WriteDocumentAsync(_document with { Compositions = compositions }, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public ThemeArtworkAdjustment ResolveInitialAdjustment(ArtworkLibraryItem item, string themeId,
        ArtworkRegion region, ArtworkColorMode mode)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureTarget(region, mode);
        ThrowIfDisposed();
        var saved = _document.Compositions.LastOrDefault(value => SameTarget(value, item.Id, themeId, region, mode));
        if (saved is not null) return saved.Adjustment.Normalize();
        var recommended = item.Recommendations.FirstOrDefault(value => value.Region == region && value.Mode == mode)
            ?? item.Recommendations.FirstOrDefault(value => value.Region == region);
        return (recommended?.Adjustment ?? new ThemeArtworkAdjustment
        {
            CompositionMode = ThemeArtworkCompositionMode.Custom,
            Placement = new ThemeArtworkPlacementSpec(),
        }).Normalize() with
        { CustomImagePath = null, ThemeAssetKey = null };
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

    private static bool SameTarget(ArtworkLibraryComposition value, string itemId, string themeId, ArtworkRegion region, ArtworkColorMode mode) =>
        string.Equals(value.ItemId, itemId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(value.ThemeId, themeId, StringComparison.OrdinalIgnoreCase) && value.Region == region && value.Mode == mode;
}
