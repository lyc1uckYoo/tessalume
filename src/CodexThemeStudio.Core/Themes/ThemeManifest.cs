using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexThemeStudio.Core.Themes;

public sealed record ThemeManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 2;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("engineVersion")]
    public int EngineVersion { get; init; } = 2;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "advanced";

    [JsonPropertyName("template")]
    public ThemeTemplate? Template { get; init; }

    [JsonIgnore]
    public bool UsesSharedTemplateV1 =>
        Template is { } template &&
        string.Equals(template.Id, "flagship", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.Version, "1.0", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.Style, "shared", StringComparison.OrdinalIgnoreCase);

    [JsonPropertyName("capabilities")]
    public ThemeCapabilities Capabilities { get; init; } = new();

    [JsonPropertyName("entryPoints")]
    public ThemeEntryPoints EntryPoints { get; init; } = new();

    [JsonPropertyName("previews")]
    public ThemePreviews Previews { get; init; } = new();

    [JsonPropertyName("assets")]
    public Dictionary<string, string> Assets { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("config")]
    public Dictionary<string, JsonElement> Config { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("compatibility")]
    public ThemeCompatibility Compatibility { get; init; } = new();
}

public sealed record ThemeTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("style")]
    public string Style { get; init; } = string.Empty;
}

public sealed record ThemeCompatibility
{
    [JsonPropertyName("petOverlay")]
    public bool PetOverlay { get; init; }
}

public sealed record ThemeCapabilities
{
    [JsonPropertyName("light")]
    public bool Light { get; init; } = true;

    [JsonPropertyName("dark")]
    public bool Dark { get; init; }
}

public sealed record ThemeEntryPoints
{
    [JsonPropertyName("css")]
    public string? Css { get; init; }

    [JsonPropertyName("script")]
    public string? Script { get; init; }
}

public sealed record ThemePreviews
{
    [JsonPropertyName("light")]
    public string? Light { get; init; }

    [JsonPropertyName("dark")]
    public string? Dark { get; init; }
}
