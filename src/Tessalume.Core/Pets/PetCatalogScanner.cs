namespace Tessalume.Core.Pets;

public sealed class PetCatalogScanner(PetPackageLoader loader)
{
    public async Task<IReadOnlyList<PetPackageCandidate>> ScanAsync(
        string catalogRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogRoot);
        var root = Path.GetFullPath(catalogRoot);
        if (!Directory.Exists(root)) return [];

        PetPathSafety.EnsureRegularDirectory(root, root);
        var candidates = new List<PetPackageCandidate>();
        foreach (var directory in Directory
                     .EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await loader.LoadAsync(directory, cancellationToken);
                candidates.Add(new PetPackageCandidate(directory, result.Package, result.Validation));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or
                ArgumentException or NotSupportedException)
            {
                var validation = new PetValidationResult();
                validation.AddError("catalog.package.unsafe", exception.Message, directory);
                candidates.Add(new PetPackageCandidate(directory, null, validation));
            }
        }

        foreach (var group in candidates
                     .Where(candidate => candidate.Package is not null)
                     .GroupBy(candidate => candidate.Package!.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var candidate in group)
            {
                candidate.Validation.AddError(
                    "catalog.pet-id.duplicate",
                    $"多个宠物包声明了同一 ID：{group.Key}",
                    candidate.DirectoryPath);
            }
        }
        return candidates;
    }
}
