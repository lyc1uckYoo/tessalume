using System.Text.Json;

var root = AppContext.BaseDirectory;
var modePath = Path.Combine(root, "fixture-mode.txt");
var mode = File.Exists(modePath) ? File.ReadAllText(modePath).Trim() : "exit";
var data = Path.Combine(root, "data");
var preferencesPath = Path.Combine(data, "ui-settings.json");

if (args is ["--update-health", var token])
{
    if (mode == "exit") return 91;
    var nextSettings = Path.Combine(root, "fixture-next-settings.json");
    if (mode == "healthy-migrate" && File.Exists(nextSettings))
    {
        Directory.CreateDirectory(data);
        File.Copy(nextSettings, preferencesPath, overwrite: true);
    }
    var versionPath = Path.Combine(root, "fixture-version.txt");
    var version = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : "v2.0.0";
    var healthDirectory = Path.Combine(data, "updates", "health");
    Directory.CreateDirectory(healthDirectory);
    var healthPath = Path.Combine(healthDirectory, $"{token}.json");
    await File.WriteAllTextAsync(
        healthPath,
        JsonSerializer.Serialize(new
        {
            Token = token,
            ProcessId = Environment.ProcessId,
            VersionLabel = version,
            ConfirmedAt = DateTimeOffset.Now,
        }));
    await Task.Delay(2000);
    return 0;
}

if (mode == "require-schema-3-stable")
{
    if (!File.Exists(preferencesPath) ||
        !File.ReadAllText(preferencesPath).Contains("\"SchemaVersion\":3", StringComparison.Ordinal))
    {
        return 92;
    }
    await Task.Delay(9000);
    return 0;
}

return 91;
