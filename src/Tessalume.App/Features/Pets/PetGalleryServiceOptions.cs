using System.IO;
using Tessalume.App.Infrastructure;

namespace Tessalume.App.Features.Pets;

internal sealed record PetGalleryServiceOptions(
    string OfficialPackagesRoot,
    string DevelopmentProjectsRoot)
{
    public static PetGalleryServiceOptions ForLayout(PortableLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new PetGalleryServiceOptions(
            Path.GetFullPath(layout.PetsDirectory),
            FindDevelopmentProjectsRoot(layout.RootDirectory));
    }

    private static string FindDevelopmentProjectsRoot(string applicationRoot)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var startingPath in new[] { applicationRoot, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startingPath));
            while (directory is not null && visited.Add(directory.FullName))
            {
                var candidate = Path.Combine(directory.FullName, "pet-projects");
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(directory.FullName, "Tessalume.sln")))
                {
                    return Path.GetFullPath(candidate);
                }
                directory = directory.Parent;
            }
        }

        return Path.GetFullPath(Path.Combine(applicationRoot, "pet-projects"));
    }
}
