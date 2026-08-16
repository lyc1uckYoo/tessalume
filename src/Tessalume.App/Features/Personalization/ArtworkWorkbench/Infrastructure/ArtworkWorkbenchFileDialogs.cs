using System.Windows;
using Microsoft.Win32;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal static class ArtworkWorkbenchFileDialogs
{
    private const string ImageFilter =
        "图片文件 (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp";
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

}
