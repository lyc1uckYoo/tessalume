using System.Globalization;
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
    private string _valueBeforeEdit = string.Empty;

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
            ArtworkParameterGroup.Composition => ArtworkParameter.PlacementSize,
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

    internal void SetFixedWidthComposition(bool fixedWidth)
    {
        SizeHeightValue.IsEnabled = !fixedWidth;
        SizeHeightLabel.Text = fixedWidth ? "高度（自动）" : "高度";
        SizeHeightValue.ToolTip = fixedWidth
            ? "左栏宽度固定，高度始终按原图比例自动计算"
            : "选择或输入背景高度";
        AutomationProperties.SetHelpText(
            SizeHeightValue,
            fixedWidth
                ? "左栏使用固定宽度构图，高度保持原图比例"
                : "选择或输入背景高度");
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

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || sender is not Slider { Tag: string tag } slider ||
            !TryGetParameter(tag, out var parameter)) return;
        SelectParameter(parameter);
        if (FindValueEditor(parameter) is { } editor)
        {
            editor.Text = FormatValue(parameter, slider.Value);
        }
        NumericValueChanged?.Invoke(
            this,
            new ArtworkParameterValueChangedEventArgs(parameter, slider.Value));
    }

    private void Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider { Tag: string tag } && TryGetParameter(tag, out var parameter))
        {
            BeginInteraction(parameter);
        }
    }

    private void Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        EndActiveInteraction();

    private void Slider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Released) EndActiveInteraction();
    }

    private void Slider_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is Slider { Tag: string tag } && TryGetParameter(tag, out var parameter))
        {
            BeginInteraction(parameter);
        }
    }

    private void Slider_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        EndActiveInteraction();

    private void ValueEditor_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } editor ||
            !TryGetParameter(tag, out var parameter)) return;
        _valueBeforeEdit = editor.Text;
        editor.SelectAll();
        BeginInteraction(parameter);
    }

    private void ValueEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox editor) CommitValueEditor(editor);
        EndActiveInteraction();
    }

    private void ValueEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox editor) return;
        if (e.Key == Key.Enter)
        {
            CommitValueEditor(editor);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            editor.Text = _valueBeforeEdit;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down &&
                 editor.Tag is string tag &&
                 TryGetParameter(tag, out var parameter) &&
                 FindSlider(parameter) is { } slider)
        {
            var step = parameter == ArtworkParameter.Blur ? 0.5 : 1d;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) step *= 10;
            slider.Value = Math.Clamp(
                slider.Value + (e.Key == Key.Up ? step : -step),
                slider.Minimum,
                slider.Maximum);
            e.Handled = true;
        }
    }

    private void CommitValueEditor(TextBox editor)
    {
        if (editor.Tag is not string tag ||
            !TryGetParameter(tag, out var parameter) ||
            FindSlider(parameter) is not { } slider) return;
        var text = editor.Text
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("°", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (!double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var value) &&
            !double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) ||
            !double.IsFinite(value))
        {
            editor.Text = FormatValue(parameter, slider.Value);
            return;
        }
        slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        editor.Text = FormatValue(parameter, slider.Value);
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

    private static void SetSlider(
        Slider slider,
        TextBox editor,
        double value,
        ArtworkParameter parameter)
    {
        slider.Value = value;
        editor.Text = FormatValue(parameter, value);
    }

    private Slider? FindSlider(ArtworkParameter parameter) => parameter switch
    {
        ArtworkParameter.Brightness => BrightnessSlider,
        ArtworkParameter.Contrast => ContrastSlider,
        ArtworkParameter.Saturation => SaturationSlider,
        ArtworkParameter.Opacity => OpacitySlider,
        ArtworkParameter.Zoom => ZoomSlider,
        ArtworkParameter.OffsetX => OffsetXSlider,
        ArtworkParameter.OffsetY => OffsetYSlider,
        ArtworkParameter.Grayscale => GrayscaleSlider,
        ArtworkParameter.HueRotation => HueRotationSlider,
        ArtworkParameter.Blur => BlurSlider,
        ArtworkParameter.OverlayOpacity => OverlayOpacitySlider,
        ArtworkParameter.GradientStrength => GradientStrengthSlider,
        ArtworkParameter.Vignette => VignetteSlider,
        _ => null,
    };

    private TextBox? FindValueEditor(ArtworkParameter parameter) => parameter switch
    {
        ArtworkParameter.Brightness => BrightnessValue,
        ArtworkParameter.Contrast => ContrastValue,
        ArtworkParameter.Saturation => SaturationValue,
        ArtworkParameter.Opacity => OpacityValue,
        ArtworkParameter.Zoom => ZoomValue,
        ArtworkParameter.OffsetX => OffsetXValue,
        ArtworkParameter.OffsetY => OffsetYValue,
        ArtworkParameter.Grayscale => GrayscaleValue,
        ArtworkParameter.HueRotation => HueRotationValue,
        ArtworkParameter.Blur => BlurValue,
        ArtworkParameter.OverlayOpacity => OverlayOpacityValue,
        ArtworkParameter.GradientStrength => GradientStrengthValue,
        ArtworkParameter.Vignette => VignetteValue,
        _ => null,
    };

    private static bool TryGetParameter(string tag, out ArtworkParameter parameter)
    {
        parameter = tag switch
        {
            "brightness" => ArtworkParameter.Brightness,
            "contrast" => ArtworkParameter.Contrast,
            "saturation" => ArtworkParameter.Saturation,
            "opacity" => ArtworkParameter.Opacity,
            "zoom" => ArtworkParameter.Zoom,
            "offsetX" => ArtworkParameter.OffsetX,
            "offsetY" => ArtworkParameter.OffsetY,
            "grayscale" => ArtworkParameter.Grayscale,
            "hueRotation" => ArtworkParameter.HueRotation,
            "blur" => ArtworkParameter.Blur,
            "overlayOpacity" => ArtworkParameter.OverlayOpacity,
            "gradientStrength" => ArtworkParameter.GradientStrength,
            "vignette" => ArtworkParameter.Vignette,
            _ => default,
        };
        return tag is "brightness" or "contrast" or "saturation" or "opacity" or
            "zoom" or "offsetX" or "offsetY" or "grayscale" or "hueRotation" or
            "blur" or "overlayOpacity" or "gradientStrength" or "vignette";
    }

    private static string FormatValue(ArtworkParameter parameter, double value) => parameter switch
    {
        ArtworkParameter.OffsetX or ArtworkParameter.OffsetY or ArtworkParameter.Blur =>
            $"{value:0.#} px",
        ArtworkParameter.HueRotation => $"{value:0.#}°",
        _ => $"{value:0.#}%",
    };

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
