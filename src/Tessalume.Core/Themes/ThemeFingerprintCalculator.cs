using System.Security.Cryptography;
using System.Text;

namespace Tessalume.Core.Themes;

public static class ThemeFingerprintCalculator
{
    public static async Task<string> CalculateAsync(ThemePackage package, CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["manifest"] = package.ManifestPath,
        };
        if (package.CssPath is not null) files["css"] = package.CssPath;
        if (package.ScriptPath is not null) files["script"] = package.ScriptPath;
        if (package.ArtworkDefaultsPath is not null)
        {
            files["artwork.defaults"] = package.ArtworkDefaultsPath;
        }
        if (package.PreviewLightPath is not null) files["preview.light"] = package.PreviewLightPath;
        if (package.PreviewDarkPath is not null) files["preview.dark"] = package.PreviewDarkPath;
        foreach (var (name, path) in package.AssetPaths) files[$"asset.{name}"] = path;

        var buffer = new byte[64 * 1024];
        foreach (var (name, path) in files.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"{name}\0{Path.GetRelativePath(package.RootDirectory, path)}\0"));
            await using var stream = File.OpenRead(path);
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, bytesRead));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static async Task<string> CalculateEffectiveAsync(
        ThemePackage package,
        string sharedTemplatePath,
        CancellationToken cancellationToken = default)
    {
        var packageFingerprint = await CalculateAsync(package, cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"package\0{packageFingerprint}\0shared.template-v1\0"));
        var buffer = new byte[64 * 1024];
        await using var stream = File.OpenRead(sharedTemplatePath);
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer.AsSpan(0, bytesRead));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
