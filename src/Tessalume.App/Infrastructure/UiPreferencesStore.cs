using System.IO;
using System.Text.Json;

namespace Tessalume.App.Infrastructure;

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
            return File.Exists(_path)
                ? UiPreferencesMigration.Deserialize(File.ReadAllText(_path), _options, out _)
                : new UiPreferences();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiPreferences();
        }
    }

    public async Task SaveAsync(UiPreferences preferences)
    {
        preferences = UiPreferencesMigration.PrepareForSave(preferences);
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
