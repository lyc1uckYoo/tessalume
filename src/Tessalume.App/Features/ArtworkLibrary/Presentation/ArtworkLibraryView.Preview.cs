using System.Windows;
using System.Windows.Controls;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.ArtworkLibrary.Presentation;

public partial class ArtworkLibraryView
{
    internal async Task SelectItemAsync(ArtworkLibraryItem item)
    {
        if (_disposed || _service is null || _busy) return;
        _updating = true;
        if (_entryTarget is { } entry)
        {
            ThemeSelector.SelectedItem = ThemeSelector.Items.OfType<NamedChoice>().FirstOrDefault(t => t.Id == entry.ThemeId);
            RegionSelector.SelectedIndex = (int)entry.Region;
            ModeSelector.SelectedIndex = (int)entry.Mode;
        }
        else
        {
            var suggestedThemeId = item.SourceThemeId ?? _items.FirstOrDefault(candidate =>
                candidate.CharacterId == item.CharacterId && candidate.SourceThemeId is not null)?.SourceThemeId;
            ThemeSelector.SelectedItem = ThemeSelector.Items.OfType<NamedChoice>().FirstOrDefault(t => t.Id == suggestedThemeId)
                ?? ThemeSelector.Items.OfType<NamedChoice>().FirstOrDefault(t => t.Id == _fallbackThemeId);
            var region = _browseRegion is { } browse && item.Regions.Contains(browse)
                ? browse : PrimaryRegion(item);
            var mode = item.Modes.Contains(_fallbackMode) || item.Modes.Count == 0 ? _fallbackMode : item.Modes[0];
            RegionSelector.SelectedIndex = (int)region;
            ModeSelector.SelectedIndex = (int)mode;
        }
        _updating = false;
        RenderTarget();
        OpenDetail();
        await LoadSelectedPreviewAsync(item);
    }

    internal async Task ReloadSavedTargetPreviewAsync(string themeId, ArtworkRegion region, ArtworkColorMode mode)
    {
        if (!_detailVisible || SelectedThemeId != themeId || SelectedRegion != region || SelectedMode != mode) return;
        RenderCurrentTargetImage();
        var saved = FindCurrentTargetItem();
        if (saved is not null)
        {
            await LoadSelectedPreviewAsync(saved);
            return;
        }
        // A missing catalog entry must never leave the previous replacement in
        // the preview after a successful restore. Resolve the actual saved file.
        if (CurrentTargetSource() is { } source)
        {
            var theme = _themes.First(theme => theme.Manifest.Id == themeId);
            await LoadSelectedPreviewAsync(new ArtworkLibraryItem
            {
                Id = $"current:{themeId}:{region}:{mode}",
                AbsolutePath = source.AbsolutePath,
                Name = source.DisplayName,
                CharacterName = theme.Manifest.Name,
                SourceThemeId = themeId,
                SourceThemeName = theme.Manifest.Name,
                IsBuiltIn = source.SourceKind == ArtworkImageSourceKind.ThemeOriginal,
                Regions = [region],
                Modes = [mode],
            });
            return;
        }
        ClearSelection();
        PreviewCanvas.SetEmptyMessage("此位置的原图暂不可用，请刷新图库后重试。");
    }

    private async Task LoadSelectedPreviewAsync(ArtworkLibraryItem item)
    {
        if (_disposed || _service is null || !_detailVisible) return;
        CancelPreview();
        var version = _previewVersion;
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        _selectedItem = item;
        _previewReady = false;
        _previewPath = null;
        var isCurrentImage = IsCurrentTargetItem(item);
        var initial = isCurrentImage && _settings.TryGetValue(SelectedThemeId, out var currentSettings)
            ? ArtworkSettingsAccessor.GetAdjustment(currentSettings, SelectedMode, SelectedRegion)
            : _service.ResolveInitialAdjustment(item, SelectedThemeId, SelectedRegion, SelectedMode);
        _draft = _initialDraft = initial.Normalize() with { CustomImagePath = null, ThemeAssetKey = null };
        _draftEdited = false;
        var adjustment = _draft;
        PreviewCanvas.SetComposition(_draft, _draft.Placement ?? new ThemeArtworkPlacementSpec());
        PreviewCanvas.SetSources(null);
        PreviewCanvas.SetLoading(true, "正在加载图片预览…");
        RenderSelectedMetadata();
        UpdateActions();
        PositionPreview();
        if (!IsTargetSupported())
        {
            PreviewCanvas.SetLoading(false);
            PreviewCanvas.SetEmptyMessage("此主题没有这个图片位置，请选择其他位置。");
            return;
        }
        try
        {
            var preview = await _imageCache.LoadWithMetadataAsync(item.AbsolutePath, 1400, token);
            var processed = await ArtworkPreviewPixelEffectProcessor.ProcessAsync(preview.Bitmap, adjustment, token);
            token.ThrowIfCancellationRequested();
            if (_disposed || !_detailVisible || version != _previewVersion || _selectedItem?.Id != item.Id) return;
            PreviewCanvas.SetSources(preview.Bitmap, processed,
                originalPixelSize: new ArtworkSize(preview.SourcePixelWidth, preview.SourcePixelHeight));
            PreviewCanvas.SetLoading(false);
            _previewReady = true;
            _previewPath = item.AbsolutePath;
            var motion = _settings.TryGetValue(SelectedThemeId, out var settings) ? settings.Display.MotionIntensity : "full";
            PreviewCanvas.SetMotionPreview(adjustment.Motion is { Mode: "loop", Keyframes.Count: > 0 } && motion != "off", motion == "reduced");
            UpdateActions();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (_disposed || version != _previewVersion) return;
            PreviewCanvas.SetSources(null);
            PreviewCanvas.SetLoading(false);
            PreviewCanvas.SetEmptyMessage("图片无法预览，请检查原文件后重新选择。");
            SetStatus($"预览失败：{exception.Message}");
            UpdateActions();
        }
    }

    private void RenderSelectedMetadata()
    {
        if (_selectedItem is not { } item) return;
        SelectedName.Text = item.Name;
        var size = item.PixelWidth > 0 ? $" · {item.PixelWidth} × {item.PixelHeight}" : "";
        SelectedDetail.Text = $"{item.CharacterName} · {(item.IsBuiltIn ? "主题原图" : "我的图片")}{size}";
        FavoriteButton.Content = item.IsFavorite ? "★ 已收藏" : "☆ 收藏";
        var current = IsCurrentTargetItem(item);
        PreviewStateText.Text = _draftEdited ? "构图未保存" : current ? "当前已使用" : "预览草稿";
        RecommendationText.Text = _draftEdited ? "构图已调整，应用后保存。" : current
            ? IsCurrentTargetDefault ? "正在预览此位置的主题原图。" : "正在预览已保存的图片和构图。"
            : item.Recommendations.Any(r => r.Region == SelectedRegion && r.Mode == SelectedMode)
                ? "已载入推荐或上次保存的构图。"
                : "拖动或缩放图片，调整到喜欢的位置。";
        RenderCurrentTargetImage();
    }

    private void PreviewMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewCanvas is null) return;
        var fullSource = PreviewModeSelector.SelectedIndex == 1;
        PreviewCanvas.SetViewMode(fullSource ? ArtworkCanvasViewMode.FullSource : ArtworkCanvasViewMode.Result);
        UpdatePreviewHeight();
        PreviewHint.Text = fullSource
            ? "完整原图 · 拖动取景框，查看哪些内容会保留"
            : "标准尺寸场景预览 · 拖动图片或滚轮缩放";
    }

    private void UpdatePreviewHeight()
    {
        PreviewCanvas.Height = PreviewModeSelector.SelectedIndex == 1 ? 400 : SelectedRegion switch
        {
            ArtworkRegion.Sidebar => 450,
            ArtworkRegion.Chat => 360,
            ArtworkRegion.Memory => 280,
            ArtworkRegion.TaskLeft or ArtworkRegion.TaskRightPrimary or ArtworkRegion.TaskRightSecondary => 420,
            _ => 240,
        };
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => RequestApply();
    private void CancelDraft_Click(object sender, RoutedEventArgs e) => CancelDraft();
    private void ResetDraft_Click(object sender, RoutedEventArgs e)
    {
        if (!_previewReady) return;
        _draft = _initialDraft;
        _draftEdited = false;
        PreviewCanvas.SetComposition(_draft, _initialDraft.Placement ?? new ThemeArtworkPlacementSpec());
        RenderSelectedMetadata();
        SetStatus("已重置为本次打开的构图。点击应用才会保存。");
    }
    private void Workbench_Click(object sender, RoutedEventArgs e) => WorkbenchRequested?.Invoke(this,
        new ArtworkLibraryTargetEventArgs(SelectedThemeId, SelectedRegion, SelectedMode));
    private void Restore_Click(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this,
        new ArtworkLibraryTargetEventArgs(SelectedThemeId, SelectedRegion, SelectedMode));
    private void Undo_Click(object sender, RoutedEventArgs e) => UndoRequested?.Invoke(this, EventArgs.Empty);
}
