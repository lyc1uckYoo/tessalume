namespace Tessalume.Core.Themes;

public sealed class ThemeCatalog(ThemePackageLoader loader)
{
    public async Task<IReadOnlyList<ThemeCatalogItem>> ScanAsync(
        string themesDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(themesDirectory))
        {
            return [];
        }

        var packageDirectories = Directory.EnumerateDirectories(themesDirectory)
            .Where(child => !Path.GetFileName(child).StartsWith('.'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new List<ThemeCatalogItem>();
        foreach (var directory in packageDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await loader.LoadAsync(directory, cancellationToken);
            results.Add(new ThemeCatalogItem(directory, result.Package, result.Validation));
        }

        return results;
    }
}
