using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Tessalume.App.Features.Pets;

public partial class PetGalleryView : UserControl
{
    private IReadOnlyList<PetGalleryEntry> _entries = [];

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
            query.Length == 0 ||
             entry.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             entry.PetId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        var cards = filtered
            .Select(PetGalleryCardViewModel.Create)
            .ToArray();

        GalleryItems.ItemsSource = cards;
        GallerySectionTitle.Text = "全部伙伴";
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

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private sealed record PetGalleryCardViewModel(
        PetGalleryEntry Entry,
        BitmapImage? CoverImage)
    {
        public string DisplayName => Entry.DisplayName;

        public string Description => Entry.Description;

        public string SourceBadge => Entry.SourceBadge;

        public string HealthMessage => Entry.HealthMessage;

        public string VersionText => $"v{Entry.Version}";

        public string ActionSummary => $"{Entry.PreviewFrames.Count}/11 动作";

        public string ValidationText => Entry.UsesLastGoodPreview
            ? "资源更新中，暂时显示上一组完整预览"
            : Entry.IsValid
                ? "资源完整，已通过本机校验"
                : Entry.HealthMessage;

        public string FooterText => Entry.UsesLastGoodPreview
            ? "等待资源写入完成"
            : $"更新于 {Entry.LastUpdated.ToLocalTime():MM-dd HH:mm}";

        public string ActionText => Entry.CanOpen ? "查看并安装" : "资源不可用";

        public string AutomationName => $"查看{Entry.DisplayName}{Entry.SourceBadge}";

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
