using System.IO;
using System.Windows;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void CreatorCenter_Click(object sender, RoutedEventArgs e)
    {
        ShowInfoPage(RightPane.Creator);
        await CreatorCenter.ActivateAsync();
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
