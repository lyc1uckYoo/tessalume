namespace Tessalume.App.Creator;

internal interface ICreatorWorkspaceRepository
{
    IReadOnlyList<CreatorWorkspaceRecord> Entries { get; }

    void Touch(string directoryPath, DateTimeOffset openedAt, string? displayName = null);

    bool Remove(string directoryPath);
}
