using System.Text.Json;
using Tessalume.App.Creator;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Infrastructure;

internal sealed class UnsupportedUiPreferencesSchemaException(
    int sourceVersion,
    int supportedVersion) : JsonException(
        $"UI preferences schema {sourceVersion} is newer than supported schema {supportedVersion}.")
{
    public int SourceVersion { get; } = sourceVersion;

    public int SupportedVersion { get; } = supportedVersion;
}

internal sealed record UiPreferences
{
    public const int CurrentSchemaVersion = 7;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool DarkMode { get; init; }

    public bool OnboardingCompleted { get; init; }

    public bool AutomaticUpdateChecks { get; init; } = true;

    public bool QuickSwitchVisible { get; init; } = true;

    public DateTimeOffset? LastUpdateCheckAt { get; init; }

    public List<string> FavoriteThemeIds { get; init; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, ThemeVisualSettings> ThemeVisualSettings { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ThemeVisualSettingsOverride> ThemeVisualOverrides { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string ThemeLibrarySort { get; init; } = ThemeLibraryState.DefaultSort;

    public List<ThemeUsageRecord> RecentThemeUsage { get; init; } = [];

    public CreatorPromptDraft CreatorPromptDraft { get; init; } = new();

    public Dictionary<string, CreatorPromptDraft> CreatorPromptDrafts { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

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
            throw new UnsupportedUiPreferencesSchemaException(
                sourceVersion,
                UiPreferences.CurrentSchemaVersion);
        }

        var preferences = sourceVersion switch
        {
            0 => DeserializeVersionZero(json, options),
            1 => DeserializeVersionOne(json, options),
            2 => DeserializeVersionTwo(json, options),
            3 => DeserializeVersionThree(json, options),
            4 => DeserializeVersionFour(json, options),
            5 => DeserializeVersionFive(json, options),
            6 => DeserializeVersionSix(json, options),
            UiPreferences.CurrentSchemaVersion => DeserializeCurrent(json, options),
            _ => throw new JsonException($"Unsupported UI preferences schema {sourceVersion}."),
        };

        if (sourceVersion < UiPreferences.CurrentSchemaVersion)
        {
            preferences = preferences with
            {
                ThemeVisualSettings = ReadLegacyVisualSettings(document.RootElement, options),
            };
        }

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

    private static Dictionary<string, ThemeVisualSettings> ReadLegacyVisualSettings(
        JsonElement root,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(nameof(UiPreferences.ThemeVisualSettings), out var value) &&
            !root.TryGetProperty("themeVisualSettings", out value))
        {
            return new Dictionary<string, ThemeVisualSettings>(StringComparer.OrdinalIgnoreCase);
        }
        return JsonSerializer.Deserialize<Dictionary<string, ThemeVisualSettings>>(
                   value.GetRawText(),
                   options) ??
               new Dictionary<string, ThemeVisualSettings>(StringComparer.OrdinalIgnoreCase);
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

    private static UiPreferences DeserializeVersionThree(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<UiPreferences>(json, options)
        ?? throw new JsonException("UI preferences could not be read.");

    private static UiPreferences DeserializeVersionFour(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<UiPreferences>(json, options)
        ?? throw new JsonException("UI preferences could not be read.");

    private static UiPreferences DeserializeVersionFive(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<UiPreferences>(json, options)
        ?? throw new JsonException("UI preferences could not be read.");

    private static UiPreferences DeserializeVersionSix(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<UiPreferences>(json, options)
        ?? throw new JsonException("UI preferences could not be read.");

    private static UiPreferences DeserializeCurrent(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<UiPreferences>(json, options)
        ?? throw new JsonException("UI preferences could not be read.");

    private static UiPreferences Normalize(UiPreferences preferences)
    {
        var overrides = NormalizeVisualOverrides(preferences.ThemeVisualOverrides);
        foreach (var (themeId, legacySettings) in preferences.ThemeVisualSettings ??
                     new Dictionary<string, ThemeVisualSettings>(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(themeId) || overrides.ContainsKey(themeId)) continue;
            var migratedOverride = ThemeArtworkSettingsResolver.MigrateSchemaFive(
                legacySettings ?? new ThemeVisualSettings());
            if (!migratedOverride.IsEmpty) overrides[themeId] = migratedOverride;
        }

        return preferences with
        {
            SchemaVersion = UiPreferences.CurrentSchemaVersion,
            FavoriteThemeIds = preferences.FavoriteThemeIds ?? [],
            // Schema six and later persist only sparse overrides. This property remains as a
            // deserialization bridge for schema five and earlier files.
            ThemeVisualSettings = new Dictionary<string, ThemeVisualSettings>(
                StringComparer.OrdinalIgnoreCase),
            ThemeVisualOverrides = overrides,
            ThemeLibrarySort = ThemeLibraryState.NormalizeSort(preferences.ThemeLibrarySort),
            RecentThemeUsage = ThemeLibraryState.NormalizeUsage(preferences.RecentThemeUsage),
            CreatorPromptDrafts = CreatorPromptDraftStore.Normalize(
                preferences.CreatorPromptDrafts,
                preferences.CreatorPromptDraft),
            CreatorPromptDraft = new CreatorPromptDraft(),
            RecentCreatorWorkspaces = CreatorWorkspaceStore
                .Normalize(preferences.RecentCreatorWorkspaces)
                .ToList(),
        };
    }

    private static Dictionary<string, ThemeVisualSettingsOverride> NormalizeVisualOverrides(
        IReadOnlyDictionary<string, ThemeVisualSettingsOverride>? source)
    {
        var result = new Dictionary<string, ThemeVisualSettingsOverride>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (themeId, value) in source ??
                     new Dictionary<string, ThemeVisualSettingsOverride>(
                         StringComparer.OrdinalIgnoreCase))
        {
            var key = (themeId ?? string.Empty).Trim();
            if (key.Length == 0 || key.Length > 256 || value is null) continue;
            var normalized = value.Normalize();
            if (!normalized.IsEmpty) result[key] = normalized;
        }
        return result;
    }

}
