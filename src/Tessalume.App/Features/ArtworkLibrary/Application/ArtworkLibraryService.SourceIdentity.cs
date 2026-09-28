using System.IO;
using System.Security.Cryptography;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.ArtworkLibrary.Infrastructure;

namespace Tessalume.App.Features.ArtworkLibrary.Application;

internal sealed partial class ArtworkLibraryService
{
    private async Task<string> ResolveSourceFingerprintAsync(string key, string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (_document.SourceFingerprints.TryGetValue(key, out var existing) &&
            existing.Length == info.Length && existing.LastWriteTicks == info.LastWriteTimeUtc.Ticks)
            return existing.Sha256;

        // Cache the byte identity across launches. Unchanged source files never
        // need a full read on subsequent gallery filters or refreshes.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        var fingerprints = new Dictionary<string, ArtworkLibrarySourceFingerprint>(_document.SourceFingerprints, StringComparer.OrdinalIgnoreCase)
        { [key] = new(info.Length, info.LastWriteTimeUtc.Ticks, hash) };
        _document = _document with { SourceFingerprints = fingerprints };
        return hash;
    }

    private ArtworkLibraryDocument RecoverPreparedOriginals(ArtworkLibraryDocument document,
        List<ArtworkLibraryItem> builtIns, List<string> diagnostics)
    {
        var currentIds = builtIns.Select(item => item.Id).Concat(document.Images.Select(image => image.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recovered = new List<ArtworkLibraryStoredImage>();
        foreach (var (id, storedPath) in document.PreparedImages)
        {
            if (currentIds.Contains(id) || ResolvePersonal(storedPath) is null) continue;
            document.PreparedMetadata.TryGetValue(id, out var metadata);
            var themeId = id.Split(':').ElementAtOrDefault(1) ?? "personal";
            recovered.Add(metadata is null ? new ArtworkLibraryStoredImage
            {
                Id = id,
                Name = "已保留的主题图片",
                StoredPath = storedPath,
                CharacterId = themeId.Split('.')[0],
                CharacterName = "已保留的图片",
                AddedAt = DateTimeOffset.UtcNow,
            } : metadata with { Id = id, StoredPath = storedPath });
        }
        if (recovered.Count == 0) return document;
        diagnostics.Add($"{recovered.Count} 张已选用的旧主题原图已保留到个人图库，收藏与构图保持不变。");
        return document with { Images = [.. document.Images, .. recovered] };
    }
}
