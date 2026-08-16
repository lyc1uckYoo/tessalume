using System.IO;

namespace Tessalume.App.Features.Pets;

internal sealed record PetApplicationServiceOptions(
    string CodexPetsRoot,
    string BackupRoot,
    string StatePath)
{
    public static PetApplicationServiceOptions ForCurrentUser(string portableDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portableDataDirectory);
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("无法解析当前 Windows 用户目录。");
        }

        var featureDataRoot = Path.Combine(
            Path.GetFullPath(portableDataDirectory),
            "pets");
        return new PetApplicationServiceOptions(
            Path.GetFullPath(Path.Combine(userProfile, ".codex", "pets")),
            Path.Combine(featureDataRoot, "backups"),
            Path.Combine(featureDataRoot, "pet-center-state.v1.json"));
    }
}
