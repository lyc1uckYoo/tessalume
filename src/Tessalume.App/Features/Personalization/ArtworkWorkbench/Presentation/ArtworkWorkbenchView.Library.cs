using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using System.Windows.Controls;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkWorkbenchView
{
    private bool _renderingCardRegion;

    private void CardRegion_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_renderingCardRegion || CardRegionSelector?.SelectedIndex is not > 0 || !CanEdit()) return;
        var region = (ArtworkRegion)(CardRegionSelector.SelectedIndex + 2);
        if (_package is null || !ArtworkImageSourceResolver.IsRegionSupported(_package, region, _mode)) return;
        SelectEditingTarget(region, _mode);
    }

    private void RenderCardRegions(bool available)
    {
        _renderingCardRegion = true;
        try
        {
            var any = false;
            for (var index = 1; index < CardRegionSelector.Items.Count; index++)
            {
                var option = (ComboBoxItem)CardRegionSelector.Items[index];
                option.IsEnabled = available && _package is not null && ArtworkImageSourceResolver.IsRegionSupported(
                    _package, (ArtworkRegion)(index + 2), _mode);
                option.ToolTip = option.IsEnabled ? null : "这个主题没有此位置的图片";
                any |= option.IsEnabled;
            }
            CardRegionSelector.IsEnabled = any;
            CardRegionSelector.SelectedIndex = (int)_region >= 3 ? (int)_region - 2 : 0;
        }
        finally { _renderingCardRegion = false; }
    }

    private void Inspector_ChooseLibraryImageRequested(object? sender, EventArgs e)
    {
        if (!CanEdit()) return;
        ChooseLibraryImageRequested?.Invoke(
            this, new ArtworkChooseImageEventArgs(_themeId!, _mode, _region));
    }

    internal void SelectEditingTarget(ArtworkRegion region, ArtworkColorMode mode)
    {
        EndWheelGesture();
        _region = region;
        _mode = mode;
        if (region != ArtworkRegion.Chat && Inspector.SelectedGroup == ArtworkParameterGroup.Mask)
            _canvasViewMode = ArtworkCanvasViewMode.Result;
        UpdateResponsiveLayout(ActualWidth);
        RenderAll();
        QueuePreviewReload();
    }
}
