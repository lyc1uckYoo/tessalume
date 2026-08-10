using System.IO;

namespace Tessalume.App.Creator;

internal sealed class CreatorPromptDraftStore
{
    public const string NewThemeKey = "$new-theme";
    private const int MaximumDrafts = 24;

    private readonly Dictionary<string, CreatorPromptDraft> _drafts;

    public CreatorPromptDraftStore(
        IReadOnlyDictionary<string, CreatorPromptDraft>? drafts = null,
        CreatorPromptDraft? legacyDraft = null) =>
        _drafts = Normalize(drafts, legacyDraft);

    public CreatorPromptDraft Get(string? workspacePath)
    {
        var key = ResolveKey(workspacePath);
        return _drafts.TryGetValue(key, out var draft)
            ? draft.Normalize()
            : new CreatorPromptDraft();
    }

    public void Set(string? workspacePath, CreatorPromptDraft draft) =>
        _drafts[ResolveKey(workspacePath)] = (draft ?? new CreatorPromptDraft()).Normalize();

    public Dictionary<string, CreatorPromptDraft> Snapshot() => _drafts
        .OrderBy(pair => pair.Key == NewThemeKey ? 0 : 1)
        .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .Take(MaximumDrafts)
        .ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Normalize(),
            StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, CreatorPromptDraft> Normalize(
        IReadOnlyDictionary<string, CreatorPromptDraft>? drafts,
        CreatorPromptDraft? legacyDraft = null)
    {
        var result = new Dictionary<string, CreatorPromptDraft>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in drafts ?? new Dictionary<string, CreatorPromptDraft>())
        {
            if (value is null || !TryNormalizeKey(key, out var normalizedKey)) continue;
            result[normalizedKey] = value.Normalize();
        }

        var legacy = (legacyDraft ?? new CreatorPromptDraft()).Normalize();
        if (!result.ContainsKey(NewThemeKey) && IsMeaningful(legacy) && !IsUntouchedLegacyDemo(legacy))
        {
            result[NewThemeKey] = legacy;
        }
        return result
            .OrderBy(pair => pair.Key == NewThemeKey ? 0 : 1)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumDrafts)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveKey(string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath)) return NewThemeKey;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath.Trim()));
    }

    private static bool TryNormalizeKey(string? key, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (string.Equals(key.Trim(), NewThemeKey, StringComparison.OrdinalIgnoreCase))
        {
            normalized = NewThemeKey;
            return true;
        }
        try
        {
            normalized = ResolveKey(key);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsMeaningful(CreatorPromptDraft draft) =>
        !string.IsNullOrWhiteSpace(draft.WorkName) ||
        !string.IsNullOrWhiteSpace(draft.CharacterName) ||
        !string.IsNullOrWhiteSpace(draft.VisualDirection) ||
        !string.IsNullOrWhiteSpace(draft.SpecialRequirements) ||
        draft.UsesReferenceImages;

    private static bool IsUntouchedLegacyDemo(CreatorPromptDraft draft) =>
        string.Equals(draft.WorkName, "鸣潮", StringComparison.Ordinal) &&
        string.Equals(draft.CharacterName, "椿", StringComparison.Ordinal) &&
        string.IsNullOrWhiteSpace(draft.VisualDirection) &&
        string.IsNullOrWhiteSpace(draft.SpecialRequirements) &&
        !draft.UsesReferenceImages;
}
