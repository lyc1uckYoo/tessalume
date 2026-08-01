namespace Tessalume.Core.Themes;

public sealed class ThemeImporter(ThemePackageLoader loader)
{
    public async Task<ThemePackage> ImportAsync(
        string sourceDirectory,
        string themesDirectory,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        var loadResult = await loader.LoadAsync(sourceDirectory, cancellationToken);
        var package = loadResult.Package;
        if (package is null)
        {
            var details = string.Join(Environment.NewLine, loadResult.Validation.Issues.Select(issue => issue.Message));
            throw new InvalidDataException($"主题包未通过校验：{Environment.NewLine}{details}");
        }

        Directory.CreateDirectory(themesDirectory);
        var destination = Path.Combine(themesDirectory, package.Manifest.Id);
        if (string.Equals(
                Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return package;
        }

        if (Directory.Exists(destination) && !overwrite)
        {
            throw new IOException($"本地主题库中已存在 {package.Manifest.Id}。");
        }

        var staging = Path.Combine(themesDirectory, $".import-{Guid.NewGuid():N}");
        var backup = Path.Combine(themesDirectory, $".backup-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            CopyPackageFile(package.RootDirectory, package.ManifestPath, staging);
            if (package.CssPath is not null)
            {
                CopyPackageFile(package.RootDirectory, package.CssPath, staging);
            }

            if (package.ScriptPath is not null)
            {
                CopyPackageFile(package.RootDirectory, package.ScriptPath, staging);
            }
            foreach (var path in package.AssetPaths.Values)
            {
                CopyPackageFile(package.RootDirectory, path, staging);
            }

            if (package.PreviewLightPath is not null)
            {
                CopyPackageFile(package.RootDirectory, package.PreviewLightPath, staging);
            }

            if (package.PreviewDarkPath is not null)
            {
                CopyPackageFile(package.RootDirectory, package.PreviewDarkPath, staging);
            }

            var stagedResult = await loader.LoadAsync(staging, cancellationToken);
            if (!stagedResult.Validation.IsValid)
            {
                throw new InvalidDataException("复制后的主题包未通过二次校验。");
            }

            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backup);
            }

            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(destination))
                {
                    Directory.Move(backup, destination);
                }

                throw;
            }

            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }

            return (await loader.LoadAsync(destination, cancellationToken)).Package
                ?? throw new InvalidDataException("导入后的主题包无法读取。");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static void CopyPackageFile(string sourceRoot, string sourcePath, string stagingRoot)
    {
        var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
        var destinationPath = Path.Combine(stagingRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }
}
