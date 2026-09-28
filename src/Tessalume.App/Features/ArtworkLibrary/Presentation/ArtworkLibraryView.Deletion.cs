using System.Windows;
using System.Windows.Threading;
using Tessalume.App.Features.ArtworkLibrary.Domain;

namespace Tessalume.App.Features.ArtworkLibrary.Presentation;

public partial class ArtworkLibraryView
{
    internal void RequestDelete()
    {
        if (_disposed || _busy || _selectedItem is not { CanDelete: true } item) return;
        DeleteRequested?.Invoke(this, new ArtworkLibraryDeleteEventArgs(item));
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => RequestDelete();

    internal async Task CompleteImportedImageDeletionAsync(ArtworkLibraryItem item)
    {
        if (_disposed) return;
        var closeDetail = _selectedItem?.Id == item.Id;
        var character = _characterId;
        if (character == item.CharacterId) _characterName ??= item.CharacterName;
        // Remove the persisted deletion locally first. A refresh failure must
        // not leave the deleted picture selected or visible in a stale grid.
        _items = _items.Where(candidate => candidate.Id != item.Id).ToArray();
        if (closeDetail)
        {
            ClearSelection();
            _detailVisible = false;
            DetailPage.Visibility = Visibility.Collapsed;
            GalleryPage.Visibility = Visibility.Visible;
        }
        await FilterAsync(preserveSelection: true);
        await RefreshAsync();
        RenderCurrentTargetImage();
        if (!closeDetail) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_disposed && !_detailVisible && _characterId == character)
                FindParentScrollViewer()?.ScrollToVerticalOffset(_galleryScrollOffset);
        }));
    }
}
