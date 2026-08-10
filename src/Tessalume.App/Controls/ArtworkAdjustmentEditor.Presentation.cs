using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Tessalume.App.Features.Personalization;

namespace Tessalume.App.Controls;

public partial class ArtworkAdjustmentEditor
{
    private static void OnTitleChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e) =>
        ((ArtworkAdjustmentEditor)dependencyObject).UpdateAutomationNames();

    private void UpdateAutomationNames()
    {
        if (BrightnessSlider is null || string.IsNullOrWhiteSpace(Title)) return;
        AutomationProperties.SetName(ResetButton, $"重置{Title}当前参数组");
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
        AutomationProperties.SetName(OverlayOpacitySlider, $"{Title}叠色强度");
        AutomationProperties.SetName(GradientStrengthSlider, $"{Title}渐变遮罩");
        AutomationProperties.SetName(VignetteSlider, $"{Title}暗角");
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
        AutomationProperties.SetName(OverlayOpacityValue, $"精确输入{Title}叠色强度");
        AutomationProperties.SetName(GradientStrengthValue, $"精确输入{Title}渐变遮罩");
        AutomationProperties.SetName(VignetteValue, $"精确输入{Title}暗角");
    }

    private void UpdateImageStatus()
    {
        if (CustomImageStatusText is null) return;
        var source = string.IsNullOrWhiteSpace(_currentAdjustment.CustomImagePath)
            ? "主题原图"
            : $"本地图片 · {Path.GetFileName(_currentAdjustment.CustomImagePath)}";
        CustomImageStatusText.Text = $"{(_darkMode ? "暗色" : "亮色")}模式 · {source}";
    }

    private void UpdateGroupPresentation()
    {
        if (ResetButtonText is null) return;
        var (title, hint, resetLabel) = _visibleGroup switch
        {
            ArtworkAdjustmentGroup.Composition => (
                "构图位置",
                "调整缩放和位置，只影响当前区域与当前亮暗模式。",
                "重置构图"),
            ArtworkAdjustmentGroup.Effects => (
                "氛围效果",
                "叠色、遮罩与可读性保护；不会改写主题图片。",
                "重置效果"),
            _ => (
                "基础调节",
                "改善明暗与色彩；建议一次只调整一个参数。",
                "重置基础"),
        };
        GroupTitleText.Text = title;
        GroupHintText.Text = hint;
        ResetButtonText.Text = resetLabel;
        ResetButton.IsEnabled = true;
        ResetButton.ToolTip = $"只{resetLabel}，不会影响另外两组；可使用撤销恢复";
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
        SetValueText(OverlayOpacityValue, $"{OverlayOpacitySlider.Value:0}%", force);
        SetValueText(GradientStrengthValue, $"{GradientStrengthSlider.Value:0}%", force);
        SetValueText(VignetteValue, $"{VignetteSlider.Value:0}%", force);
    }

    private static void SetValueText(TextBox textBox, string value, bool force)
    {
        if (force || !textBox.IsKeyboardFocusWithin) textBox.Text = value;
    }
}
