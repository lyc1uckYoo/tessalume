using System.IO;

namespace Tessalume.App.Infrastructure;

internal sealed record PortableLayout(
    string RootDirectory,
    string ThemesDirectory,
    string DataDirectory)
{
    private const string LegacyAdvancedThemesFolderName = "advanced";

    public string PetsDirectory => Path.Combine(RootDirectory, "pets");

    public static PortableLayout Create()
    {
        var root = Path.GetFullPath(AppContext.BaseDirectory);
        var themes = Path.Combine(root, "themes");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(themes);
        Directory.CreateDirectory(Path.Combine(root, "pets"));
        Directory.CreateDirectory(data);
        MigrateLegacyThemeLayout(themes);
        return new PortableLayout(root, themes, data);
    }

    private static void MigrateLegacyThemeLayout(string themesDirectory)
    {
        var legacyDirectory = Path.Combine(themesDirectory, LegacyAdvancedThemesFolderName);
        if (!Directory.Exists(legacyDirectory))
        {
            return;
        }

        foreach (var source in Directory.EnumerateDirectories(legacyDirectory))
        {
            var destination = Path.Combine(themesDirectory, Path.GetFileName(source));
            if (!Directory.Exists(destination))
            {
                Directory.Move(source, destination);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(legacyDirectory).Any())
        {
            Directory.Delete(legacyDirectory);
        }
    }
}
