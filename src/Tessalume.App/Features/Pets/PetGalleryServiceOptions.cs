using System.IO;
using Tessalume.App.Infrastructure;

namespace Tessalume.App.Features.Pets;

internal sealed record PetGalleryServiceOptions(
    string PackagesRoot)
{
    public static PetGalleryServiceOptions ForLayout(PortableLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new PetGalleryServiceOptions(Path.GetFullPath(layout.PetsDirectory));
    }
}
