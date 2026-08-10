using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tessalume.App.Features.Personalization;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Controls;

public sealed class ArtworkAdjustmentChangedEventArgs(
    string region,
    string property,
    double value) : EventArgs
{
    public string Region { get; } = region;

    public string Property { get; } = property;

    public double Value { get; } = value;
}

public sealed class ArtworkAdjustmentOptionChangedEventArgs(
    string region,
    string property,
    string value) : EventArgs
{
    public string Region { get; } = region;

    public string Property { get; } = property;

    public string Value { get; } = value;
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
    private bool _darkMode;
    private ArtworkAdjustmentGroup _visibleGroup;
    private ThemeArtworkAdjustment _currentAdjustment = new();

    public ArtworkAdjustmentEditor()
    {
        InitializeComponent();
        _initialized = true;
        UpdateLabels();
        UpdateAutomationNames();
    }

    public event EventHandler<ArtworkAdjustmentChangedEventArgs>? AdjustmentChanged;

    public event EventHandler<ArtworkAdjustmentOptionChangedEventArgs>? OptionChanged;

    public event RoutedEventHandler? ResetRequested;

    public event RoutedEventHandler? CopyRequested;

    public event RoutedEventHandler? PasteRequested;

    public event RoutedEventHandler? ChooseImageRequested;

    public event RoutedEventHandler? ClearImageRequested;

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
        _currentAdjustment = adjustment.Normalize();
        _updating = true;
        try
        {
            BrightnessSlider.Value = _currentAdjustment.Brightness;
            ContrastSlider.Value = _currentAdjustment.Contrast;
            SaturationSlider.Value = _currentAdjustment.Saturation;
            OpacitySlider.Value = _currentAdjustment.Opacity;
            ZoomSlider.Value = _currentAdjustment.Zoom;
            OffsetXSlider.Value = _currentAdjustment.OffsetX;
            OffsetYSlider.Value = _currentAdjustment.OffsetY;
            GrayscaleSlider.Value = _currentAdjustment.Grayscale;
            HueRotationSlider.Value = _currentAdjustment.HueRotation;
            BlurSlider.Value = _currentAdjustment.Blur;
            OverlayOpacitySlider.Value = _currentAdjustment.OverlayOpacity;
            GradientStrengthSlider.Value = _currentAdjustment.GradientStrength;
            VignetteSlider.Value = _currentAdjustment.Vignette;
            BlendModeComboBox.SelectedValue = _currentAdjustment.BlendMode;
            OverlayColorTextBox.Text = _currentAdjustment.OverlayColor;
            ReadabilityCheckBox.IsChecked = _currentAdjustment.ReadabilityProtection;
            UpdateImageStatus();
            ClearImageButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentAdjustment.CustomImagePath);
            UpdateLabels();
            UpdateGroupPresentation();
        }
        finally
        {
            _updating = false;
        }
    }

    internal void ShowGroup(ArtworkAdjustmentGroup group)
    {
        _visibleGroup = group;
        BasicPanel.Visibility = group == ArtworkAdjustmentGroup.Basic
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompositionPanel.Visibility = group == ArtworkAdjustmentGroup.Composition
            ? Visibility.Visible
            : Visibility.Collapsed;
        EffectsPanel.Visibility = group == ArtworkAdjustmentGroup.Effects
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateGroupPresentation();
    }

    internal ArtworkAdjustmentGroup VisibleGroup => _visibleGroup;

    public void SetEditingMode(bool darkMode)
    {
        _darkMode = darkMode;
        UpdateImageStatus();
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

    private void ChooseImage_Click(object sender, RoutedEventArgs e) =>
        ChooseImageRequested?.Invoke(this, e);

    private void ClearImage_Click(object sender, RoutedEventArgs e) =>
        ClearImageRequested?.Invoke(this, e);

    private void BlendMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || BlendModeComboBox.SelectedValue is not string value) return;
        RaiseOptionChanged("blendMode", value);
    }

    private void Readability_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        RaiseOptionChanged(
            "readabilityProtection",
            ReadabilityCheckBox.IsChecked == true ? "true" : "false");
    }

    private void OverlayColor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        CommitOverlayColor();

    private void OverlayColor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitOverlayColor();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OverlayColorTextBox.Text = "#000000";
            OverlayColorTextBox.SelectAll();
            e.Handled = true;
        }
    }

    private void CommitOverlayColor()
    {
        var value = OverlayColorTextBox.Text.Trim().ToUpperInvariant();
        if (value.Length != 7 || value[0] != '#' || !value[1..].All(Uri.IsHexDigit))
        {
            value = "#000000";
        }
        OverlayColorTextBox.Text = value;
        if (!_updating) RaiseOptionChanged("overlayColor", value);
    }

    private void RaiseOptionChanged(string property, string value) =>
        OptionChanged?.Invoke(
            this,
            new ArtworkAdjustmentOptionChangedEventArgs(RegionKey, property, value));

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
        "overlayOpacity" => OverlayOpacitySlider,
        "gradientStrength" => GradientStrengthSlider,
        "vignette" => VignetteSlider,
        _ => null,
    };

}
