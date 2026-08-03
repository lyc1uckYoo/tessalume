using System.IO;
using System.Text.Json;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Infrastructure;

internal sealed record UiPreferences
{
    public bool DarkMode { get; init; }

    public List<string> FavoriteThemeIds { get; init; } = [];

    public Dictionary<string, ThemeVisualSettings> ThemeVisualSettings { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class UiPreferencesStore(string dataDirectory)
{
    private readonly string _path = Path.Combine(dataDirectory, "ui-settings.json");
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public UiPreferences Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(_path), _options) ?? new UiPreferences()
                : new UiPreferences();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new UiPreferences();
        }
    }

    public async Task SaveAsync(UiPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(preferences, _options));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
