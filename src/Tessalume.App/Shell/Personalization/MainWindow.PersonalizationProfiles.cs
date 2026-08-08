using Tessalume.App.Features.Personalization;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private void DisplayPreferencesPage_PreferencesChanged(
        object? sender,
        DisplayPreferencesChangedEventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var settings = GetVisualSettings(themeId);
        var replacement = (settings with { Display = e.Preferences }).Normalize();
        if (replacement == settings.Normalize()) return;
        RecordVisualUndo(themeId, settings, "display-preferences");
        _themeVisualSettings[themeId] = replacement;
        ScheduleVisualSettingsUpdate();
    }

    private async void ExperienceProfilesPage_SaveRequested(object? sender, EventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var requestedName = ExperienceProfilesPage.RequestedName;
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? $"体验方案 {_experiencePresets.Count + 1}"
            : requestedName;
        if (name.Length > 32) name = name[..32];
        var preset = new ThemeExperiencePreset
        {
            Name = name,
            ThemeId = themeId,
            DarkMode = _editingVisualDarkMode,
            Settings = GetVisualSettings(themeId),
        }.Normalize();
        var existing = _experiencePresets
            .Select((item, index) => (item, index))
            .FirstOrDefault(pair => string.Equals(
                pair.item.Name,
                preset.Name,
                StringComparison.OrdinalIgnoreCase));
        if (existing.item is not null)
        {
            _experiencePresets[existing.index] = preset;
        }
        else
        {
            if (_experiencePresets.Count >= 24)
            {
                ShowProductMessage(
                    "无法保存体验方案",
                    "最多可以保存 24 个体验方案，请先删除不再使用的方案。",
                    ProductDialogKind.Warning);
                return;
            }
            _experiencePresets.Add(preset);
        }

        ExperienceProfilesPage.Select(preset);
        await SavePreferencesAsync();
        ShowToast($"已保存完整体验方案“{preset.Name}”");
    }

    private async void ExperienceProfilesPage_ApplyRequested(object? sender, EventArgs e)
    {
        var preset = ExperienceProfilesPage.SelectedProfile;
        if (preset is null) return;
        var theme = _themes.FirstOrDefault(candidate =>
            candidate.IsValid && string.Equals(
                candidate.ThemeId,
                preset.ThemeId,
                StringComparison.OrdinalIgnoreCase));
        if (theme is null)
        {
            ShowProductMessage(
                "无法应用体验方案",
                "方案引用的主题当前不在本地库中。重新导入主题后，方案和个人图片仍会保留。",
                ProductDialogKind.Warning);
            return;
        }

        var current = GetVisualSettings(preset.ThemeId);
        if (current.Normalize() != preset.Settings.Normalize())
        {
            RecordVisualUndo(preset.ThemeId, current);
            _themeVisualSettings[preset.ThemeId] = preset.Settings.Normalize();
        }
        SelectTheme(theme);
        if (!await ApplyThemeAsync(theme)) return;
        if (_codexDarkMode is null)
        {
            await RefreshCodexColorSchemeAsync();
        }
        if (_codexDarkMode is null)
        {
            _editingVisualDarkMode = preset.DarkMode;
            await SavePreferencesAsync();
            UpdateVisualAdjustmentControls();
            ShowProductMessage(
                "显示模式等待连接",
                "主题与个人参数已经生效，但暂时无法读取 Codex 的亮暗模式。连接 Codex 后再次应用即可完成模式切换。",
                ProductDialogKind.Warning);
            return;
        }
        if (_codexDarkMode != preset.DarkMode)
        {
            var toggled = await ToggleCodexColorSchemeAsync();
            if (toggled is null || toggled.Value != preset.DarkMode)
            {
                ShowProductMessage(
                    "显示模式未完全应用",
                    "主题与个人参数已经生效，但 Codex 当前无法切换到方案记录的亮暗模式。请连接 Codex 后重试。",
                    ProductDialogKind.Warning);
                return;
            }
            _editingVisualDarkMode = toggled.Value;
        }
        else
        {
            _editingVisualDarkMode = preset.DarkMode;
        }
        await SavePreferencesAsync();
        UpdateVisualAdjustmentControls();
        ShowToast($"已应用体验方案“{preset.Name}”");
    }

    private async void ExperienceProfilesPage_DeleteRequested(object? sender, EventArgs e)
    {
        var preset = ExperienceProfilesPage.SelectedProfile;
        if (preset is null || !ShowProductConfirmation(
                "删除体验方案",
                $"将删除“{preset.Name}”。已经应用到主题的图片和参数不会改变。",
                "删除方案",
                dangerous: true)) return;
        _experiencePresets.Remove(preset);
        ExperienceProfilesPage.Select(null);
        await SavePreferencesAsync();
        ShowToast("体验方案已删除");
    }
}
