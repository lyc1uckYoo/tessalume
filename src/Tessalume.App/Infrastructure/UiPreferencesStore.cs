using System.IO;
using System.Text.Json;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Infrastructure;

internal sealed record UiPreferences
{
    public bool DarkMode { get; init; }

    public bool OnboardingCompleted { get; init; }

    public bool AutomaticUpdateChecks { get; init; } = true;

    public DateTimeOffset? LastUpdateCheckAt { get; init; }

    public List<string> FavoriteThemeIds { get; init; } = [];

    public Dictionary<string, ThemeVisualSettings> ThemeVisualSettings { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class UiPreferencesStore(string dataDirectory) : IDisposable
{
    private readonly string _path = Path.Combine(dataDirectory, "ui-settings.json");
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public bool Exists => File.Exists(_path);

    public UiPreferences Load()
    {
        try
        {
            var preferences = File.Exists(_path)
                ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(_path), _options) ?? new UiPreferences()
                : new UiPreferences();
            return preferences with
            {
                FavoriteThemeIds = preferences.FavoriteThemeIds ?? [],
                ThemeVisualSettings = preferences.ThemeVisualSettings ??
                    new Dictionary<string, ThemeVisualSettings>(StringComparer.OrdinalIgnoreCase),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiPreferences();
        }
    }

    public async Task SaveAsync(UiPreferences preferences)
    {
        await _saveGate.WaitAsync();
        var temporaryPath = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(preferences, _options));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
            _saveGate.Release();
        }
    }

    public void Dispose() => _saveGate.Dispose();
}
