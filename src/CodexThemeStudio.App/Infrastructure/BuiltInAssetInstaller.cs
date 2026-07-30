using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CodexThemeStudio.App.Infrastructure;

internal static class BuiltInAssetInstaller
{
    private const string ThemePrefix = "CodexThemeStudio.BuiltInThemes/";
    private const string CompatibilityPrefix = "CodexThemeStudio.Compatibility/";
    private const string TemplatePrefix = "CodexThemeStudio.Templates/";
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
        => File.Exists(path) && new FileInfo(path).Length == resource.Length;

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
}
