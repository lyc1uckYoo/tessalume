using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

namespace Tessalume.App.Features.ArtworkLibrary.Presentation;

public partial class ArtworkLibraryView
{
    private string? _characterId;
    private string? _characterName;
    private bool _initialCharacterPending;
    private IReadOnlyList<ArtworkLibraryCharacter> _filteredCharacters = [];
    private IReadOnlyList<CharacterCard> _renderedCharacters = [];
    private CharacterBrowseState? _characterReturnState;

    private sealed record CharacterBrowseState(string Search, bool FavoritesOnly, int Page, double ScrollOffset);

    internal string? SelectedCharacterId => _characterId;
    internal IReadOnlyList<ArtworkLibraryCharacter> RenderedCharacters =>
        _renderedCharacters.Select(card => card.Character).ToArray();

    internal async Task OpenCharacterAsync(string characterId)
    {
        if (_disposed || _busy || !_items.Any(item => item.CharacterId == characterId)) return;
        if (_detailVisible) BackToGallery();
        var query = SearchBox.Text;
        var favorite = FavoritesOnly.IsChecked == true;
        if (_characterId is null)
            _characterReturnState = new(query, favorite, _page, FindParentScrollViewer()?.VerticalOffset ?? 0);
        _characterId = characterId;
        var name = _items.First(item => item.CharacterId == characterId).CharacterName;
        _characterName = name;
        _updating = true;
        SearchBox.Text = name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ? string.Empty : query;
        FavoritesOnly.IsChecked = favorite;
        _page = 0;
        _browseRegion = null;
        _updating = false;
        RenderBrowseFilters();
        await FilterAsync();
        if (!_disposed && _characterId == characterId && !_detailVisible) FindParentScrollViewer()?.ScrollToTop();
    }

    internal async Task BackToCharactersAsync()
    {
        if (_disposed || _busy) return;
        if (_detailVisible) BackToGallery();
        var restore = _characterReturnState ?? new CharacterBrowseState(string.Empty, false, 0, 0);
        _characterId = null;
        _characterName = null;
        _updating = true;
        SearchBox.Text = restore.Search;
        FavoritesOnly.IsChecked = restore.FavoritesOnly;
        _page = restore.Page;
        _browseRegion = null;
        _updating = false;
        RenderBrowseFilters();
        await FilterAsync(preserveSelection: true);
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_disposed && !_detailVisible && _characterId is null)
                FindParentScrollViewer()?.ScrollToVerticalOffset(restore.ScrollOffset);
        }));
    }

    private async void BackToCharacters_Click(object sender, RoutedEventArgs e) => await BackToCharactersAsync();

    private async void CharacterCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CharacterCard card }) await OpenCharacterAsync(card.Id);
    }

    private void RenderBrowserLevel()
    {
        var characters = _characterId is null;
        CharactersPanel.Visibility = characters ? Visibility.Visible : Visibility.Collapsed;
        AlbumPanel.Visibility = characters ? Visibility.Collapsed : Visibility.Visible;
        BrowseBackButton.Visibility = BrowseRegionSelector.Visibility = characters ? Visibility.Collapsed : Visibility.Visible;
        BrowseTitleText.Text = characters ? "角色图库" :
            _items.FirstOrDefault(item => item.CharacterId == _characterId)?.CharacterName ?? _characterName ?? "角色相册";
        SearchPlaceholder.Text = characters ? "搜索角色或图片" : "搜索相册图片";
        BackButton.Content = characters ? "← 返回角色图库" : "← 返回角色相册";
        var emptyAlbum = !characters && !_items.Any(item => item.CharacterId == _characterId);
        EmptyTitle.Text = emptyAlbum ? "这个相册还没有图片" : "还没有匹配的图片";
        EmptyDescription.Text = emptyAlbum ? "可以导入新图片，或返回全部角色。" : "试试其他搜索，或导入喜欢的图片。";
    }

    private async Task RenderCharacterPageAsync()
    {
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = new CancellationTokenSource();
        var token = _pageCancellation.Token;
        _renderedCards = [];
        _renderedSections = [];
        ArtworkItems.ItemsSource = null;
        var cards = _filteredCharacters.Skip(_page * CharacterPageSize).Take(CharacterPageSize).Select(character =>
        {
            var cover = _filtered.Where(item => item.CharacterId == character.Id)
                .OrderByDescending(item => item.Regions.Contains(ArtworkRegion.Hero) && item.Modes.Contains(ArtworkColorMode.Light))
                .ThenByDescending(item => item.IsBuiltIn).First();
            return new CharacterCard(character, cover);
        }).ToArray();
        _renderedCharacters = cards;
        CharacterItems.ItemsSource = cards;
        LibraryCountText.Text = $"{_filteredCharacters.Count} 个角色";
        EmptyState.Visibility = cards.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var pages = Math.Max(1, (int)Math.Ceiling(_filteredCharacters.Count / (double)CharacterPageSize));
        PageText.Text = $"{_page + 1} / {pages}";
        PreviousButton.IsEnabled = _page > 0;
        NextButton.IsEnabled = _page + 1 < pages;
        PaginationPanel.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        RenderBrowserLevel();
        UpdateGridColumns();
        using var decoding = new SemaphoreSlim(3);
        await Task.WhenAll(cards.Select(async card =>
        {
            var entered = false;
            try
            {
                await decoding.WaitAsync(token);
                entered = true;
                var bitmap = await _imageCache.LoadAsync(card.CoverItem.AbsolutePath, 700, token);
                token.ThrowIfCancellationRequested();
                card.Cover = bitmap;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally { if (entered) decoding.Release(); }
        }));
    }

    private sealed class CharacterCard(ArtworkLibraryCharacter character, ArtworkLibraryItem coverItem) : INotifyPropertyChanged
    {
        private ImageSource? _cover;
        private double _cardWidth = 300;
        public ArtworkLibraryCharacter Character { get; } = character;
        internal ArtworkLibraryItem CoverItem { get; } = coverItem;
        public string Id => Character.Id;
        public string Name => Character.Name;
        public string Summary => $"{Character.ImageCount} 张图片";
        public ImageSource? Cover { get => _cover; set { _cover = value; Changed(); } }
        public double CardWidth => _cardWidth;
        public double ImageHeight => (_cardWidth - 16) * 10d / 16;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void SetWidth(double width)
        {
            if (Math.Abs(width - _cardWidth) < .1) return;
            _cardWidth = width;
            Changed(nameof(CardWidth));
            Changed(nameof(ImageHeight));
        }
        private void Changed([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
