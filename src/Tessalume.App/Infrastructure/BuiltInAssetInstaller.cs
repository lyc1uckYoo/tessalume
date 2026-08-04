using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Tessalume.App.Infrastructure;

internal sealed record CreatorWorkspaceUpgradeResult(
    int UpdatedFileCount,
    string? BackupDirectory);

internal static class BuiltInAssetInstaller
{
    private const string ThemePrefix = "Tessalume.BuiltInThemes/";
    private const string CompatibilityPrefix = "Tessalume.Compatibility/";
    private const string TemplatePrefix = "Tessalume.Templates/";
    private const string CreatorWorkspacePrefix = "Tessalume.CreatorWorkspace/";
    private const string DeletedThemesFileName = "deleted-built-in-themes.txt";

    private static IReadOnlyDictionary<string, string> ResourceFolderThemeIds { get; } =
        DiscoverBuiltInThemes();

    public static IReadOnlySet<string> ThemeIds { get; } = ResourceFolderThemeIds.Values
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsBuiltInTheme(string? themeId) =>
        themeId is not null && ThemeIds.Contains(themeId);

    public static void EnsureInstalled(PortableLayout layout)
    {
        var assembly = typeof(BuiltInAssetInstaller).Assembly;
        var deletedThemeIds = LoadDeletedThemeIds(layout);
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (resourceName.StartsWith(ThemePrefix, StringComparison.Ordinal))
            {
                var relativeName = resourceName[ThemePrefix.Length..];
                var resourceFolder = GetThemeResourceFolder(relativeName);
                var themeId = ResourceFolderThemeIds.GetValueOrDefault(resourceFolder, resourceFolder);
                if (deletedThemeIds.Contains(themeId))
                {
                    continue;
                }

                ExtractResource(
                    assembly,
                    resourceName,
                    ThemePrefix,
                    layout.ThemesDirectory);
            }
            else if (resourceName.StartsWith(CompatibilityPrefix, StringComparison.Ordinal))
            {
                ExtractResource(
                    assembly,
                    resourceName,
                    CompatibilityPrefix,
                    Path.Combine(layout.RootDirectory, "Compatibility"));
            }
            else if (resourceName.StartsWith(TemplatePrefix, StringComparison.Ordinal))
            {
                ExtractResource(
                    assembly,
                    resourceName,
                    TemplatePrefix,
                    Path.Combine(layout.RootDirectory, "Templates"));
            }
        }
    }

    public static void MarkDeleted(PortableLayout layout, string themeId)
    {
        if (!IsBuiltInTheme(themeId)) return;

        var deletedThemeIds = LoadDeletedThemeIds(layout);
        if (!deletedThemeIds.Add(themeId)) return;

        var path = Path.Combine(layout.DataDirectory, DeletedThemesFileName);
        File.WriteAllLines(path, deletedThemeIds.Order(StringComparer.OrdinalIgnoreCase));
    }

    public static int RestoreDeletedThemes(PortableLayout layout)
    {
        var deletedThemeIds = LoadDeletedThemeIds(layout);
        if (deletedThemeIds.Count == 0) return 0;

        var path = Path.Combine(layout.DataDirectory, DeletedThemesFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        EnsureInstalled(layout);
        return deletedThemeIds.Count;
    }

    public static void CreateCreatorWorkspace(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"目标位置已经存在：{destination}");
        }

        Directory.CreateDirectory(destination);
        try
        {
            var assembly = typeof(BuiltInAssetInstaller).Assembly;
            var resources = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(CreatorWorkspacePrefix, StringComparison.Ordinal))
                .ToArray();
            if (resources.Length == 0)
            {
                throw new InvalidDataException("程序中没有找到 Codex 主题创作者工作区资源。");
            }

            foreach (var resourceName in resources)
            {
                ExtractResource(
                    assembly,
                    resourceName,
                    CreatorWorkspacePrefix,
                    destination);
            }

            ExtractResource(
                assembly,
                CompatibilityPrefix + "theme-template-v1.css",
                CompatibilityPrefix,
                Path.Combine(destination, "src", "Tessalume.App", "Compatibility"));
        }
        catch
        {
            Directory.Delete(destination, recursive: true);
            throw;
        }
    }

    public static CreatorWorkspaceUpgradeResult UpgradeCreatorWorkspace(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        destination = Path.GetFullPath(destination);
        if (!Directory.Exists(destination) || !Directory.Exists(Path.Combine(destination, "themes")))
        {
            throw new InvalidDataException("所选目录不是可升级的 Tessalume 创作者工作区。");
        }
        EnsureNoReparsePoints(destination, destination);

        var assembly = typeof(BuiltInAssetInstaller).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(CreatorWorkspacePrefix, StringComparison.Ordinal))
            .Where(IsManagedCreatorWorkspaceResource)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
        {
            throw new InvalidDataException("程序中没有找到可升级的创作者工作区资源。");
        }

        var changes = resources.Select(resourceName =>
        {
            var relativePath = resourceName[CreatorWorkspacePrefix.Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(destination, relativePath));
            EnsureContained(destination, destinationPath);
            EnsureNoReparsePoints(destination, destinationPath);
            using var resource = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Missing embedded creator resource: {resourceName}");
            return new CreatorWorkspaceResourceChange(
                resourceName,
                relativePath,
                destinationPath,
                File.Exists(destinationPath),
                !FileMatches(destinationPath, resource));
        }).Where(change => change.RequiresUpdate).ToArray();
        if (changes.Length == 0)
        {
            return new CreatorWorkspaceUpgradeResult(0, null);
        }

        var backupDirectory = CreateWorkspaceUpgradeBackupDirectory(destination);
        try
        {
            foreach (var change in changes.Where(change => change.Existed))
            {
                var backupPath = Path.Combine(backupDirectory, change.RelativePath);
                EnsureNoReparsePoints(destination, change.DestinationPath);
                EnsureNoReparsePoints(destination, backupPath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                EnsureNoReparsePoints(destination, backupPath);
                File.Copy(change.DestinationPath, backupPath, overwrite: true);
            }

            foreach (var change in changes)
            {
                EnsureNoReparsePoints(destination, change.DestinationPath);
                ExtractResource(
                    assembly,
                    change.ResourceName,
                    CreatorWorkspacePrefix,
                    destination);
            }

            var contract = Tessalume.Core.Creator.CreatorWorkspaceContract.Inspect(destination);
            if (contract.State != Tessalume.Core.Creator.CreatorWorkspaceContractState.Current)
            {
                throw new InvalidDataException("工作区文件已经写入，但版本复核没有通过，正在恢复升级前状态。");
            }
            return new CreatorWorkspaceUpgradeResult(changes.Length, backupDirectory);
        }
        catch
        {
            foreach (var change in changes)
            {
                var backupPath = Path.Combine(backupDirectory, change.RelativePath);
                if (change.Existed && File.Exists(backupPath))
                {
                    EnsureNoReparsePoints(destination, change.DestinationPath);
                    EnsureNoReparsePoints(destination, backupPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(change.DestinationPath)!);
                    EnsureNoReparsePoints(destination, change.DestinationPath);
                    File.Copy(backupPath, change.DestinationPath, overwrite: true);
                }
                else if (!change.Existed && File.Exists(change.DestinationPath))
                {
                    EnsureNoReparsePoints(destination, change.DestinationPath);
                    File.Delete(change.DestinationPath);
                }
            }
            throw;
        }
    }

    private static bool IsManagedCreatorWorkspaceResource(string resourceName)
    {
        var relative = resourceName[CreatorWorkspacePrefix.Length..];
        return relative is
            "AGENTS.md" or
            "START_HERE.md" or
            "TESSALUME_CREATOR_WORKSPACE.md" or
            "TESSALUME_CREATOR_WORKSPACE.json" ||
            relative.StartsWith(".agents/", StringComparison.Ordinal) ||
            relative.StartsWith("schemas/", StringComparison.Ordinal) ||
            relative.StartsWith("src/", StringComparison.Ordinal);
    }

    private static string CreateWorkspaceUpgradeBackupDirectory(string workspace)
    {
        var parent = Path.Combine(workspace, ".tessalume-backups");
        EnsureNoReparsePoints(workspace, parent);
        Directory.CreateDirectory(parent);
        EnsureNoReparsePoints(workspace, parent);
        var baseName = $"workspace-upgrade-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
        for (var suffix = 0; suffix < 100; suffix++)
        {
            var name = suffix == 0 ? baseName : $"{baseName}-{suffix + 1}";
            var candidate = Path.Combine(parent, name);
            if (Directory.Exists(candidate)) continue;
            Directory.CreateDirectory(candidate);
            EnsureNoReparsePoints(workspace, candidate);
            return candidate;
        }
        throw new IOException("短时间内创建了过多工作区升级备份，请稍后重试。");
    }

    private sealed record CreatorWorkspaceResourceChange(
        string ResourceName,
        string RelativePath,
        string DestinationPath,
        bool Existed,
        bool RequiresUpdate);

    private static HashSet<string> LoadDeletedThemeIds(PortableLayout layout)
    {
        var path = Path.Combine(layout.DataDirectory, DeletedThemesFileName);
        return File.Exists(path)
            ? File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> DiscoverBuiltInThemes()
    {
        var assembly = typeof(BuiltInAssetInstaller).Assembly;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ThemePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativeName = resourceName[ThemePrefix.Length..];
            var resourceFolder = GetThemeResourceFolder(relativeName);
            using var resource = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Missing embedded theme manifest: {resourceName}");
            using var document = JsonDocument.Parse(resource);
            if (!document.RootElement.TryGetProperty("id", out var idElement) ||
                string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                throw new InvalidDataException($"Embedded theme manifest has no id: {resourceName}");
            }

            var themeId = idElement.GetString()!;
            if (!result.TryAdd(resourceFolder, themeId))
            {
                throw new InvalidDataException($"Duplicate embedded theme folder: {resourceFolder}");
            }
        }

        return result;
    }

    private static string GetThemeResourceFolder(string relativeName)
    {
        var segments = relativeName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments[0];
    }

    private static void ExtractResource(
        Assembly assembly,
        string resourceName,
        string prefix,
        string destinationRoot)
    {
        var relativePath = resourceName[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
        EnsureContained(destinationRoot, destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var resource = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Missing embedded resource: {resourceName}");
        if (FileMatches(destinationPath, resource))
        {
            return;
        }

        resource.Position = 0;
        var temporaryPath = destinationPath + ".tmp";
        using (var output = File.Create(temporaryPath))
        {
            resource.CopyTo(output);
        }

        File.Move(temporaryPath, destinationPath, overwrite: true);
    }

    private static bool FileMatches(string path, Stream resource)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != resource.Length)
        {
            return false;
        }

        using var file = File.OpenRead(path);
        Span<byte> fileBuffer = stackalloc byte[8192];
        Span<byte> resourceBuffer = stackalloc byte[8192];
        while (true)
        {
            var fileRead = file.Read(fileBuffer);
            var resourceRead = resource.Read(resourceBuffer);
            if (fileRead != resourceRead)
            {
                return false;
            }

            if (fileRead == 0)
            {
                return true;
            }

            if (!fileBuffer[..fileRead].SequenceEqual(resourceBuffer[..resourceRead]))
            {
                return false;
            }
        }
    }

    private static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), path);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("Embedded asset path escapes the local library.");
        }
    }

    private static void EnsureNoReparsePoints(string root, string path)
    {
        root = Path.GetFullPath(root);
        path = Path.GetFullPath(path);
        EnsureContained(root, path);

        CheckExistingPath(root);
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".") return;

        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current)) break;
            CheckExistingPath(current);
        }

        static void CheckExistingPath(string candidate)
        {
            if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"创作者工作区升级不能穿过符号链接或重解析点：{candidate}");
            }
        }
    }
}
