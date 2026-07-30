using System.IO;
using System.Text.Json;

namespace CodexThemeStudio.App.Infrastructure;

internal sealed record StudioState
{
    public int Port { get; init; }

    public string ThemeId { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; init; }

    public bool Enabled { get; init; } = true;
}

internal sealed class StudioStateStore(string dataDirectory)
{
    private readonly string _statePath = Path.Combine(dataDirectory, "state.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<StudioState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_statePath);
            return await JsonSerializer.DeserializeAsync<StudioState>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(StudioState state, CancellationToken cancellationToken = default)
    {
        var temporaryPath = _statePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _statePath, overwrite: true);
    }
}
