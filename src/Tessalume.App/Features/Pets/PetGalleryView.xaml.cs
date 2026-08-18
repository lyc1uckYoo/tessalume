using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace Tessalume.App.Features.Pets;

public partial class PetGalleryView : UserControl
{
    private IReadOnlyList<PetGalleryEntry> _entries = [];
    private PetGalleryFilter _filter;

    public PetGalleryView()
    {
        InitializeComponent();
    }

    internal event EventHandler<PetGalleryEntry>? EntryRequested;

    internal event EventHandler? RefreshRequested;

    internal void ShowLoading()
    {
        GalleryCountText.Text = "正在扫描…";
        GalleryItems.ItemsSource = null;
        GallerySection.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
    }

    internal void Render(PetGallerySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _entries = snapshot.Entries;
        GalleryCountText.Text = $"{_entries.Count} 个角色伙伴";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var filtered = _entries.Where(entry =>
            (_filter == PetGalleryFilter.All ||
             _filter == PetGalleryFilter.Official && !entry.IsDevelopment ||
             _filter == PetGalleryFilter.Development && entry.IsDevelopment) &&
            (query.Length == 0 ||
             entry.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             entry.PetId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .ToArray();
        var cards = filtered
            .Select(PetGalleryCardViewModel.Create)
            .ToArray();

        GalleryItems.ItemsSource = cards;
        GallerySectionTitle.Text = _filter switch
        {
            PetGalleryFilter.Official => "官方宠物",
            PetGalleryFilter.Development => "开发预览",
            _ => "全部伙伴",
        };
        FilteredCountText.Text = $"{cards.Length} 个结果";
        GallerySection.Visibility = cards.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        EmptyState.Visibility = cards.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PetCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PetGalleryEntry entry } && entry.CanOpen)
        {
            EntryRequested?.Invoke(this, entry);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilter();
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string filter })
        {
            return;
        }
        _filter = filter switch
        {
            "official" => PetGalleryFilter.Official,
            "development" => PetGalleryFilter.Development,
            _ => PetGalleryFilter.All,
        };
        AllFilterButton.IsChecked = _filter == PetGalleryFilter.All;
        OfficialFilterButton.IsChecked = _filter == PetGalleryFilter.Official;
        DevelopmentFilterButton.IsChecked = _filter == PetGalleryFilter.Development;
        ApplyFilter();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private enum PetGalleryFilter
    {
        All,
        Official,
        Development,
    }

    private sealed record PetGalleryCardViewModel(
        PetGalleryEntry Entry,
        BitmapImage? CoverImage)
    {
        public string DisplayName => Entry.DisplayName;

        public string Description => Entry.Description;

        public string SourceBadge => Entry.SourceBadge;

        public string HealthMessage => Entry.HealthMessage;

        public string VersionText => Entry.IsDevelopment
            ? $"v{Entry.Version} · 草稿"
            : $"v{Entry.Version}";

        public string ActionSummary => $"{Entry.PreviewFrames.Count}/11 动作";

        public string FooterText => Entry.IsDevelopment
            ? $"更新于 {Entry.LastUpdated.ToLocalTime():MM-dd HH:mm}"
            : "完整校验 · 可安全安装";

        public string ActionText => Entry.IsDevelopment ? "查看开发预览" : "查看并安装";

        public string AutomationName => $"查看{Entry.DisplayName}{Entry.SourceBadge}";

        public bool IsDevelopment => Entry.IsDevelopment;

        public bool CanOpen => Entry.CanOpen;

        public static PetGalleryCardViewModel Create(PetGalleryEntry entry) =>
            new(entry, LoadCover(entry.CoverPreview?.FilePath));

        private static BitmapImage? LoadCover(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.DecodePixelWidth = 320;
                image.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException or
                ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }
    }
}
