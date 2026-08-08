using System.IO;

namespace Tessalume.App.Creator;

internal sealed record CreatorWorkspaceRecord
{
    public string DirectoryPath { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public DateTimeOffset LastOpenedAt { get; init; }
}

internal sealed class CreatorWorkspaceStore : ICreatorWorkspaceRepository
{
    public const int MaximumRecentWorkspaces = 12;

    private readonly List<CreatorWorkspaceRecord> _entries;

    public CreatorWorkspaceStore(IEnumerable<CreatorWorkspaceRecord>? entries = null) =>
        _entries = Normalize(entries).ToList();

    public IReadOnlyList<CreatorWorkspaceRecord> Entries => _entries;

    public void Touch(
        string directoryPath,
        DateTimeOffset openedAt,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var normalizedPath = NormalizePath(directoryPath);
        var previous = _entries.FirstOrDefault(entry => PathsEqual(entry.DirectoryPath, normalizedPath));
        var normalizedName = string.IsNullOrWhiteSpace(displayName)
            ? previous?.DisplayName
            : displayName.Trim();

        _entries.RemoveAll(entry => PathsEqual(entry.DirectoryPath, normalizedPath));
        _entries.Add(new CreatorWorkspaceRecord
        {
            DirectoryPath = normalizedPath,
            DisplayName = ResolveDisplayName(normalizedPath, normalizedName),
            LastOpenedAt = openedAt,
        });
        ReplaceWithNormalizedEntries();
    }

    public bool Remove(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return false;

        string normalizedPath;
        try
        {
            normalizedPath = NormalizePath(directoryPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }

        return _entries.RemoveAll(entry => PathsEqual(entry.DirectoryPath, normalizedPath)) > 0;
    }

    public List<CreatorWorkspaceRecord> Snapshot() => Normalize(_entries).ToList();

    public static IReadOnlyList<CreatorWorkspaceRecord> Normalize(
        IEnumerable<CreatorWorkspaceRecord>? entries)
    {
        if (entries is null) return [];

        var normalized = new Dictionary<string, CreatorWorkspaceRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.DirectoryPath)) continue;

            string path;
            try
            {
                path = NormalizePath(entry.DirectoryPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
            {
                continue;
            }

            var candidate = entry with
            {
                DirectoryPath = path,
                DisplayName = ResolveDisplayName(path, entry.DisplayName),
            };
            if (!normalized.TryGetValue(path, out var current) ||
                candidate.LastOpenedAt > current.LastOpenedAt)
            {
                normalized[path] = candidate;
            }
        }

        return normalized.Values
            .OrderByDescending(entry => entry.LastOpenedAt)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumRecentWorkspaces)
            .ToArray();
    }

    private void ReplaceWithNormalizedEntries()
    {
        var normalized = Normalize(_entries);
        _entries.Clear();
        _entries.AddRange(normalized);
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string ResolveDisplayName(string path, string? requestedName)
    {
        if (!string.IsNullOrWhiteSpace(requestedName)) return requestedName.Trim();
        var name = new DirectoryInfo(path).Name;
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }
}
