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
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;
using Tessalume.Core.Updates;
using Microsoft.Win32;

namespace Tessalume.App;

public partial class MainWindow
{
    private void OpenTemplate_Click(object sender, RoutedEventArgs e)
    {
        var path = GetTemplatePath();
        if (!Directory.Exists(path))
        {
            ShowProductMessage("找不到模板", "模板文件尚未释放，请重启应用后再试。", ProductDialogKind.Error);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void PrepareCreatorWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 Codex 主题创作者工作区的保存位置",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var destination = GetAvailableCreatorWorkspacePath(dialog.FolderName);
            BuiltInAssetInstaller.CreateCreatorWorkspace(destination);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{destination}\"") { UseShellExecute = true });
            ShowProductMessage(
                "Codex 创作工作区已准备",
                $"工作区已经创建并打开：\n{destination}\n\n请在 Codex 中打开整个文件夹，然后参照主题创作页的提示词示例发送角色需求。",
                ProductDialogKind.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowProductMessage("无法创建创作工作区", exception.Message, ProductDialogKind.Error);
        }
    }

    private void CopyCreatorPrompt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(CreatorPromptText.Text);
            ShowToast("提示词已复制");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowToast("剪贴板正忙，请再点一次");
        }
    }

    private static string GetAvailableCreatorWorkspacePath(string parentDirectory)
    {
        var first = Path.Combine(parentDirectory, CreatorWorkspaceFolderName);
        if (!Directory.Exists(first) && !File.Exists(first)) return first;

        for (var suffix = 2; suffix <= 99; suffix++)
        {
            var candidate = Path.Combine(parentDirectory, $"{CreatorWorkspaceFolderName}-{suffix}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }

        throw new IOException("所选位置已有过多 Tessalume-Creator 工作区，请换一个文件夹后重试。");
    }

    private void CopyTemplate_Click(object sender, RoutedEventArgs e)
    {
        var source = GetTemplatePath();
        if (!Directory.Exists(source))
        {
            ShowProductMessage("找不到模板", "模板文件尚未释放，请重启应用后再试。", ProductDialogKind.Error);
            return;
        }

        var dialog = new OpenFolderDialog { Title = "选择新主题的保存位置", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;

        const string name = "my-character-theme";
        var destination = Path.Combine(dialog.FolderName, name);
        if (Directory.Exists(destination))
        {
            ShowProductMessage("无法复制模板", $"目标位置已存在文件夹：\n{destination}", ProductDialogKind.Warning);
            return;
        }

        CopyDirectory(source, destination);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{destination}\"") { UseShellExecute = true });
        ShowToast("主题模板已复制并打开");
    }

    private string GetTemplatePath() => Path.Combine(
        _layout.RootDirectory,
        "Templates",
        BuiltInTemplateFolderName);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }

    private void EnsureDeletableThemePath(string themeDirectory)
    {
        var library = Path.GetFullPath(_layout.ThemesDirectory);
        var target = Path.GetFullPath(themeDirectory);
        var relative = Path.GetRelativePath(library, target);
        if (relative is "." or ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("主题路径不在本地主题库内，已拒绝删除。");
        }

        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("符号链接或重解析目录不能通过 Studio 删除。");
        }
    }

}
