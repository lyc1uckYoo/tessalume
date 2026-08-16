using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkInspectorView : UserControl
{
    private ThemeArtworkAdjustment _adjustment = new();
    private ThemeArtworkPlacementSpec _placement = new();
    private IReadOnlyDictionary<string, ThemeArtworkValueSource> _provenance =
        new Dictionary<string, ThemeArtworkValueSource>(StringComparer.Ordinal);
    private ArtworkRegion _region = ArtworkRegion.Hero;
    private ArtworkParameterGroup _group = ArtworkParameterGroup.Basic;
    private ArtworkParameter _selectedParameter = ArtworkParameter.Brightness;
    private ArtworkParameter? _activeInteraction;
    private bool _updating;
    private bool _responsiveCover;

    public ArtworkInspectorView()
    {
        InitializeComponent();
        BlendModeComboBox.SelectedIndex = 0;
        SetGroup(ArtworkParameterGroup.Composition);
    }

    internal event EventHandler<ArtworkParameterValueChangedEventArgs>? NumericValueChanged;

    internal event EventHandler<ArtworkParameterTextChangedEventArgs>? TextValueChanged;

    internal event EventHandler<ArtworkParameterEventArgs>? InteractionStarted;

    internal event EventHandler<ArtworkParameterEventArgs>? InteractionCompleted;

    internal event EventHandler<ArtworkParameterGroupEventArgs>? GroupChanged;

    internal event EventHandler? ResetParameterRequested;

    internal event EventHandler? ResetGroupRequested;

    internal event EventHandler? ResetRegionRequested;

    internal event EventHandler? RestoreOriginalBaselineRequested;

    internal event EventHandler<ArtworkPlacementChangedEventArgs>? PlacementChanged;

    internal event EventHandler? ChooseImageRequested;

    internal event EventHandler? ClearImageRequested;

    internal ArtworkParameter SelectedParameter => _selectedParameter;

    internal ArtworkParameterGroup SelectedGroup => _group;

    internal void SetAdjustment(ThemeArtworkAdjustment adjustment)
    {
        _adjustment = (adjustment ?? new ThemeArtworkAdjustment()).Normalize();
        _updating = true;
        try
        {
            SetSlider(BrightnessSlider, BrightnessValue, _adjustment.Brightness, ArtworkParameter.Brightness);
            SetSlider(ContrastSlider, ContrastValue, _adjustment.Contrast, ArtworkParameter.Contrast);
            SetSlider(SaturationSlider, SaturationValue, _adjustment.Saturation, ArtworkParameter.Saturation);
            SetSlider(OpacitySlider, OpacityValue, _adjustment.Opacity, ArtworkParameter.Opacity);
            SetSlider(ZoomSlider, ZoomValue, _adjustment.Zoom, ArtworkParameter.Zoom);
            SetSlider(OffsetXSlider, OffsetXValue, _adjustment.OffsetX, ArtworkParameter.OffsetX);
            SetSlider(OffsetYSlider, OffsetYValue, _adjustment.OffsetY, ArtworkParameter.OffsetY);
            SetSlider(GrayscaleSlider, GrayscaleValue, _adjustment.Grayscale, ArtworkParameter.Grayscale);
            SetSlider(HueRotationSlider, HueRotationValue, _adjustment.HueRotation, ArtworkParameter.HueRotation);
            SetSlider(BlurSlider, BlurValue, _adjustment.Blur, ArtworkParameter.Blur);
            SetSlider(
                OverlayOpacitySlider,
                OverlayOpacityValue,
                _adjustment.OverlayOpacity,
                ArtworkParameter.OverlayOpacity);
            SetSlider(
                GradientStrengthSlider,
                GradientStrengthValue,
                _adjustment.GradientVeil.Enabled
                    ? _adjustment.GradientVeil.Strength
                    : _adjustment.GradientStrength,
                ArtworkParameter.GradientStrength);
            SetSlider(VignetteSlider, VignetteValue, _adjustment.Vignette, ArtworkParameter.Vignette);
            OverlayColorValue.Text = _adjustment.OverlayColor;
            foreach (var candidate in BlendModeComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(
                        candidate.Tag as string,
                        _adjustment.BlendMode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    BlendModeComboBox.SelectedItem = candidate;
                    break;
                }
            }
            ClearImageButton.IsEnabled = true;
        }
        finally
        {
            _updating = false;
        }
    }

    internal void SetGroup(ArtworkParameterGroup group)
    {
        if (group == ArtworkParameterGroup.Mask && _region != ArtworkRegion.Chat)
        {
            group = ArtworkParameterGroup.Composition;
        }
        _group = group;
        SelectParameter(group switch
        {
            ArtworkParameterGroup.Composition => _responsiveCover
                ? ArtworkParameter.PlacementX
                : ArtworkParameter.PlacementSize,
            ArtworkParameterGroup.Effects => ArtworkParameter.OverlayOpacity,
            ArtworkParameterGroup.Mask => ArtworkParameter.GradientStrength,
            _ => ArtworkParameter.Brightness,
        });
        BasicPanel.Visibility = group == ArtworkParameterGroup.Basic
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompositionPanel.Visibility = group == ArtworkParameterGroup.Composition
            ? Visibility.Visible
            : Visibility.Collapsed;
        EffectsPanel.Visibility = group == ArtworkParameterGroup.Effects
            ? Visibility.Visible
            : Visibility.Collapsed;
        MaskPanel.Visibility = group == ArtworkParameterGroup.Mask
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetGroupButton(BasicGroupButton, group == ArtworkParameterGroup.Basic);
        SetGroupButton(CompositionGroupButton, group == ArtworkParameterGroup.Composition);
        SetGroupButton(EffectsGroupButton, group == ArtworkParameterGroup.Effects);
        SetGroupButton(MaskGroupButton, group == ArtworkParameterGroup.Mask);
        ResetGroupButton.Content = $"恢复{group switch
        {
            ArtworkParameterGroup.Composition => "构图",
            ArtworkParameterGroup.Effects => "效果",
            ArtworkParameterGroup.Mask => "遮罩",
            _ => "明暗",
        }}组";
        SelectedParameterText.Text = $"当前参数：{GetParameterName(_selectedParameter)}";
    }

    internal void SetRegion(ArtworkRegion region)
    {
        _region = region;
        var chat = region == ArtworkRegion.Chat;
        MaskGroupButton.Visibility = chat ? Visibility.Visible : Visibility.Collapsed;
        ParameterTabs.Columns = chat ? 4 : 3;
        if (!chat && _group == ArtworkParameterGroup.Mask)
        {
            SetGroup(ArtworkParameterGroup.Composition);
        }
    }

    internal void SetTargetSummary(string regionName, string modeName)
    {
        ResetRegionButton.Content = $"恢复{regionName} · {modeName}到主题推荐值";
        AutomationProperties.SetName(
            ResetRegionButton,
            $"恢复{regionName}{modeName}到主题推荐值并保留图片来源");
    }

    internal void SetFixedWidthComposition(bool fixedWidth, bool responsiveCover = false)
    {
        _responsiveCover = responsiveCover;
        var sizeVisibility = responsiveCover ? Visibility.Collapsed : Visibility.Visible;
        SizeRowLabel.Visibility = sizeVisibility;
        SizeWidthLabel.Visibility = sizeVisibility;
        SizeHeightLabel.Visibility = sizeVisibility;
        SizeWidthValue.Visibility = sizeVisibility;
        SizeHeightValue.Visibility = sizeVisibility;
        SizeWidthValue.IsEnabled = !responsiveCover;
        SizeHeightValue.IsEnabled = !fixedWidth && !responsiveCover;
        SizeWidthLabel.Text = responsiveCover ? "模式（自适应）" : "宽度 / 模式";
        SizeHeightLabel.Text = fixedWidth || responsiveCover ? "高度（自动）" : "高度";
        SizeWidthValue.ToolTip = responsiveCover
            ? "首页横幅与聊天背景固定使用等比填满；拖动画面调整焦点，滚轮调整缩放"
            : "选择或输入背景宽度";
        SizeHeightValue.ToolTip = responsiveCover
            ? "自适应填满始终保持原图比例，不单独设置高度"
            : fixedWidth
            ? "左栏宽度固定，高度始终按原图比例自动计算"
            : "选择或输入背景高度";
        AutomationProperties.SetHelpText(
            SizeWidthValue,
            responsiveCover
                ? "首页和聊天使用响应式等比填满，宽高不会独立拉伸"
                : "选择或输入背景宽度");
        AutomationProperties.SetHelpText(
            SizeHeightValue,
            responsiveCover
                ? "响应式构图的高度由原图比例和当前窗口自动计算"
                : fixedWidth
                ? "左栏使用固定宽度构图，高度保持原图比例"
                : "选择或输入背景高度");
        if (responsiveCover &&
            _group == ArtworkParameterGroup.Composition &&
            _selectedParameter == ArtworkParameter.PlacementSize)
        {
            SelectParameter(ArtworkParameter.PlacementX);
        }
    }

    internal void SetSourceSummary(string summary, bool hasLocalImage)
    {
        SourceBadgeText.Text = summary;
        ClearImageButton.IsEnabled = true;
        ClearImageButton.Content = hasLocalImage ? "切回主题原图" : "正在使用主题原图";
    }

    private void Group_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string tag }) return;
        var group = tag switch
        {
            "composition" => ArtworkParameterGroup.Composition,
            "effects" => ArtworkParameterGroup.Effects,
            "mask" => ArtworkParameterGroup.Mask,
            _ => ArtworkParameterGroup.Basic,
        };
        if (_group == group) return;
        SetGroup(group);
        GroupChanged?.Invoke(this, new ArtworkParameterGroupEventArgs(group));
    }

    private void BlendModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || BlendModeComboBox.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        SelectParameter(ArtworkParameter.BlendMode);
        TextValueChanged?.Invoke(
            this,
            new ArtworkParameterTextChangedEventArgs(ArtworkParameter.BlendMode, value));
    }

    private void OverlayColorValue_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e) => CommitOverlayColor();

    private void OverlayColorValue_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitOverlayColor();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OverlayColorValue.Text = _adjustment.OverlayColor;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void CommitOverlayColor()
    {
        var value = OverlayColorValue.Text.Trim();
        if (value.Length != 7 || value[0] != '#' || !value[1..].All(Uri.IsHexDigit))
        {
            OverlayColorValue.Text = _adjustment.OverlayColor;
            return;
        }
        value = value.ToUpperInvariant();
        OverlayColorValue.Text = value;
        SelectParameter(ArtworkParameter.OverlayColor);
        TextValueChanged?.Invoke(
            this,
            new ArtworkParameterTextChangedEventArgs(ArtworkParameter.OverlayColor, value));
    }

    private void ResetParameter_Click(object sender, RoutedEventArgs e) =>
        ResetParameterRequested?.Invoke(this, EventArgs.Empty);

    private void ResetGroup_Click(object sender, RoutedEventArgs e) =>
        ResetGroupRequested?.Invoke(this, EventArgs.Empty);

    private void ResetRegion_Click(object sender, RoutedEventArgs e) =>
        ResetRegionRequested?.Invoke(this, EventArgs.Empty);

    private void RestoreOriginalBaseline_Click(object sender, RoutedEventArgs e) =>
        RestoreOriginalBaselineRequested?.Invoke(this, EventArgs.Empty);

    private void ChooseImage_Click(object sender, RoutedEventArgs e) =>
        ChooseImageRequested?.Invoke(this, EventArgs.Empty);

    private void ClearImage_Click(object sender, RoutedEventArgs e) =>
        ClearImageRequested?.Invoke(this, EventArgs.Empty);

    private void BeginInteraction(ArtworkParameter parameter)
    {
        SelectParameter(parameter);
        if (_activeInteraction == parameter) return;
        if (_activeInteraction is not null) EndActiveInteraction();
        _activeInteraction = parameter;
        InteractionStarted?.Invoke(this, new ArtworkParameterEventArgs(parameter));
    }

    private void EndActiveInteraction()
    {
        if (_activeInteraction is not { } parameter) return;
        _activeInteraction = null;
        InteractionCompleted?.Invoke(this, new ArtworkParameterEventArgs(parameter));
    }

    private void SelectParameter(ArtworkParameter parameter)
    {
        _selectedParameter = parameter;
        var name = GetParameterName(parameter);
        if (SelectedParameterText is not null)
        {
            SelectedParameterText.Text = $"当前参数：{name}";
        }
        if (ResetParameterButton is not null)
        {
            ResetParameterButton.Content = $"恢复{name}";
            AutomationProperties.SetName(ResetParameterButton, $"恢复当前参数{name}到主题推荐值");
        }
        RenderParameterOrigin();
    }

    private static void SetGroupButton(Button button, bool active)
    {
        button.Tag = active ? "active" : "inactive";
        AutomationProperties.SetItemStatus(button, active ? "当前选中" : "未选中");
    }

    private static string GetParameterName(ArtworkParameter parameter) => parameter switch
    {
        ArtworkParameter.Brightness => "亮度",
        ArtworkParameter.Contrast => "对比度",
        ArtworkParameter.Saturation => "饱和度",
        ArtworkParameter.Opacity => "透明度",
        ArtworkParameter.Zoom => "缩放",
        ArtworkParameter.OffsetX => "水平位置",
        ArtworkParameter.OffsetY => "垂直位置",
        ArtworkParameter.PlacementSize => "图片大小",
        ArtworkParameter.PlacementX => "水平位置",
        ArtworkParameter.PlacementY => "垂直位置",
        ArtworkParameter.Grayscale => "灰度",
        ArtworkParameter.HueRotation => "色相",
        ArtworkParameter.Blur => "柔化",
        ArtworkParameter.OverlayColor => "叠色颜色",
        ArtworkParameter.OverlayOpacity => "叠色强度",
        ArtworkParameter.GradientStrength => "聊天遮罩",
        ArtworkParameter.Vignette => "暗角",
        ArtworkParameter.BlendMode => "混合模式",
        ArtworkParameter.ReadabilityProtection => "文字可读性",
        _ => "参数",
    };
}
