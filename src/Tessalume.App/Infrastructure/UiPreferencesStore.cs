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
            if (!File.Exists(_path)) return new UiPreferences();
            var json = File.ReadAllText(_path);
            var preferences = UiPreferencesMigration.Deserialize(json, _options, out var migrated);
            if (migrated)
            {
                if (PreserveMigrationSnapshot(json))
                {
                    PersistMigratedPreferences(preferences);
                }
            }
            return preferences;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiPreferences();
        }
    }

    private bool PreserveMigrationSnapshot(string json)
    {
        var backupsDirectory = Path.Combine(Path.GetDirectoryName(_path)!, "backups");
        var snapshotPath = Path.Combine(backupsDirectory, "latest-before-preferences-migration.json");
        var temporaryPath = snapshotPath + ".tmp";
        try
        {
            Directory.CreateDirectory(backupsDirectory);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, snapshotPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporaryPath); }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    private void PersistMigratedPreferences(UiPreferences preferences)
    {
        var temporaryPath = _path + ".migration.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(UiPreferencesMigration.PrepareForSave(preferences), _options));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporaryPath); }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException) { }
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
