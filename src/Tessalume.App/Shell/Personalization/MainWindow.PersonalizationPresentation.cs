using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tessalume.App.Controls;
using Tessalume.App.Features.Personalization;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
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
        UpdateVisualAdjustmentRegion();
        ExperienceProfilesPage.SetSaveEnabled(available);
        if (!available)
        {
            DisplayPreferencesPage.Render(new ThemeDisplayPreferences(), enabled: false);
            return;
        }

        var settings = GetVisualSettings(theme!.ThemeId!);
        var mode = _editingVisualDarkMode ? settings.Dark : settings.Light;
        DisplayPreferencesPage.Render(settings.Display, enabled: true);
        HeroAdjustmentEditor.SetEditingMode(_editingVisualDarkMode);
        SidebarAdjustmentEditor.SetEditingMode(_editingVisualDarkMode);
        ChatAdjustmentEditor.SetEditingMode(_editingVisualDarkMode);
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
        CopyVisualModeText.Text = _editingVisualDarkMode ? "复制到亮色" : "复制到暗色";
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

    private void UpdateVisualAdjustmentRegion()
    {
        if (!_uiInitialized || HeroAdjustmentEditor is null) return;
        HeroAdjustmentEditor.Visibility = _visualAdjustmentRegion == "hero" ? Visibility.Visible : Visibility.Collapsed;
        SidebarAdjustmentEditor.Visibility = _visualAdjustmentRegion == "sidebar" ? Visibility.Visible : Visibility.Collapsed;
        ChatAdjustmentEditor.Visibility = _visualAdjustmentRegion == "chat" ? Visibility.Visible : Visibility.Collapsed;
        VisualHeroRegionButton.Tag = _visualAdjustmentRegion == "hero" ? "active" : "inactive";
        VisualSidebarRegionButton.Tag = _visualAdjustmentRegion == "sidebar" ? "active" : "inactive";
        VisualChatRegionButton.Tag = _visualAdjustmentRegion == "chat" ? "active" : "inactive";
    }

    private void SetVisualAdjustmentGroupButton(Button button, ArtworkAdjustmentGroup group)
    {
        var active = _visualAdjustmentGroup == group;
        button.Background = active ? (Brush)Resources["AccentSoft"] : Brushes.Transparent;
        button.BorderBrush = active ? (Brush)Resources["Accent"] : Brushes.Transparent;
        button.Foreground = (Brush)Resources[active ? "Accent" : "MutedText"];
    }
}
