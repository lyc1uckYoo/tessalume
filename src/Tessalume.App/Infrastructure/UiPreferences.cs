using System.Text.Json;
using Tessalume.App.Creator;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Infrastructure;

internal sealed record UiPreferences
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool DarkMode { get; init; }

    public bool OnboardingCompleted { get; init; }

    public bool AutomaticUpdateChecks { get; init; } = true;

    public DateTimeOffset? LastUpdateCheckAt { get; init; }

    public List<string> FavoriteThemeIds { get; init; } = [];

    public Dictionary<string, ThemeVisualSettings> ThemeVisualSettings { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<ThemeArtworkPreset> ArtworkPresets { get; init; } = [];

    public string ThemeLibrarySort { get; init; } = ThemeLibraryState.DefaultSort;

    public List<ThemeUsageRecord> RecentThemeUsage { get; init; } = [];

    public CreatorPromptDraft CreatorPromptDraft { get; init; } = new();

    public List<CreatorWorkspaceRecord> RecentCreatorWorkspaces { get; init; } = [];
}

internal static class UiPreferencesMigration
{
    public static UiPreferences Deserialize(
        string json,
        JsonSerializerOptions options,
        out bool migrated)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The UI preferences root must be a JSON object.");
        }

        var sourceVersion = ReadSchemaVersion(document.RootElement);
        if (sourceVersion > UiPreferences.CurrentSchemaVersion)
        {
            throw new JsonException(
                $"UI preferences schema {sourceVersion} is newer than supported schema " +
                $"{UiPreferences.CurrentSchemaVersion}.");
        }

        var preferences = sourceVersion switch
        {
            0 => DeserializeVersionZero(json, options),
            1 => DeserializeVersionOne(json, options),
            2 => DeserializeVersionTwo(json, options),
            UiPreferences.CurrentSchemaVersion => DeserializeCurrent(json, options),
            _ => throw new JsonException($"Unsupported UI preferences schema {sourceVersion}."),
        };

        migrated = sourceVersion != UiPreferences.CurrentSchemaVersion;
        return Normalize(preferences);
    }

    public static UiPreferences PrepareForSave(UiPreferences preferences) => Normalize(preferences);

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(UiPreferences.SchemaVersion), out var property) &&
            !root.TryGetProperty("schemaVersion", out property))
        {
            return 0;
        }

        if (!property.TryGetInt32(out var version) || version < 0)
        {
            throw new JsonException("UI preferences schema version must be a non-negative integer.");
        }

        return version;
    }

    private static UiPreferences DeserializeVersionZero(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<UiPreferences>(json, options)
        ?? throw new JsonException("UI preferences could not be read.");

    private static UiPreferences DeserializeVersionOne(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<UiPreferences>(json, options)
        ?? throw new JsonException("UI preferences could not be read.");

    private static UiPreferences DeserializeVersionTwo(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<UiPreferences>(json, options)
        ?? throw new JsonException("UI preferences could not be read.");

    private static UiPreferences DeserializeCurrent(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<UiPreferences>(json, options)
        ?? throw new JsonException("UI preferences could not be read.");

    private static UiPreferences Normalize(UiPreferences preferences) => preferences with
    {
        SchemaVersion = UiPreferences.CurrentSchemaVersion,
        FavoriteThemeIds = preferences.FavoriteThemeIds ?? [],
        ThemeVisualSettings = preferences.ThemeVisualSettings ??
            new Dictionary<string, ThemeVisualSettings>(StringComparer.OrdinalIgnoreCase),
        ArtworkPresets = NormalizeArtworkPresets(preferences.ArtworkPresets),
        ThemeLibrarySort = ThemeLibraryState.NormalizeSort(preferences.ThemeLibrarySort),
        RecentThemeUsage = ThemeLibraryState.NormalizeUsage(preferences.RecentThemeUsage),
        CreatorPromptDraft = (preferences.CreatorPromptDraft ?? new CreatorPromptDraft()).Normalize(),
        RecentCreatorWorkspaces = CreatorWorkspaceStore
            .Normalize(preferences.RecentCreatorWorkspaces)
            .ToList(),
    };

    private static List<ThemeArtworkPreset> NormalizeArtworkPresets(
        IEnumerable<ThemeArtworkPreset>? presets)
    {
        var result = new List<ThemeArtworkPreset>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in presets ?? [])
        {
            if (candidate is null) continue;
            var preset = candidate.Normalize();
            if (string.IsNullOrWhiteSpace(preset.Name) || !names.Add(preset.Name)) continue;
            result.Add(preset);
            if (result.Count == 24) break;
        }
        return result;
    }
}
