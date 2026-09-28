using System.Windows.Controls;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

namespace Tessalume.App.Features.ArtworkLibrary.Presentation;

public partial class ArtworkLibraryView
{
    private bool IsTargetSupported() => _themes.FirstOrDefault(theme =>
        theme.Manifest.Id == SelectedThemeId) is { } package &&
        ArtworkImageSourceResolver.IsRegionSupported(package, SelectedRegion, SelectedMode);

    private void RenderSupportedRegions()
    {
        var package = _themes.FirstOrDefault(theme => theme.Manifest.Id == SelectedThemeId);
        for (var index = 0; index < RegionSelector.Items.Count; index++)
        {
            if (RegionSelector.Items[index] is not ComboBoxItem option) continue;
            var supported = package is not null && ArtworkImageSourceResolver.IsRegionSupported(
                package, (ArtworkRegion)index, SelectedMode);
            option.IsEnabled = supported;
            option.ToolTip = supported ? null : "这个主题没有此位置的图片";
        }
    }

    private ArtworkImageSource? CurrentTargetSource()
    {
        var package = _themes.FirstOrDefault(theme => theme.Manifest.Id == SelectedThemeId);
        if (package is null || _service is null || !_settings.TryGetValue(SelectedThemeId, out var settings)) return null;
        return _service.ResolveTargetSource(package, SelectedRegion, SelectedMode,
            ArtworkSettingsAccessor.GetAdjustment(settings, SelectedMode, SelectedRegion));
    }

    private ArtworkLibraryItem? FindCurrentTargetItem()
    {
        var source = CurrentTargetSource();
        if (source is null) return null;
        return _items.FirstOrDefault(item => string.Equals(item.AbsolutePath, source.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            ?? _items.FirstOrDefault(item => item.Usages.Any(usage => usage.ThemeId == SelectedThemeId &&
                usage.Region == SelectedRegion && usage.Mode == SelectedMode));
    }

    private bool IsCurrentTargetItem(ArtworkLibraryItem item) => FindCurrentTargetItem()?.Id == item.Id ||
        string.Equals(CurrentTargetSource()?.AbsolutePath, item.AbsolutePath, StringComparison.OrdinalIgnoreCase);

    private void RenderCurrentTargetImage()
    {
        if (CurrentImageText is null) return;
        var source = CurrentTargetSource();
        var item = FindCurrentTargetItem();
        CurrentImageText.Text = source is null
            ? IsTargetSupported() ? "原图暂不可用" : "此主题没有这个图片位置"
            : source.SourceKind == ArtworkImageSourceKind.ThemeOriginal
                ? "主题原图"
                : item?.Name ?? "已应用的导入图片";
        CurrentImageText.ToolTip = source?.AbsolutePath;
        RestoreButton.ToolTip = $"恢复{(ThemeSelector.SelectedItem as NamedChoice)?.Name} · {RegionName(SelectedRegion)} · {ModeName(SelectedMode)}的原图与推荐构图。";
    }
}
