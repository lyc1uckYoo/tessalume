using System.Globalization;
using System.IO;

namespace Tessalume.App.Infrastructure;

internal sealed record ThemeUsageRecord
{
    public string ThemeId { get; init; } = string.Empty;

    public DateTimeOffset LastUsedAt { get; init; }

    public int UseCount { get; init; } = 1;
}

internal enum ThemeVersionRelation
{
    Unknown,
    Same,
    Newer,
    Older,
}

internal enum ThemeImportSourceKind
{
    Unsupported,
    Directory,
    ZipArchive,
}

internal static class ThemeLibraryState
{
    public const string DefaultSort = "default";
    public const string RecentSort = "recent";
    public const string NameSort = "name";
    public const string AuthorSort = "author";

    private static readonly HashSet<string> SupportedSorts = new(StringComparer.OrdinalIgnoreCase)
    {
        DefaultSort,
        RecentSort,
        NameSort,
        AuthorSort,
    };

    public static string NormalizeSort(string? value) =>
        SupportedSorts.Contains(value ?? string.Empty)
            ? value!.ToLowerInvariant()
            : DefaultSort;

    public static List<ThemeUsageRecord> NormalizeUsage(
        IEnumerable<ThemeUsageRecord>? records,
        int limit = 100)
    {
        if (limit <= 0) return [];

        return (records ?? [])
            .Where(record => record is not null &&
                !string.IsNullOrWhiteSpace(record.ThemeId) &&
                record.LastUsedAt != default)
            .Select(record => record with
            {
                ThemeId = record.ThemeId.Trim(),
                UseCount = Math.Max(1, record.UseCount),
            })
            .GroupBy(record => record.ThemeId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(record => record.LastUsedAt)
                .ThenByDescending(record => record.UseCount)
                .First())
            .OrderByDescending(record => record.LastUsedAt)
            .ThenBy(record => record.ThemeId, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    public static ThemeImportSourceKind ClassifyImportSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return ThemeImportSourceKind.Unsupported;
        if (Directory.Exists(path)) return ThemeImportSourceKind.Directory;
        return File.Exists(path) && string.Equals(
            Path.GetExtension(path),
            ".zip",
            StringComparison.OrdinalIgnoreCase)
                ? ThemeImportSourceKind.ZipArchive
                : ThemeImportSourceKind.Unsupported;
    }

    public static ThemeVersionRelation CompareVersions(string? current, string? incoming)
    {
        if (!TryParseVersion(current, out var currentVersion) ||
            !TryParseVersion(incoming, out var incomingVersion))
        {
            return string.Equals(current?.Trim(), incoming?.Trim(), StringComparison.OrdinalIgnoreCase)
                ? ThemeVersionRelation.Same
                : ThemeVersionRelation.Unknown;
        }

        var comparison = CompareParsedVersions(incomingVersion, currentVersion);
        return comparison switch
        {
            > 0 => ThemeVersionRelation.Newer,
            < 0 => ThemeVersionRelation.Older,
            _ => ThemeVersionRelation.Same,
        };
    }

    private static int CompareParsedVersions(ParsedVersion left, ParsedVersion right)
    {
        var count = Math.Max(left.Components.Count, right.Components.Count);
        for (var index = 0; index < count; index++)
        {
            var leftValue = index < left.Components.Count ? left.Components[index] : 0;
            var rightValue = index < right.Components.Count ? right.Components[index] : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0) return comparison;
        }

        if (left.PreRelease.Count == 0 && right.PreRelease.Count == 0) return 0;
        if (left.PreRelease.Count == 0) return 1;
        if (right.PreRelease.Count == 0) return -1;

        count = Math.Max(left.PreRelease.Count, right.PreRelease.Count);
        for (var index = 0; index < count; index++)
        {
            if (index >= left.PreRelease.Count) return -1;
            if (index >= right.PreRelease.Count) return 1;
            var comparison = CompareIdentifier(left.PreRelease[index], right.PreRelease[index]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
        var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
        if (leftNumeric && rightNumeric) return leftNumber.CompareTo(rightNumber);
        if (leftNumeric) return -1;
        if (rightNumeric) return 1;
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string? value, out ParsedVersion version)
    {
        version = new ParsedVersion([], []);
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        normalized = normalized.Split('+', 2)[0];
        var parts = normalized.Split('-', 2);
        var componentText = parts[0].Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (componentText.Length == 0) return false;

        var components = new List<int>(componentText.Length);
        foreach (var component in componentText)
        {
            if (!int.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            {
                return false;
            }
            components.Add(parsed);
        }

        var preRelease = parts.Length == 2
            ? parts[1]
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList()
            : [];
        if (parts.Length == 2 && preRelease.Count == 0) return false;
        version = new ParsedVersion(components, preRelease);
        return true;
    }

    private sealed record ParsedVersion(IReadOnlyList<int> Components, IReadOnlyList<string> PreRelease);
}
