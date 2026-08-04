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
    private readonly Dictionary<string, List<ThemeVisualSettings>> _visualUndoHistory =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ThemeVisualSettings>> _visualRedoHistory =
        new(StringComparer.OrdinalIgnoreCase);
    private ThemeArtworkAdjustment? _visualAdjustmentClipboard;
    private string? _visualHistoryCoalesceKey;
    private string? _visualHistoryCoalesceThemeId;
    private bool _visualOriginalPreviewActive;
    private int _visualOriginalPreviewVersion;

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        UpdateStartupButton();
        UpdateUpdateControls();
        if (_codexDarkMode is { } dark)
        {
            _editingVisualDarkMode = dark;
        }
        ShowInfoPage(RightPane.Settings);
        UpdateVisualAdjustmentControls();
        _ = RefreshCodexColorSchemeAsync();
    }

    private async void SettingsPreviousTheme_Click(object sender, RoutedEventArgs e) =>
        await ApplyRelativeSettingsThemeAsync(-1);

    private async void SettingsNextTheme_Click(object sender, RoutedEventArgs e) =>
        await ApplyRelativeSettingsThemeAsync(1);

    private async Task ApplyRelativeSettingsThemeAsync(int offset)
    {
        var candidates = GetQuickSwitchCandidates();
        if (candidates.Length == 0)
        {
            SetStatus("还没有可切换的有效主题");
            UpdateSettingsVisualHeader();
            return;
        }

        var currentId = _activeThemeId ?? GetVisualAdjustmentTheme()?.ThemeId;
        var currentIndex = Array.FindIndex(candidates, theme =>
            string.Equals(theme.ThemeId, currentId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = offset > 0 ? -1 : 0;
        }

        var nextIndex = (currentIndex + offset + candidates.Length) % candidates.Length;
        var nextTheme = candidates[nextIndex];
        SelectTheme(nextTheme);
        if (await ApplyThemeAsync(nextTheme))
        {
            UpdateVisualAdjustmentControls();
        }
    }

    private async void SettingsColorMode_Click(object sender, RoutedEventArgs e)
    {
        var dark = await ToggleCodexColorSchemeAsync();
        if (dark is null) return;

        _editingVisualDarkMode = dark.Value;
        UpdateVisualAdjustmentControls();
    }

    private ThemeVisualSettings GetVisualSettings(string themeId)
    {
        if (_themeVisualSettings.TryGetValue(themeId, out var settings))
        {
            return settings.Normalize();
        }

        settings = new ThemeVisualSettings();
        _themeVisualSettings[themeId] = settings;
        return settings;
    }

    private ThemeCardModel? GetVisualAdjustmentTheme()
    {
        if (!string.IsNullOrWhiteSpace(_activeThemeId))
        {
            var active = _themes.FirstOrDefault(theme =>
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase));
            if (active is not null) return active;
        }

        return _selectedTheme;
    }

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

    private async void SaveVisualPreset_Click(object sender, RoutedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var name = VisualPresetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) && VisualPresetComboBox.SelectedItem is ThemeArtworkPreset selected)
        {
            name = selected.Name;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"我的方案 {_artworkPresets.Count + 1}";
        }
        if (name.Length > 32) name = name[..32];

        var settings = GetVisualSettings(themeId);
        var preset = new ThemeArtworkPreset
        {
            Name = name,
            Settings = (_editingVisualDarkMode ? settings.Dark : settings.Light).Normalize(),
        };
        var existing = _artworkPresets
            .Select((item, index) => (item, index))
            .FirstOrDefault(pair => string.Equals(pair.item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing.item is not null)
        {
            _artworkPresets[existing.index] = preset;
        }
        else
        {
            if (_artworkPresets.Count >= 24)
            {
                ShowProductMessage("无法保存个人方案", "最多可以保存 24 个个人图像方案，请先删除不再使用的方案。", ProductDialogKind.Warning);
                return;
            }
            _artworkPresets.Add(preset);
        }

        VisualPresetComboBox.SelectedItem = preset;
        VisualPresetNameBox.Clear();
        await SavePreferencesAsync();
        UpdateVisualEditorActions();
        ShowToast($"已保存个人方案“{preset.Name}”");
    }

    private void ApplyVisualPreset_Click(object sender, RoutedEventArgs e)
    {
        var preset = ResolveSelectedVisualPreset();
        var theme = GetVisualAdjustmentTheme();
        if (preset is null || theme?.ThemeId is not { Length: > 0 } themeId) return;

        var settings = GetVisualSettings(themeId);
        var replacement = (_editingVisualDarkMode
            ? settings with { Dark = preset.Settings }
            : settings with { Light = preset.Settings }).Normalize();
        if (replacement == settings.Normalize())
        {
            ShowToast("当前模式已经使用这个方案");
            return;
        }

        RecordVisualUndo(themeId, settings);
        _themeVisualSettings[themeId] = replacement;
        UpdateVisualAdjustmentControls();
        ScheduleVisualSettingsUpdate();
        ShowToast($"已应用个人方案“{preset.Name}”");
    }

    private async void DeleteVisualPreset_Click(object sender, RoutedEventArgs e)
    {
        var preset = ResolveSelectedVisualPreset();
        if (preset is null) return;
        if (!ShowProductConfirmation(
                "删除个人图像方案",
                $"将删除“{preset.Name}”。已经应用到主题的参数不会改变。",
                "删除方案",
                dangerous: true)) return;

        _artworkPresets.Remove(preset);
        VisualPresetComboBox.SelectedItem = null;
        VisualPresetNameBox.Clear();
        await SavePreferencesAsync();
        UpdateVisualEditorActions();
        ShowToast("个人方案已删除");
    }

    private async void ImportVisualPreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 Tessalume 图像方案",
            Filter = "Tessalume 图像方案 (*.tessalume-look.json)|*.tessalume-look.json|JSON 文件 (*.json)|*.json",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var preset = await ThemeArtworkPresetExchange.ImportAsync(dialog.FileName);
            var existing = _artworkPresets
                .Select((item, index) => (item, index))
                .FirstOrDefault(pair => string.Equals(
                    pair.item.Name,
                    preset.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (existing.item is not null)
            {
                if (!ShowProductConfirmation(
                        "替换同名图像方案？",
                        $"本机已经保存了“{preset.Name}”。导入会替换这份方案，但不会改变已经应用到各主题的参数。",
                        "替换并导入")) return;
                _artworkPresets[existing.index] = preset;
            }
            else
            {
                if (_artworkPresets.Count >= 24)
                {
                    ShowProductMessage(
                        "无法导入图像方案",
                        "本机最多保存 24 个个人图像方案，请先删除不再使用的方案。",
                        ProductDialogKind.Warning);
                    return;
                }
                _artworkPresets.Add(preset);
            }

            VisualPresetComboBox.SelectedItem = preset;
            VisualPresetNameBox.Clear();
            await SavePreferencesAsync();
            UpdateVisualEditorActions();
            LocalLog.Write($"Imported artwork preset '{preset.Name}' from {dialog.FileName}.");
            ShowToast($"已导入图像方案“{preset.Name}”");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Importing an artwork preset failed.", exception);
            ShowProductMessage("无法导入图像方案", exception.Message, ProductDialogKind.Error);
        }
    }

    private async void ExportVisualPreset_Click(object sender, RoutedEventArgs e)
    {
        var preset = ResolveSelectedVisualPreset();
        if (preset is null) return;
        var safeName = string.Concat(preset.Name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Tessalume-look";
        var dialog = new SaveFileDialog
        {
            Title = "导出 Tessalume 图像方案",
            Filter = "Tessalume 图像方案 (*.tessalume-look.json)|*.tessalume-look.json|JSON 文件 (*.json)|*.json",
            DefaultExt = ThemeArtworkPresetExchange.FileExtension,
            AddExtension = true,
            FileName = safeName + ThemeArtworkPresetExchange.FileExtension,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await ThemeArtworkPresetExchange.ExportAsync(dialog.FileName, preset);
            LocalLog.Write($"Exported artwork preset '{preset.Name}' to {dialog.FileName}.");
            ShowToast($"已导出图像方案“{preset.Name}”");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Exporting an artwork preset failed.", exception);
            ShowProductMessage("无法导出图像方案", exception.Message, ProductDialogKind.Error);
        }
    }

    private void VisualPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateVisualEditorActions();

    private async void VisualOriginalPreview_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!VisualOriginalPreviewButton.IsEnabled) return;
        VisualOriginalPreviewButton.CaptureMouse();
        await SetOriginalPreviewAsync(showOriginal: true);
        e.Handled = true;
    }

    private async void VisualOriginalPreview_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        await SetOriginalPreviewAsync(showOriginal: false);
        VisualOriginalPreviewButton.ReleaseMouseCapture();
        e.Handled = true;
    }

    private async void VisualOriginalPreview_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_visualOriginalPreviewActive) await SetOriginalPreviewAsync(showOriginal: false);
    }

    private async Task SetOriginalPreviewAsync(bool showOriginal)
    {
        if (_visualOriginalPreviewActive == showOriginal) return;
        var version = ++_visualOriginalPreviewVersion;
        _visualOriginalPreviewActive = showOriginal;
        VisualOriginalPreviewButton.Content = showOriginal ? "正在显示原图" : "按住看原图";
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId ||
            !string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
        {
            _visualOriginalPreviewActive = false;
            VisualOriginalPreviewButton.Content = "按住看原图";
            return;
        }

        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port;
        if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
        {
            if (version == _visualOriginalPreviewVersion)
            {
                _visualOriginalPreviewActive = false;
                VisualOriginalPreviewButton.Content = "按住看原图";
                SetStatus("Codex 尚未连接，暂时无法对比原图");
            }
            return;
        }
        if (version != _visualOriginalPreviewVersion) return;
        try
        {
            if (showOriginal)
            {
                _visualSettingsDebounce?.Stop();
                await SavePreferencesAsync();
            }
            var settings = GetVisualSettings(themeId);
            var preview = showOriginal
                ? _editingVisualDarkMode
                    ? settings with { Dark = new ThemeVisualModeSettings() }
                    : settings with { Light = new ThemeVisualModeSettings() }
                : settings;
            await _runtime.ApplyVisualSettingsAsync(port.Value, themeId, preview.Normalize());
            if (version == _visualOriginalPreviewVersion)
            {
                SetStatus(showOriginal ? "正在临时显示主题原图" : "已恢复个人图像参数");
            }
        }
        catch (Exception exception)
        {
            if (version == _visualOriginalPreviewVersion)
            {
                _visualOriginalPreviewActive = false;
                VisualOriginalPreviewButton.Content = "按住看原图";
                SetStatus($"原图对比失败：{exception.Message}");
            }
        }
    }

    private void ScheduleVisualSettingsUpdate()
    {
        if (_visualSettingsDebounce is null) return;
        _visualSettingsDebounce.Stop();
        _visualSettingsDebounce.Start();
    }

    private async void VisualSettingsDebounce_Tick(object? sender, EventArgs e)
    {
        _visualSettingsDebounce?.Stop();
        ResetVisualHistoryCoalescing();
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        try
        {
            await SavePreferencesAsync();
            if (!string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"{theme.Name} 的图像参数已保存，应用主题后生效");
                return;
            }

            var state = await _stateStore.LoadAsync();
            var port = _activePort ?? state?.Port;
            if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
            {
                SetStatus("图像参数已保存；Codex 下次连接时自动生效");
                return;
            }

            await _runtime.ApplyVisualSettingsAsync(port.Value, themeId, GetVisualSettings(themeId));
            SetStatus($"已实时更新 {theme.Name} 的图像参数");
        }
        catch (Exception exception)
        {
            SetStatus($"图像参数已保留，但实时更新失败：{exception.Message}");
        }
    }

    private void UpdateVisualAdjustmentControls()
    {
        if (!_uiInitialized || VisualAdjustmentEditor is null) return;
        var theme = GetVisualAdjustmentTheme();
        var available = theme?.ThemeId is { Length: > 0 };
        VisualAdjustmentEditor.IsEnabled = available;
        var isApplied = available && string.Equals(
            theme!.ThemeId,
            _activeThemeId,
            StringComparison.OrdinalIgnoreCase);
        VisualThemeNameText.Text = available
            ? isApplied
                ? $"{theme!.Name} · 当前修改会立即显示在 Codex 中"
                : $"{theme!.Name} · 参数会保存并在应用主题时生效"
            : "请先在主题画廊中选择一个有效主题";
        VisualEditingModeText.Text = _codexDarkMode is null
            ? $"{(_editingVisualDarkMode ? "暗色" : "亮色")}参数 · 待检测"
            : _editingVisualDarkMode ? "暗色参数" : "亮色参数";
        VisualEditingModeBadge.Background = (Brush)Resources[_editingVisualDarkMode ? "AccentSoft" : "SkySoft"];
        VisualEditingModeBadge.BorderBrush = (Brush)Resources[_editingVisualDarkMode ? "Accent" : "Sky"];
        VisualEditingModeText.Foreground = (Brush)Resources[_editingVisualDarkMode ? "Accent" : "Sky"];
        UpdateSettingsVisualHeader();
        UpdateVisualEditorActions();
        if (!available) return;

        var settings = GetVisualSettings(theme!.ThemeId!);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        HeroAdjustmentEditor.SetAdjustment(mode.Hero);
        SidebarAdjustmentEditor.SetAdjustment(mode.Sidebar);
        ChatAdjustmentEditor.SetAdjustment(mode.Chat);
        UpdateVisualAdjustmentGroup();
    }

    private void UpdateVisualEditorActions()
    {
        if (!_uiInitialized || VisualUndoButton is null) return;
        var theme = GetVisualAdjustmentTheme();
        var themeId = theme?.ThemeId;
        var available = !string.IsNullOrWhiteSpace(themeId);
        VisualUndoButton.IsEnabled = available &&
            _visualUndoHistory.TryGetValue(themeId!, out var undo) && undo.Count > 0;
        VisualRedoButton.IsEnabled = available &&
            _visualRedoHistory.TryGetValue(themeId!, out var redo) && redo.Count > 0;
        VisualOriginalPreviewButton.IsEnabled = available &&
            string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase);
        CopyVisualModeButton.IsEnabled = available;
        CopyVisualModeButton.Content = _editingVisualDarkMode ? "复制到亮色" : "复制到暗色";
        SaveVisualPresetButton.IsEnabled = available;
        var preset = ResolveSelectedVisualPreset();
        ApplyVisualPresetButton.IsEnabled = available && preset is not null;
        DeleteVisualPresetButton.IsEnabled = preset is not null;
        ExportVisualPresetButton.IsEnabled = preset is not null;
        HeroAdjustmentEditor.SetPasteAvailable(_visualAdjustmentClipboard is not null);
        SidebarAdjustmentEditor.SetPasteAvailable(_visualAdjustmentClipboard is not null);
        ChatAdjustmentEditor.SetPasteAvailable(_visualAdjustmentClipboard is not null);
    }

    private void RecordVisualUndo(
        string themeId,
        ThemeVisualSettings current,
        string? coalesceKey = null)
    {
        if (coalesceKey is not null &&
            string.Equals(themeId, _visualHistoryCoalesceThemeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(coalesceKey, _visualHistoryCoalesceKey, StringComparison.Ordinal)) return;

        AddVisualHistoryEntry(GetVisualHistory(_visualUndoHistory, themeId), current.Normalize());
        GetVisualHistory(_visualRedoHistory, themeId).Clear();
        _visualHistoryCoalesceThemeId = themeId;
        _visualHistoryCoalesceKey = coalesceKey;
        UpdateVisualEditorActions();
    }

    private static List<ThemeVisualSettings> GetVisualHistory(
        Dictionary<string, List<ThemeVisualSettings>> histories,
        string themeId)
    {
        if (histories.TryGetValue(themeId, out var history)) return history;
        history = [];
        histories[themeId] = history;
        return history;
    }

    private static void AddVisualHistoryEntry(
        List<ThemeVisualSettings> history,
        ThemeVisualSettings settings)
    {
        if (history.Count == 0 || history[^1] != settings)
        {
            history.Add(settings);
        }
        if (history.Count > MaxVisualHistoryEntries)
        {
            history.RemoveAt(0);
        }
    }

    private void ResetVisualHistoryCoalescing()
    {
        _visualHistoryCoalesceThemeId = null;
        _visualHistoryCoalesceKey = null;
        UpdateVisualEditorActions();
    }

    private ThemeArtworkPreset? ResolveSelectedVisualPreset()
    {
        if (VisualPresetComboBox?.SelectedItem is ThemeArtworkPreset selected) return selected;
        return null;
    }

    private static ThemeArtworkAdjustment GetRegionAdjustment(
        ThemeVisualModeSettings mode,
        string region) => region switch
        {
            "hero" => mode.Hero,
            "sidebar" => mode.Sidebar,
            "chat" => mode.Chat,
            _ => new ThemeArtworkAdjustment(),
        };

    private static ThemeVisualModeSettings SetRegionAdjustment(
        ThemeVisualModeSettings mode,
        string region,
        ThemeArtworkAdjustment adjustment) => region switch
        {
            "hero" => mode with { Hero = adjustment },
            "sidebar" => mode with { Sidebar = adjustment },
            "chat" => mode with { Chat = adjustment },
            _ => mode,
        };

    private static string GetRegionDisplayName(string region) => region switch
    {
        "hero" => "首页横幅",
        "sidebar" => "左栏图片",
        "chat" => "聊天背景",
        _ => "图像区域",
    };

    private void UpdateVisualAdjustmentGroup()
    {
        if (!_uiInitialized || HeroAdjustmentEditor is null) return;
        HeroAdjustmentEditor.ShowGroup(_visualAdjustmentGroup);
        SidebarAdjustmentEditor.ShowGroup(_visualAdjustmentGroup);
        ChatAdjustmentEditor.ShowGroup(_visualAdjustmentGroup);

        SetVisualAdjustmentGroupButton(VisualBasicGroupButton, ArtworkAdjustmentGroup.Basic);
        SetVisualAdjustmentGroupButton(VisualCompositionGroupButton, ArtworkAdjustmentGroup.Composition);
        SetVisualAdjustmentGroupButton(VisualEffectsGroupButton, ArtworkAdjustmentGroup.Effects);
    }

    private void SetVisualAdjustmentGroupButton(Button button, ArtworkAdjustmentGroup group)
    {
        var active = _visualAdjustmentGroup == group;
        button.Background = active ? (Brush)Resources["AccentSoft"] : Brushes.Transparent;
        button.BorderBrush = active ? (Brush)Resources["Accent"] : Brushes.Transparent;
        button.Foreground = (Brush)Resources[active ? "Accent" : "MutedText"];
    }

    private void UpdateSettingsVisualHeader()
    {
        if (!_uiInitialized || SettingsThemeControlBar is null) return;

        var candidates = GetQuickSwitchCandidates();
        var adjustmentTheme = GetVisualAdjustmentTheme();
        var activeTheme = string.IsNullOrWhiteSpace(_activeThemeId)
            ? null
            : _themes.FirstOrDefault(theme =>
                string.Equals(theme.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase));
        var positionTheme = activeTheme ?? adjustmentTheme;
        var position = positionTheme is null
            ? -1
            : Array.FindIndex(candidates, theme =>
                string.Equals(theme.ThemeId, positionTheme.ThemeId, StringComparison.OrdinalIgnoreCase));

        SettingsCurrentThemeNameText.Text = activeTheme?.Name ?? "Codex 默认外观";
        SettingsThemeStateText.Text = activeTheme is not null
            ? "已应用 · 下方调节实时生效"
            : adjustmentTheme is not null
                ? $"默认外观 · 待应用 {adjustmentTheme.Name}"
                : "还没有可用主题";
        SettingsThemePositionText.Text = position >= 0
            ? $"{position + 1:00} / {candidates.Length:00}"
            : $"— / {candidates.Length:00}";
        SettingsLiveDot.Fill = (Brush)Resources[activeTheme is not null
            ? "Positive"
            : adjustmentTheme is not null ? "Amber" : "SubtleText"];
        SettingsPreviousThemeButton.IsEnabled = candidates.Length > 0;
        SettingsNextThemeButton.IsEnabled = candidates.Length > 0;

        SettingsModeMoonIcon.Visibility = _codexDarkMode is true ? Visibility.Visible : Visibility.Collapsed;
        SettingsModeSunIcon.Visibility = _codexDarkMode is false ? Visibility.Visible : Visibility.Collapsed;
        SettingsModeUnknownText.Visibility = _codexDarkMode is null ? Visibility.Visible : Visibility.Collapsed;
        if (_codexDarkMode is true)
        {
            SettingsColorModeText.Text = "Codex 当前暗色";
            SettingsColorModeHintText.Text = "点击切换到亮色";
            SettingsColorModeButton.Background = (Brush)Resources["AccentSoft"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["Accent"];
            SettingsColorModeButton.ToolTip = "Codex 当前为暗色，点击切换到亮色";
        }
        else if (_codexDarkMode is false)
        {
            SettingsColorModeText.Text = "Codex 当前亮色";
            SettingsColorModeHintText.Text = "点击切换到暗色";
            SettingsColorModeButton.Background = (Brush)Resources["SkySoft"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["Sky"];
            SettingsColorModeButton.ToolTip = "Codex 当前为亮色，点击切换到暗色";
        }
        else
        {
            SettingsColorModeText.Text = "检测显示模式";
            SettingsColorModeHintText.Text = "点击连接并切换";
            SettingsColorModeButton.Background = (Brush)Resources["SettingsControlSurface"];
            SettingsColorModeButton.BorderBrush = (Brush)Resources["SettingsControlBorder"];
            SettingsColorModeButton.ToolTip = "连接 Codex 后读取并切换亮暗模式";
        }
    }

}
