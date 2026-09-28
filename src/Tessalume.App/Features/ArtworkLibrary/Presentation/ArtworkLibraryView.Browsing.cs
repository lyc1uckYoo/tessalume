using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

namespace Tessalume.App.Features.ArtworkLibrary.Presentation;

public partial class ArtworkLibraryView
{
    private async void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating || _service is null) return;
        await FilterAsync();
    }

    internal async Task SelectBrowseRegionAsync(ArtworkRegion? region)
    {
        if (_disposed || _busy) return;
        _browseRegion = region;
        RenderBrowseFilters();
        await FilterAsync();
    }

    private async void BrowseRegion_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _service is null) return;
        await SelectBrowseRegionAsync(BrowseRegionSelector.SelectedIndex > 0
            ? (ArtworkRegion)(BrowseRegionSelector.SelectedIndex - 1) : null);
    }

    private void RenderBrowseFilters()
    {
        var updating = _updating;
        _updating = true;
        BrowseRegionSelector.SelectedIndex = _browseRegion is { } region ? (int)region + 1 : 0;
        _updating = updating;
        RenderBrowserLevel();
    }

    private async Task FilterAsync(bool preserveSelection = false)
    {
        if (_disposed) return;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        var character = _characterId;
        _filtered = _items.Where(item =>
                (string.IsNullOrEmpty(character) || item.CharacterId == character) &&
                (FavoritesOnly.IsChecked != true || item.IsFavorite) &&
                (character is null || _browseRegion is null || item.Regions.Count == 0 || item.Regions.Contains(_browseRegion.Value)) &&
                item.Matches(SearchBox.Text))
            .OrderBy(item => item.CharacterName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.SourceThemeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(PrimaryRegion)
            .ThenBy(item => item.Modes.Count > 0 ? item.Modes[0] : ArtworkColorMode.Light)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _filteredCharacters = _filtered.GroupBy(item => item.CharacterId)
            .Select(group => new ArtworkLibraryCharacter(group.Key, group.First().CharacterName, group.Count()))
            .OrderBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var count = _characterId is null ? _filteredCharacters.Count : _filtered.Count;
        _page = preserveSelection ? Math.Clamp(_page, 0, Math.Max(0, count - 1) / CurrentPageSize) : 0;
        await RenderBrowserPageAsync();
    }

    private Task RenderBrowserPageAsync() => _characterId is null ? RenderCharacterPageAsync() : RenderPageAsync();

    private async Task RenderPageAsync()
    {
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = new CancellationTokenSource();
        var token = _pageCancellation.Token;
        _renderedCharacters = [];
        CharacterItems.ItemsSource = null;
        var cards = _filtered.Skip(_page * PageSize).Take(PageSize)
            .Select(item => new ArtworkCard(item, _browseRegion ?? PrimaryRegion(item))).ToArray();
        _renderedCards = cards;
        _renderedSections = cards.GroupBy(card => IsCardRegion(card.Region)).OrderBy(group => group.Key)
            .Select(group => new ArtworkSection(group.Key ? "角色卡片" : "页面背景", group.ToArray())).ToArray();
        ArtworkItems.ItemsSource = _renderedSections;
        LibraryCountText.Text = $"{_filtered.Count} 张";
        EmptyState.Visibility = cards.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var pages = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
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
                card.Thumbnail = await _imageCache.LoadAsync(card.Item.AbsolutePath, 800, token);
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!token.IsCancellationRequested) card.Detail = "图片暂不可用 · 可重新导入"; }
            finally { if (entered) decoding.Release(); }
        }));
    }

    private async void ArtworkCard_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { DataContext: ArtworkCard card }) return;
        await SelectItemAsync(card.Item);
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || _selectedItem is null || _busy) return;
        var item = _selectedItem;
        FavoriteButton.IsEnabled = false;
        try
        {
            await _service.SetFavoriteAsync(item.Id, !item.IsFavorite);
            var replacement = item with { IsFavorite = !item.IsFavorite };
            _items = _items.Select(i => i.Id == item.Id ? replacement : i).ToArray();
            if (_selectedItem?.Id == item.Id)
            {
                _selectedItem = replacement;
                RenderSelectedMetadata();
            }
            await FilterAsync(preserveSelection: true);
            SetStatus(replacement.IsFavorite ? "已加入收藏。" : "已取消收藏。");
        }
        catch (Exception exception) { SetStatus($"收藏未保存：{exception.Message}"); }
        finally { UpdateActions(); }
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 0) return;
        _page--;
        await RenderBrowserPageAsync();
        GalleryPanel.BringIntoView();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if ((_page + 1) * CurrentPageSize >= (_characterId is null ? _filteredCharacters.Count : _filtered.Count)) return;
        _page++;
        await RenderBrowserPageAsync();
        GalleryPanel.BringIntoView();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var characterId = _characterId;
        var reference = string.IsNullOrEmpty(characterId) ? null : _items.FirstOrDefault(item => item.CharacterId == characterId);
        ImportRequested?.Invoke(this, new ArtworkLibraryImportEventArgs(
            characterId ?? "personal", reference?.CharacterName ?? _characterName ?? "我的图片", _browseRegion, null));
    }

    private void View_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DetailControlsColumn is null) return;
        _compact = e.NewSize.Width < 700;
        BrowseTitleText.MaxWidth = e.NewSize.Width < 850 ? 92 : 150;
        LibraryCountText.Visibility = e.NewSize.Width < 720 ? Visibility.Collapsed : Visibility.Visible;
        PositionPreview();
        UpdateGridColumns();
    }

    private void PositionPreview()
    {
        if (DetailControlsColumn is null) return;
        DetailControlsColumn.Width = _compact ? new GridLength(0) : new GridLength(310);
        Grid.SetColumn(TargetPanel, _compact ? 0 : 1);
        Grid.SetRow(TargetPanel, _compact ? 1 : 0);
        PreviewPanel.Margin = _compact ? new Thickness(0, 0, 0, 16) : new Thickness(0, 0, 18, 0);
    }

    private void UpdateGridColumns()
    {
        var width = GalleryPanel.ActualWidth > 0 ? GalleryPanel.ActualWidth : Math.Max(300, ActualWidth);
        var characterColumns = Math.Clamp((int)((width + 12) / 235), 2, 4);
        foreach (var character in _renderedCharacters)
            character.SetWidth(Math.Floor((width + 12) / characterColumns - 12));
        foreach (var section in _renderedSections)
        {
            section.SetWidth(width);
            var columns = Math.Min(section.Regions.Count,
                section.IsCardSection ? width >= 860 ? 4 : width >= 460 ? 2 : 1
                : width >= 650 ? 3 : width >= 430 ? 2 : 1);
            var regionWidth = Math.Floor((width - 12 * (Math.Max(1, columns) - 1)) / Math.Max(1, columns));
            foreach (var region in section.Regions)
            {
                region.SetWidth(regionWidth);
                // Every position owns its vertical flow. Only the two narrow
                // sidebar variants share a row, inside their own column.
                var imageColumns = region.Region == ArtworkRegion.Sidebar && region.Cards.Count > 1 && regionWidth >= 220 ? 2 : 1;
                var cardWidth = Math.Floor((regionWidth + 12) / imageColumns - 12);
                foreach (var card in region.Cards)
                    card.SetWidth(Math.Min(cardWidth, section.IsCardSection ? 230 : 260));
            }
        }
    }

    private ScrollViewer? FindParentScrollViewer()
    {
        for (DependencyObject? current = this; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer scroll) return scroll;
        return null;
    }

    private static ArtworkRegion PrimaryRegion(ArtworkLibraryItem item) => item.Regions.Count == 1
        ? item.Regions[0]
        : item.PixelHeight > item.PixelWidth && item.PixelWidth > 0 ? ArtworkRegion.Sidebar : ArtworkRegion.Chat;

    private IReadOnlyList<ArtworkSection> _renderedSections = [];

    private sealed class ArtworkSection(string title, IReadOnlyList<ArtworkCard> cards) : INotifyPropertyChanged
    {
        private double _sectionWidth = 300;
        public double SectionWidth => _sectionWidth;
        public string Title => title;
        public string CountLabel => $"{cards.Count} 张";
        public IReadOnlyList<ArtworkCard> Cards => cards;
        public bool IsCardSection => cards.Count > 0 && IsCardRegion(cards[0].Region);
        public IReadOnlyList<ArtworkRegionColumn> Regions { get; } = cards.GroupBy(card => card.Region)
            .OrderBy(group => group.Key).Select(group => new ArtworkRegionColumn(group.Key, group.ToArray())).ToArray();
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void SetWidth(double width)
        {
            if (Math.Abs(width - _sectionWidth) < .1) return;
            _sectionWidth = width;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SectionWidth)));
        }
    }

    private sealed class ArtworkRegionColumn(ArtworkRegion region, IReadOnlyList<ArtworkCard> cards) : INotifyPropertyChanged
    {
        private double _regionWidth = 260;
        public ArtworkRegion Region => region;
        public double RegionWidth => _regionWidth;
        public string Title => RegionName(region);
        public string CountLabel => $"{cards.Count} 张";
        public IReadOnlyList<ArtworkCard> Cards => cards;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void SetWidth(double width)
        {
            if (Math.Abs(width - _regionWidth) < .1) return;
            _regionWidth = width;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RegionWidth)));
        }
    }

    private sealed class ArtworkCard : INotifyPropertyChanged
    {
        private ImageSource? _thumbnail;
        private string _detail;
        private double _cardWidth = 300;
        internal ArtworkCard(ArtworkLibraryItem item, ArtworkRegion region)
        {
            Item = item;
            Region = region;
            _detail = item.IsBuiltIn ? item.SourceThemeName ?? item.Name : item.CharacterName;
        }
        public ArtworkLibraryItem Item { get; }
        public ArtworkRegion Region { get; }
        public string Name => Item.IsBuiltIn ? RegionName(Region) : Item.Name;
        public string SourceLabel => Item.IsBuiltIn ? "主题原图" : "我的图片";
        public string ModeLabel => Item.Modes.Count == 1 ? ModeName(Item.Modes[0]) : "通用图片";
        public string FavoriteLabel => Item.IsFavorite ? "♥" : "";
        public string AutomationName => $"打开 {Item.Name}，{Item.CharacterName}，{RegionName(Region)}";
        public string Detail { get => _detail; set { _detail = value; Changed(); } }
        public ImageSource? Thumbnail { get => _thumbnail; set { _thumbnail = value; Changed(); } }
        public double CardWidth => _cardWidth;
        private double ImageRatio => Item.PixelWidth > 0 && Item.PixelHeight > 0
                ? Item.PixelHeight / (double)Item.PixelWidth
                : Region == ArtworkRegion.Sidebar ? 1.5 : .625;
        public double ImageHeight => (_cardWidth - 16) * ImageRatio;
        internal void SetWidth(double width)
        {
            width = Math.Min(width, 280 / ImageRatio + 16);
            if (Math.Abs(_cardWidth - width) < .1) return;
            _cardWidth = width;
            Changed(nameof(CardWidth));
            Changed(nameof(ImageHeight));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
