using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Tessalume.App.Features.ArtworkLibrary.Application;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.App.Features.ArtworkLibrary.Presentation;

public partial class ArtworkLibraryView : UserControl, IDisposable
{
    private const int PageSize = 24;
    private const int CharacterPageSize = 12;
    private int CurrentPageSize => _characterId is null ? CharacterPageSize : PageSize;
    private readonly ArtworkPreviewImageCache _imageCache = new();
    private ArtworkLibraryService? _service;
    private IReadOnlyList<ThemePackage> _themes = [];
    private IReadOnlyDictionary<string, ThemeVisualSettings> _settings = new Dictionary<string, ThemeVisualSettings>();
    private IReadOnlyList<ArtworkLibraryItem> _items = [];
    private List<ArtworkLibraryItem> _filtered = [];
    private CancellationTokenSource? _loadCancellation, _pageCancellation, _previewCancellation;
    private int _page, _previewVersion;
    private bool _updating, _busy, _disposed, _previewReady, _compact, _undoAvailable;
    private ArtworkLibraryItem? _selectedItem;
    private ThemeArtworkAdjustment _draft = new(), _initialDraft = new();
    private ArtworkPlacementProjection? _gestureStart;
    private string? _previewPath;
    private ArtworkRegion? _browseRegion;
    private ArtworkLibraryTargetEventArgs? _entryTarget;
    private string? _initialCharacterThemeId;
    private bool _detailVisible;
    private double _galleryScrollOffset;
    private IReadOnlyList<ArtworkCard> _renderedCards = [];
    private string _fallbackThemeId = string.Empty;
    private ArtworkColorMode _fallbackMode;
    private bool _draftEdited;

    public ArtworkLibraryView()
    {
        InitializeComponent();
        PreviewCanvas.SetSources(null);
        PreviewCanvas.SetEmptyMessage("从图库打开图片，在这里查看构图。");
        PreviewCanvas.SetGuidesVisible(false);
        PreviewCanvas.SetViewMode(Personalization.ArtworkWorkbench.Presentation.ArtworkCanvasViewMode.Result);
        PreviewCanvas.InteractionStarted += (_, _) => _gestureStart = _previewReady ? PreviewCanvas.PlacementProjection : null;
        PreviewCanvas.InteractionCompleted += (_, _) => _gestureStart = null;
        PreviewCanvas.DragRequested += Canvas_DragRequested;
        PreviewCanvas.ZoomRequested += Canvas_ZoomRequested;
        PreviewCanvas.CropFrameChanged += Canvas_CropFrameChanged;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _busy) return;
            if (_detailVisible) BackToGallery();
            else if (_characterId is not null) _ = BackToCharactersAsync();
            else return;
            e.Handled = true;
        };
        UpdateActions();
    }

    internal event EventHandler<ArtworkLibraryApplyEventArgs>? ApplyRequested;
    internal event EventHandler<ArtworkLibraryImportEventArgs>? ImportRequested;
    internal event EventHandler<ArtworkLibraryDeleteEventArgs>? DeleteRequested;
    internal event EventHandler<ArtworkLibraryTargetEventArgs>? RestoreRequested;
    internal event EventHandler<ArtworkLibraryTargetEventArgs>? WorkbenchRequested;
    internal event EventHandler? UndoRequested;
    internal string SelectedThemeId => (ThemeSelector.SelectedItem as NamedChoice)?.Id ?? "";
    internal ArtworkRegion SelectedRegion => (ArtworkRegion)Math.Max(0, RegionSelector.SelectedIndex);
    internal ArtworkColorMode SelectedMode => ModeSelector.SelectedIndex == 1 ? ArtworkColorMode.Dark : ArtworkColorMode.Light;
    internal ArtworkLibraryItem? SelectedItem => _selectedItem;
    internal ThemeArtworkAdjustment DraftAdjustment => _draft;
    internal bool IsPreviewReady => _previewReady;
    internal string? PreviewIdentityPath => _previewPath;
    internal IReadOnlyList<ArtworkLibraryItem> Items => _items;
    internal IReadOnlyList<ArtworkLibraryItem> RenderedItems => _renderedCards.Select(card => card.Item).ToArray();
    internal ArtworkRegion? BrowseRegion => _browseRegion;
    internal bool IsDetailVisible => _detailVisible;
    internal string CurrentTargetImageLabel => CurrentImageText.Text;
    internal bool IsCurrentTargetDefault => CurrentTargetSource()?.SourceKind == ArtworkImageSourceKind.ThemeOriginal;

    internal void Configure(ArtworkLibraryService service, IReadOnlyList<ThemePackage> themes,
        IReadOnlyDictionary<string, ThemeVisualSettings> settings, string themeId,
        ArtworkRegion region = ArtworkRegion.Hero, ArtworkColorMode mode = ArtworkColorMode.Light,
        bool preserveEntryTarget = false, string? initialCharacterThemeId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _loadCancellation?.Cancel();
        _pageCancellation?.Cancel();
        CancelPreview();
        _service = service;
        _themes = themes;
        _settings = settings;
        _items = [];
        _entryTarget = preserveEntryTarget ? new ArtworkLibraryTargetEventArgs(themeId, region, mode) : null;
        _initialCharacterThemeId = initialCharacterThemeId;
        _initialCharacterPending = initialCharacterThemeId is not null;
        _characterId = null;
        _characterName = null;
        _characterReturnState = null;
        _renderedCharacters = [];
        _browseRegion = null;
        _renderedCards = [];
        _detailVisible = false;
        GalleryPage.Visibility = Visibility.Visible;
        DetailPage.Visibility = Visibility.Collapsed;
        _updating = true;
        ArtworkItems.ItemsSource = null;
        CharacterItems.ItemsSource = null;
        SearchBox.Text = string.Empty;
        FavoritesOnly.IsChecked = false;
        PreviewModeSelector.SelectedIndex = 0;
        _page = 0;
        ThemeSelector.ItemsSource = themes.Select(t => new NamedChoice(t.Manifest.Id, t.Manifest.Name)).ToArray();
        ThemeSelector.SelectedIndex = Math.Max(0, themes.ToList().FindIndex(t => t.Manifest.Id == themeId));
        _fallbackThemeId = SelectedThemeId;
        _fallbackMode = mode;
        RegionSelector.SelectedIndex = (int)region;
        ModeSelector.SelectedIndex = (int)mode;
        _updating = false;
        RenderTarget();
        ClearSelection();
        RenderBrowseFilters();
    }

    internal void UpdateSettings(IReadOnlyDictionary<string, ThemeVisualSettings> settings)
    {
        _settings = settings;
        RenderCurrentTargetImage();
    }

    internal async Task RefreshAsync()
    {
        if (_disposed || _service is null) return;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;
        SetStatus("正在整理角色图片…");
        try
        {
            var snapshot = await _service.LoadAsync(_themes, _settings, token);
            token.ThrowIfCancellationRequested();
            _items = snapshot.Items;
            RenderCurrentTargetImage();
            if (_initialCharacterPending)
            {
                _characterId = _items.FirstOrDefault(i => i.SourceThemeId == _initialCharacterThemeId)?.CharacterId;
                _characterName = _items.FirstOrDefault(i => i.CharacterId == _characterId)?.CharacterName;
                _initialCharacterPending = false;
            }
            await FilterAsync(preserveSelection: true);
            token.ThrowIfCancellationRequested();
            if (_selectedItem is { } selected && _items.FirstOrDefault(i => i.Id == selected.Id) is { } refreshed)
            {
                _selectedItem = refreshed;
                RenderSelectedMetadata();
            }
            SetStatus(snapshot.Diagnostics.Count == 0
                ? string.Empty
                : $"已收录 {_items.Count} 张图片；{string.Join("；", snapshot.Diagnostics.Take(2))}");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested) SetStatus($"图库加载失败：{exception.Message}。可以点击刷新重试。");
        }
        finally { _updating = false; }
    }

    internal void SelectTarget(string themeId, ArtworkRegion region, ArtworkColorMode mode) =>
        _ = SelectTargetAsync(themeId, region, mode);

    internal async Task SelectTargetAsync(string themeId, ArtworkRegion region, ArtworkColorMode mode)
    {
        if (_disposed) return;
        _updating = true;
        ThemeSelector.SelectedItem = ThemeSelector.Items.OfType<NamedChoice>().FirstOrDefault(t => t.Id == themeId);
        RegionSelector.SelectedIndex = (int)region;
        ModeSelector.SelectedIndex = (int)mode;
        _updating = false;
        await ChangeTargetAsync();
    }

    internal void SetBusy(bool busy)
    {
        _busy = busy;
        BrowseToolbar.IsEnabled = !busy;
        TargetControls.IsEnabled = !busy;
        ArtworkItems.IsEnabled = !busy;
        CharacterItems.IsEnabled = !busy;
        BrowseBackButton.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy;
        BackButton.IsEnabled = !busy;
        UpdateActions();
    }

    internal void SetStatus(string message) => StatusText.Text = message;
    internal void SetUndoAvailable(bool available)
    {
        _undoAvailable = available;
        UpdateActions();
    }

    internal void CancelDraft()
    {
        BackToGallery();
        SetStatus("已取消预览，已保存的主题图片和构图保持原样。");
    }

    internal void BackToGallery()
    {
        if (_busy || _disposed) return;
        ClearSelection();
        _detailVisible = false;
        DetailPage.Visibility = Visibility.Collapsed;
        GalleryPage.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_disposed && !_detailVisible) FindParentScrollViewer()?.ScrollToVerticalOffset(_galleryScrollOffset);
        }));
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackToGallery();

    private void OpenDetail()
    {
        if (!_detailVisible) _galleryScrollOffset = FindParentScrollViewer()?.VerticalOffset ?? 0;
        SetStatus(string.Empty);
        _detailVisible = true;
        GalleryPage.Visibility = Visibility.Collapsed;
        DetailPage.Visibility = Visibility.Visible;
        PositionPreview();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_disposed || !_detailVisible) return;
            FindParentScrollViewer()?.ScrollToTop();
            BackButton.Focus();
        }));
    }

    internal void RequestApply()
    {
        if (_busy || !_previewReady || _selectedItem is null || !IsTargetSupported()) return;
        ApplyRequested?.Invoke(this, new ArtworkLibraryApplyEventArgs(
            SelectedThemeId, SelectedRegion, SelectedMode, _selectedItem, _draft.Normalize()));
    }

    private async void Target_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _service is null || PreviewCanvas is null) return;
        await ChangeTargetAsync();
    }

    private async Task ChangeTargetAsync()
    {
        RenderTarget();
        // Changing the destination re-composes the chosen picture. It must not
        // send the user back to the library or silently change its filters.
        if (_detailVisible && _selectedItem is { } item) await LoadSelectedPreviewAsync(item);
    }

    private void RenderTarget()
    {
        var name = (ThemeSelector.SelectedItem as NamedChoice)?.Name ?? "尚未选择主题";
        TargetSummary.Text = $"{name} · {RegionName(SelectedRegion)} · {ModeName(SelectedMode)}";
        ApplyButton.Content = "应用到此位置";
        PreviewCanvas.SetRegion(SelectedRegion);
        PreviewCanvas.SetColorMode(SelectedMode);
        PreviewCanvas.SetTargetViewport(SelectedRegion switch
        {
            ArtworkRegion.Sidebar => new Size(260, 800),
            ArtworkRegion.Chat => new Size(1440, 900),
            ArtworkRegion.TaskLeft => new Size(146, 234),
            ArtworkRegion.Memory => new Size(146, 165),
            ArtworkRegion.TaskRightSecondary or ArtworkRegion.TaskRightPrimary => new Size(156, 278),
            _ => new Size(1440, 420),
        });
        PreviewCanvas.SetScenePreview(true);
        UpdatePreviewHeight();
        RenderSupportedRegions();
        RenderCurrentTargetImage();
        UpdateActions();
    }

    private void ClearSelection()
    {
        CancelPreview();
        _selectedItem = null;
        _previewReady = false;
        _previewPath = null;
        _draft = _initialDraft = new ThemeArtworkAdjustment();
        _draftEdited = false;
        PreviewCanvas.SetSources(null);
        PreviewCanvas.SetLoading(false);
        PreviewCanvas.SetEmptyMessage("选择一张图片，先看看放进页面的效果。");
        SelectedName.Text = "挑一张喜欢的图片";
        SelectedDetail.Text = "角色原图与个人收藏，都在这里。";
        RecommendationText.Text = "每个区域、每种模式都可以拥有独立的搭配。";
        UpdateActions();
    }

    private void UpdateActions()
    {
        if (ApplyButton is null) return;
        var hasTarget = !string.IsNullOrEmpty(SelectedThemeId);
        var supported = hasTarget && IsTargetSupported();
        ApplyButton.IsEnabled = supported && _previewReady && !_busy;
        FavoriteButton.IsEnabled = _selectedItem is not null && _items.Any(item => item.Id == _selectedItem.Id) && !_busy;
        DeleteButton.Visibility = _selectedItem?.CanDelete == true ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.IsEnabled = _selectedItem?.CanDelete == true && !_busy;
        ZoomInButton.IsEnabled = ZoomOutButton.IsEnabled = ResetDraftButton.IsEnabled = _previewReady && !_busy;
        CancelDraftButton.IsEnabled = _selectedItem is not null && !_busy;
        WorkbenchButton.IsEnabled = RestoreButton.IsEnabled = supported && !_busy;
        UndoButton.IsEnabled = _undoAvailable && !_busy;
        PreviewCanvas.IsEnabled = !_busy;
    }

    private void CancelPreview()
    {
        _previewVersion++;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        _gestureStart = null;
        PreviewCanvas?.SetMotionPreview(false, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPreview();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _imageCache.Clear();
        GC.SuppressFinalize(this);
    }

    private static bool IsCardRegion(ArtworkRegion region) => region is ArtworkRegion.TaskLeft or ArtworkRegion.Memory or ArtworkRegion.TaskRightSecondary or ArtworkRegion.TaskRightPrimary;
    private static string RegionName(ArtworkRegion region) => region switch
    {
        ArtworkRegion.Sidebar => "左侧栏",
        ArtworkRegion.Chat => "聊天背景",
        ArtworkRegion.TaskLeft => "左侧角色卡",
        ArtworkRegion.Memory => "记忆卡",
        ArtworkRegion.TaskRightSecondary => "右侧副卡",
        ArtworkRegion.TaskRightPrimary => "右侧主卡",
        _ => "首页横幅",
    };
    private static string ModeName(ArtworkColorMode mode) => mode == ArtworkColorMode.Dark ? "暗色" : "亮色";
    private sealed record NamedChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
