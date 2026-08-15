using System.IO;
using System.Windows;
using Microsoft.Win32;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal static class ArtworkWorkbenchFileDialogs
{
    private const string ImageFilter =
        "图片文件 (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp";
    private const string PresetFilter =
        "Tessalume 图像方案 (*.tessalume-look.json)|*.tessalume-look.json|JSON 文件 (*.json)|*.json";

    public static string? ChooseImage(Window owner, string regionName, string modeName)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"为{regionName}的{modeName}参数选择本地图片",
            Filter = ImageFilter,
            Multiselect = false,
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public static string? ChoosePresetToImport(Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 Tessalume 图像方案",
            Filter = PresetFilter,
            Multiselect = false,
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public static string? ChoosePresetExportPath(Window owner, ThemeArtworkPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var safeName = string.Concat(preset.Name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Tessalume-look";
        var dialog = new SaveFileDialog
        {
            Title = "导出 Tessalume 图像方案",
            Filter = PresetFilter,
            DefaultExt = ThemeArtworkPresetExchange.FileExtension,
            AddExtension = true,
            FileName = safeName + ThemeArtworkPresetExchange.FileExtension,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public static string? ChooseArtworkDefaultsExportPath(
        Window owner,
        string themeRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRootDirectory);
        var dialog = new SaveFileDialog
        {
            Title = "导出主题推荐图像参数（作者候选）",
            Filter = "Tessalume artwork defaults (artwork-defaults.json)|artwork-defaults.json|JSON 文件 (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "artwork-defaults.json",
            InitialDirectory = themeRootDirectory,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
