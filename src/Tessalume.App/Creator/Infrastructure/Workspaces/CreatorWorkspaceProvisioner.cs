using System.IO;
using Tessalume.App.Infrastructure;

namespace Tessalume.App.Creator;

internal sealed class CreatorWorkspaceProvisioner(string applicationRoot)
{
    private const string WorkspaceFolderName = "Tessalume-Creator";
    private const string TemplateFolderName = "theme-template-v1";

    public static string CreateWorkspace(string parentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        var destination = GetAvailableDirectory(parentDirectory, WorkspaceFolderName);
        BuiltInAssetInstaller.CreateCreatorWorkspace(destination);
        return destination;
    }

    public static string ResolveExistingWorkspace(string selectedDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedDirectory);
        var selected = Path.GetFullPath(selectedDirectory);
        if (!Directory.Exists(selected))
        {
            throw new DirectoryNotFoundException("所选工作区文件夹不存在。");
        }

        if (LooksLikeWorkspace(selected)) return selected;

        var parent = Directory.GetParent(selected)?.FullName;
        if (Path.GetFileName(selected).Equals("themes", StringComparison.OrdinalIgnoreCase) &&
            parent is not null && LooksLikeWorkspace(parent))
        {
            return parent;
        }

        if (File.Exists(Path.Combine(selected, "manifest.json")) &&
            parent is not null &&
            Path.GetFileName(parent).Equals("themes", StringComparison.OrdinalIgnoreCase))
        {
            var workspace = Directory.GetParent(parent)?.FullName;
            if (workspace is not null && LooksLikeWorkspace(workspace)) return workspace;
        }

        throw new InvalidDataException(
            "没有找到创作者工作区。请选择包含 themes 文件夹的工作区根目录；也可以直接选择 themes 文件夹或其中的主题项目。");
    }

    public string CopyManualTemplate(string parentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        var source = Path.Combine(applicationRoot, "Templates", TemplateFolderName);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException("模板文件尚未释放，请重启 Tessalume 后再试。");
        }

        var destination = GetAvailableDirectory(parentDirectory, "my-character-theme");
        try
        {
            CopyDirectory(source, destination);
            return destination;
        }
        catch
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            throw;
        }
    }

    public static CreatorWorkspaceUpgradeResult UpgradeWorkspace(string workspaceDirectory) =>
        BuiltInAssetInstaller.UpgradeCreatorWorkspace(workspaceDirectory);

    private static bool LooksLikeWorkspace(string directory) =>
        Directory.Exists(Path.Combine(directory, "themes"));

    private static string GetAvailableDirectory(string parentDirectory, string baseName)
    {
        var parent = Path.GetFullPath(parentDirectory);
        var first = Path.Combine(parent, baseName);
        if (!Directory.Exists(first) && !File.Exists(first)) return first;

        for (var suffix = 2; suffix <= 99; suffix++)
        {
            var candidate = Path.Combine(parent, $"{baseName}-{suffix}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }

        throw new IOException($"所选位置已有过多 {baseName} 文件夹，请换一个位置后重试。");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
