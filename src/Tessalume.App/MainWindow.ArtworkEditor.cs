using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tessalume.App.Controls;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;
using Tessalume.Core.Updates;
using Microsoft.Win32;

namespace Tessalume.App;

public partial class MainWindow
{
    private const int MaxVisualHistoryEntries = 48;
    private ArtworkAdjustmentGroup _visualAdjustmentGroup = ArtworkAdjustmentGroup.Basic;
    private string _visualAdjustmentRegion = "hero";
    private readonly Dictionary<string, List<ThemeVisualSettings>> _visualUndoHistory =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ThemeVisualSettings>> _visualRedoHistory =
        new(StringComparer.OrdinalIgnoreCase);
    private ThemeArtworkAdjustment? _visualAdjustmentClipboard;
    private string? _visualHistoryCoalesceKey;
    private string? _visualHistoryCoalesceThemeId;
    private bool _visualOriginalPreviewActive;
    private int _visualOriginalPreviewVersion;

    private void ArtworkAdjustmentEditor_AdjustmentChanged(
        object? sender,
        ArtworkAdjustmentChangedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        var settings = GetVisualSettings(themeId);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        var adjustment = e.Region switch
        {
            "hero" => mode.Hero,
            "sidebar" => mode.Sidebar,
            "chat" => mode.Chat,
            _ => null,
        };
        if (adjustment is null) return;

        adjustment = e.Property switch
        {
            "brightness" => adjustment with { Brightness = e.Value },
            "contrast" => adjustment with { Contrast = e.Value },
            "saturation" => adjustment with { Saturation = e.Value },
            "opacity" => adjustment with { Opacity = e.Value },
            "zoom" => adjustment with { Zoom = e.Value },
            "offsetX" => adjustment with { OffsetX = e.Value },
            "offsetY" => adjustment with { OffsetY = e.Value },
            "grayscale" => adjustment with { Grayscale = e.Value },
            "hueRotation" => adjustment with { HueRotation = e.Value },
            "blur" => adjustment with { Blur = e.Value },
            "overlayOpacity" => adjustment with { OverlayOpacity = e.Value },
            "gradientStrength" => adjustment with { GradientStrength = e.Value },
            "vignette" => adjustment with { Vignette = e.Value },
            _ => adjustment,
        };
        mode = e.Region switch
        {
            "hero" => mode with { Hero = adjustment },
            "sidebar" => mode with { Sidebar = adjustment },
            "chat" => mode with { Chat = adjustment },
            _ => mode,
        };
        var replacement = (_editingVisualDarkMode
            ? settings with { Dark = mode }
            : settings with { Light = mode }).Normalize();
        if (replacement == settings.Normalize()) return;

        RecordVisualUndo(
            themeId,
            settings,
            $"{(_editingVisualDarkMode ? "dark" : "light")}:{e.Region}:{e.Property}");
        _themeVisualSettings[themeId] = replacement;
        UpdateVisualEditorActions();
        ScheduleVisualSettingsUpdate();
    }

    private void ArtworkAdjustmentEditor_OptionChanged(
        object? sender,
        ArtworkAdjustmentOptionChangedEventArgs e)
    {
        UpdateRegionAdjustment(
            e.Region,
            adjustment => e.Property switch
            {
                "overlayColor" => adjustment with { OverlayColor = e.Value },
                "blendMode" => adjustment with { BlendMode = e.Value },
                "readabilityProtection" => adjustment with
                {
                    ReadabilityProtection = string.Equals(
                        e.Value,
                        "true",
                        StringComparison.OrdinalIgnoreCase),
                },
                _ => adjustment,
            },
            e.Property);
    }

    private async void ArtworkAdjustmentEditor_ChooseImageRequested(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not ArtworkAdjustmentEditor { RegionKey: { Length: > 0 } region }) return;
        var dialog = new OpenFileDialog
        {
            Title = $"为{GetRegionDisplayName(region)}选择本地图片",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var storedPath = await _personalImageStore.ImportAsync(
                dialog.FileName,
                _personalizationCancellation.Token);
            UpdateRegionAdjustment(
                region,
                adjustment => adjustment with { CustomImagePath = storedPath },
                "customImagePath");
            UpdateVisualAdjustmentControls();
            ShowToast($"已为{GetRegionDisplayName(region)}使用本地图片");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowProductMessage("无法使用这张图片", exception.Message, ProductDialogKind.Error);
        }
    }

    private void ArtworkAdjustmentEditor_ClearImageRequested(object sender, RoutedEventArgs e)
    {
        if (sender is not ArtworkAdjustmentEditor { RegionKey: { Length: > 0 } region }) return;
        UpdateRegionAdjustment(
            region,
            adjustment => adjustment with { CustomImagePath = null },
            "customImagePath");
        UpdateVisualAdjustmentControls();
        ShowToast($"{GetRegionDisplayName(region)}已恢复主题原图");
    }

    private void UpdateRegionAdjustment(
        string region,
        Func<ThemeArtworkAdjustment, ThemeArtworkAdjustment> update,
        string? coalesceProperty = null)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var settings = GetVisualSettings(themeId);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        var current = GetRegionAdjustment(mode, region);
        var replacementMode = SetRegionAdjustment(mode, region, update(current).Normalize());
        var replacement = (_editingVisualDarkMode
            ? settings with { Dark = replacementMode }
            : settings with { Light = replacementMode }).Normalize();
        if (replacement == settings.Normalize()) return;
        var coalesceKey = coalesceProperty is null
            ? null
            : $"{(_editingVisualDarkMode ? "dark" : "light")}:{region}:{coalesceProperty}";
        RecordVisualUndo(themeId, settings, coalesceKey);
        _themeVisualSettings[themeId] = replacement;
        UpdateVisualEditorActions();
        ScheduleVisualSettingsUpdate();
    }

    private void ArtworkAdjustmentEditor_ResetRequested(object sender, RoutedEventArgs e)
    {
        if (sender is not ArtworkAdjustmentEditor { RegionKey: { Length: > 0 } region }) return;
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        var settings = GetVisualSettings(themeId);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        mode = region switch
        {
            "hero" => mode with { Hero = new ThemeArtworkAdjustment() },
            "sidebar" => mode with { Sidebar = new ThemeArtworkAdjustment() },
            "chat" => mode with { Chat = new ThemeArtworkAdjustment() },
            _ => mode,
        };
        var replacement = (_editingVisualDarkMode
            ? settings with { Dark = mode }
            : settings with { Light = mode }).Normalize();
        if (replacement == settings.Normalize()) return;

        RecordVisualUndo(themeId, settings);
        _themeVisualSettings[themeId] = replacement;
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
    }

    private void ArtworkAdjustmentEditor_CopyRequested(object sender, RoutedEventArgs e)
    {
        if (sender is not ArtworkAdjustmentEditor { RegionKey: { Length: > 0 } region }) return;
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var settings = GetVisualSettings(themeId);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        _visualAdjustmentClipboard = GetRegionAdjustment(mode, region).Normalize();
        UpdateVisualEditorActions();
        ShowToast($"已复制 {GetRegionDisplayName(region)} 的全部参数");
    }

    private void ArtworkAdjustmentEditor_PasteRequested(object sender, RoutedEventArgs e)
    {
        if (_visualAdjustmentClipboard is null ||
            sender is not ArtworkAdjustmentEditor { RegionKey: { Length: > 0 } region }) return;
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        var settings = GetVisualSettings(themeId);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        var replacementMode = SetRegionAdjustment(mode, region, _visualAdjustmentClipboard);
        var replacement = (_editingVisualDarkMode
            ? settings with { Dark = replacementMode }
            : settings with { Light = replacementMode }).Normalize();
        if (replacement == settings.Normalize()) return;

        RecordVisualUndo(themeId, settings);
        _themeVisualSettings[themeId] = replacement;
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
        ShowToast($"已粘贴到 {GetRegionDisplayName(region)}");
    }

    private void VisualAdjustmentGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string group }) return;
        _visualAdjustmentGroup = group switch
        {
            "composition" => ArtworkAdjustmentGroup.Composition,
            "effects" => ArtworkAdjustmentGroup.Effects,
            _ => ArtworkAdjustmentGroup.Basic,
        };
        UpdateVisualAdjustmentGroup();
    }

    private void VisualAdjustmentRegion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string region }) return;
        _visualAdjustmentRegion = region switch
        {
            "sidebar" => "sidebar",
            "chat" => "chat",
            _ => "hero",
        };
        UpdateVisualAdjustmentRegion();
    }

    private void ResetAllVisualSettings_Click(object sender, RoutedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var settings = GetVisualSettings(themeId);
        var replacement = new ThemeVisualSettings();
        if (settings.Normalize() == replacement) return;
        RecordVisualUndo(themeId, settings);
        _themeVisualSettings[themeId] = replacement;
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
    }

    private void VisualUndo_Click(object sender, RoutedEventArgs e) => UndoVisualSettings();

    private void VisualRedo_Click(object sender, RoutedEventArgs e) => RedoVisualSettings();

    private void UndoVisualSettings()
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId ||
            !_visualUndoHistory.TryGetValue(themeId, out var undo) || undo.Count == 0) return;

        var current = GetVisualSettings(themeId);
        var previous = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        AddVisualHistoryEntry(GetVisualHistory(_visualRedoHistory, themeId), current);
        _themeVisualSettings[themeId] = previous;
        ResetVisualHistoryCoalescing();
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
        ShowToast("已撤销上一次图像修改");
    }

    private void RedoVisualSettings()
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId ||
            !_visualRedoHistory.TryGetValue(themeId, out var redo) || redo.Count == 0) return;

        var current = GetVisualSettings(themeId);
        var next = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        AddVisualHistoryEntry(GetVisualHistory(_visualUndoHistory, themeId), current);
        _themeVisualSettings[themeId] = next;
        ResetVisualHistoryCoalescing();
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
        ShowToast("已重做图像修改");
    }

    private void CopyVisualMode_Click(object sender, RoutedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var settings = GetVisualSettings(themeId);
        var replacement = _editingVisualDarkMode
            ? settings with { Light = settings.Dark }
            : settings with { Dark = settings.Light };
        replacement = replacement.Normalize();
        if (replacement == settings.Normalize())
        {
            ShowToast("另一显示模式已经使用相同参数");
            return;
        }

        RecordVisualUndo(themeId, settings);
        _themeVisualSettings[themeId] = replacement;
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
        ShowToast(_editingVisualDarkMode ? "已复制到亮色参数" : "已复制到暗色参数");
    }

}
