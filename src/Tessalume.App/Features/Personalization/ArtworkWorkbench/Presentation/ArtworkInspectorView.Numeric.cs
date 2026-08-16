using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkInspectorView
{
    private readonly Dictionary<TextBox, (double RawValue, string DisplayText)> _numericEditorStates = [];
    private string _valueBeforeEdit = string.Empty;

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || sender is not Slider { Tag: string tag } slider ||
            !TryGetParameter(tag, out var parameter)) return;
        SelectParameter(parameter);
        if (FindValueEditor(parameter) is { } editor)
        {
            SetNumericEditorDisplay(editor, parameter, slider.Value);
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
        double value;
        if (_numericEditorStates.TryGetValue(editor, out var state) &&
            string.Equals(editor.Text.Trim(), state.DisplayText, StringComparison.Ordinal))
        {
            value = state.RawValue;
        }
        else
        {
            var text = editor.Text
                .Replace("%", string.Empty, StringComparison.Ordinal)
                .Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("°", string.Empty, StringComparison.Ordinal)
                .Trim();
            if (!double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out value) &&
                !double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) ||
                !double.IsFinite(value))
            {
                SetNumericEditorDisplay(editor, parameter, slider.Value);
                return;
            }
        }
        slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        SetNumericEditorDisplay(editor, parameter, slider.Value);
    }

    private void SetSlider(
        Slider slider,
        TextBox editor,
        double value,
        ArtworkParameter parameter)
    {
        if (editor.IsKeyboardFocusWithin || _activeInteraction == parameter) return;
        slider.Value = value;
        SetNumericEditorDisplay(editor, parameter, value);
    }

    private void SetNumericEditorDisplay(
        TextBox editor,
        ArtworkParameter parameter,
        double value)
    {
        var display = FormatValue(parameter, value);
        editor.Text = display;
        _numericEditorStates[editor] = (value, display);
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
            ArtworkPresentationFormatter.Pixels(value, spaced: true),
        ArtworkParameter.HueRotation => $"{ArtworkPresentationFormatter.Number(value)}°",
        _ => ArtworkPresentationFormatter.Percent(value),
    };
}
