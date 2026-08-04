using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Controls;

public enum ArtworkAdjustmentGroup
{
    Basic,
    Composition,
    Effects,
}

public sealed class ArtworkAdjustmentChangedEventArgs(
    string region,
    string property,
    double value) : EventArgs
{
    public string Region { get; } = region;

    public string Property { get; } = property;

    public double Value { get; } = value;
}

public partial class ArtworkAdjustmentEditor : UserControl
{
    public static readonly DependencyProperty RegionKeyProperty = DependencyProperty.Register(
        nameof(RegionKey),
        typeof(string),
        typeof(ArtworkAdjustmentEditor),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(ArtworkAdjustmentEditor),
        new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(ArtworkAdjustmentEditor),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(
        nameof(IconData),
        typeof(Geometry),
        typeof(ArtworkAdjustmentEditor),
        new PropertyMetadata(Geometry.Empty));

    public static readonly DependencyProperty HeaderAccentProperty = DependencyProperty.Register(
        nameof(HeaderAccent),
        typeof(Brush),
        typeof(ArtworkAdjustmentEditor),
        new PropertyMetadata(Brushes.SlateBlue));

    public static readonly DependencyProperty HeaderAccentSoftProperty = DependencyProperty.Register(
        nameof(HeaderAccentSoft),
        typeof(Brush),
        typeof(ArtworkAdjustmentEditor),
        new PropertyMetadata(Brushes.Lavender));

    private bool _updating;
    private bool _initialized;

    public ArtworkAdjustmentEditor()
    {
        InitializeComponent();
        _initialized = true;
        UpdateLabels();
        UpdateAutomationNames();
    }

    public event EventHandler<ArtworkAdjustmentChangedEventArgs>? AdjustmentChanged;

    public event RoutedEventHandler? ResetRequested;

    public event RoutedEventHandler? CopyRequested;

    public event RoutedEventHandler? PasteRequested;

    public string RegionKey
    {
        get => (string)GetValue(RegionKeyProperty);
        set => SetValue(RegionKeyProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public Geometry IconData
    {
        get => (Geometry)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public Brush HeaderAccent
    {
        get => (Brush)GetValue(HeaderAccentProperty);
        set => SetValue(HeaderAccentProperty, value);
    }

    public Brush HeaderAccentSoft
    {
        get => (Brush)GetValue(HeaderAccentSoftProperty);
        set => SetValue(HeaderAccentSoftProperty, value);
    }

    public void SetAdjustment(ThemeArtworkAdjustment adjustment)
    {
        _updating = true;
        try
        {
            BrightnessSlider.Value = adjustment.Brightness;
            ContrastSlider.Value = adjustment.Contrast;
            SaturationSlider.Value = adjustment.Saturation;
            OpacitySlider.Value = adjustment.Opacity;
            ZoomSlider.Value = adjustment.Zoom;
            OffsetXSlider.Value = adjustment.OffsetX;
            OffsetYSlider.Value = adjustment.OffsetY;
            GrayscaleSlider.Value = adjustment.Grayscale;
            HueRotationSlider.Value = adjustment.HueRotation;
            BlurSlider.Value = adjustment.Blur;
            UpdateLabels();
        }
        finally
        {
            _updating = false;
        }
    }

    public void ShowGroup(ArtworkAdjustmentGroup group)
    {
        BasicPanel.Visibility = group == ArtworkAdjustmentGroup.Basic
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompositionPanel.Visibility = group == ArtworkAdjustmentGroup.Composition
            ? Visibility.Visible
            : Visibility.Collapsed;
        EffectsPanel.Visibility = group == ArtworkAdjustmentGroup.Effects
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void SetPasteAvailable(bool available) => PasteButton.IsEnabled = available;

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateLabels();
        if (_updating || sender is not Slider { Tag: string property }) return;
        AdjustmentChanged?.Invoke(
            this,
            new ArtworkAdjustmentChangedEventArgs(RegionKey, property, e.NewValue));
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke(this, e);

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, e);

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteRequested?.Invoke(this, e);

    private void ValueEditor_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        textBox.Dispatcher.BeginInvoke(textBox.SelectAll, DispatcherPriority.Input);
    }

    private void ValueEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox) CommitValueEditor(textBox);
    }

    private void ValueEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string property } textBox) return;
        if (e.Key == Key.Enter)
        {
            CommitValueEditor(textBox);
            textBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            UpdateLabels(force: true);
            textBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Up or Key.Down)) return;

        var slider = GetSlider(property);
        if (slider is null) return;
        var step = property == "blur" ? 0.5d : 1d;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) step *= 5d;
        slider.Value = Math.Clamp(
            slider.Value + (e.Key == Key.Up ? step : -step),
            slider.Minimum,
            slider.Maximum);
        UpdateLabels(force: true);
        textBox.SelectAll();
        e.Handled = true;
    }

    private void CommitValueEditor(TextBox textBox)
    {
        if (textBox.Tag is not string property || GetSlider(property) is not { } slider) return;
        var candidate = textBox.Text
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("°", string.Empty, StringComparison.Ordinal)
            .Trim();
        if ((!double.TryParse(candidate, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
             !double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) ||
            !double.IsFinite(value))
        {
            UpdateLabels(force: true);
            return;
        }

        slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        UpdateLabels(force: true);
    }

    private Slider? GetSlider(string property) => property switch
    {
        "brightness" => BrightnessSlider,
        "contrast" => ContrastSlider,
        "saturation" => SaturationSlider,
        "opacity" => OpacitySlider,
        "zoom" => ZoomSlider,
        "offsetX" => OffsetXSlider,
        "offsetY" => OffsetYSlider,
        "grayscale" => GrayscaleSlider,
        "hueRotation" => HueRotationSlider,
        "blur" => BlurSlider,
        _ => null,
    };

    private static void OnTitleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((ArtworkAdjustmentEditor)dependencyObject).UpdateAutomationNames();

    private void UpdateAutomationNames()
    {
        if (BrightnessSlider is null || string.IsNullOrWhiteSpace(Title)) return;
        AutomationProperties.SetName(ResetButton, $"恢复{Title}原图参数");
        AutomationProperties.SetName(CopyButton, $"复制{Title}参数");
        AutomationProperties.SetName(PasteButton, $"粘贴参数到{Title}");
        AutomationProperties.SetName(BrightnessSlider, $"{Title}亮度");
        AutomationProperties.SetName(ContrastSlider, $"{Title}对比度");
        AutomationProperties.SetName(SaturationSlider, $"{Title}饱和度");
        AutomationProperties.SetName(OpacitySlider, $"{Title}不透明度");
        AutomationProperties.SetName(ZoomSlider, $"{Title}缩放");
        AutomationProperties.SetName(OffsetXSlider, $"{Title}水平位置");
        AutomationProperties.SetName(OffsetYSlider, $"{Title}垂直位置");
        AutomationProperties.SetName(GrayscaleSlider, $"{Title}灰度");
        AutomationProperties.SetName(HueRotationSlider, $"{Title}色相旋转");
        AutomationProperties.SetName(BlurSlider, $"{Title}柔化");
        AutomationProperties.SetName(BrightnessValue, $"精确输入{Title}亮度");
        AutomationProperties.SetName(ContrastValue, $"精确输入{Title}对比度");
        AutomationProperties.SetName(SaturationValue, $"精确输入{Title}饱和度");
        AutomationProperties.SetName(OpacityValue, $"精确输入{Title}不透明度");
        AutomationProperties.SetName(ZoomValue, $"精确输入{Title}缩放");
        AutomationProperties.SetName(OffsetXValue, $"精确输入{Title}水平位置");
        AutomationProperties.SetName(OffsetYValue, $"精确输入{Title}垂直位置");
        AutomationProperties.SetName(GrayscaleValue, $"精确输入{Title}灰度");
        AutomationProperties.SetName(HueRotationValue, $"精确输入{Title}色相旋转");
        AutomationProperties.SetName(BlurValue, $"精确输入{Title}柔化");
    }

    private void UpdateLabels(bool force = false)
    {
        if (!_initialized) return;
        SetValueText(BrightnessValue, $"{BrightnessSlider.Value:0}%", force);
        SetValueText(ContrastValue, $"{ContrastSlider.Value:0}%", force);
        SetValueText(SaturationValue, $"{SaturationSlider.Value:0}%", force);
        SetValueText(OpacityValue, $"{OpacitySlider.Value:0}%", force);
        SetValueText(ZoomValue, $"{ZoomSlider.Value:0}%", force);
        SetValueText(OffsetXValue, $"{OffsetXSlider.Value:+0;-0;0} px", force);
        SetValueText(OffsetYValue, $"{OffsetYSlider.Value:+0;-0;0} px", force);
        SetValueText(GrayscaleValue, $"{GrayscaleSlider.Value:0}%", force);
        SetValueText(HueRotationValue, $"{HueRotationSlider.Value:+0;-0;0}°", force);
        SetValueText(BlurValue, $"{BlurSlider.Value:0.#} px", force);
    }

    private static void SetValueText(TextBox textBox, string value, bool force)
    {
        if (force || !textBox.IsKeyboardFocusWithin) textBox.Text = value;
    }
}
