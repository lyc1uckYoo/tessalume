using System.IO;
using Tessalume.App.Features.ArtworkLibrary.Infrastructure;

namespace Tessalume.App.Features.ArtworkLibrary.Application;

internal sealed partial class ArtworkLibraryService
{
    public async Task DeleteImportedImageAsync(string itemId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            var item = EnsureKnownItem(itemId);
            if (!item.CanDelete || !IsImportedIdentity(item.Id, _document))
                throw new InvalidOperationException("主题自带图片不能从图库删除。");
            if (!IsPersonalRelativePath(item.StoredPath))
                throw new InvalidDataException("这张图片的图库记录无效，请刷新后重试。");

            var stored = item.StoredPath!;
            var removedIds = _document.Images.Where(image => IsImportedIdentity(image.Id, _document) &&
                    string.Equals(image.StoredPath, stored, StringComparison.OrdinalIgnoreCase))
                .Select(image => image.Id).Append(item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hiddenPaths = _document.HiddenImagePaths.Append(stored)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // The content-addressed file can also be a prepared theme original
            // or be referenced by current settings and undo. Hide only personal
            // source identities, and never remove their shared bytes.
            var document = _document with
            {
                Images = _document.Images.Where(image => !removedIds.Contains(image.Id)).ToList(),
                HiddenImagePaths = hiddenPaths,
                Favorites = _document.Favorites.Where(id => !removedIds.Contains(id)).ToList(),
                Compositions = _document.Compositions.Where(value => !removedIds.Contains(value.ItemId)).ToList(),
            };
            await WriteDocumentAsync(document, cancellationToken);
            _items = _items.Where(candidate => !removedIds.Contains(candidate.Id)).ToArray();
        }
        finally { _gate.Release(); }
    }

    private static bool IsImportedIdentity(string id, ArtworkLibraryDocument document) =>
        id.StartsWith("personal:", StringComparison.Ordinal) &&
        !document.PreparedImages.ContainsKey(id) && !document.PreparedMetadata.ContainsKey(id);
}
