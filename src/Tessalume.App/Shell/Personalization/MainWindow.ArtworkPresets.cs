using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
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
        }.Normalize();
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
}
