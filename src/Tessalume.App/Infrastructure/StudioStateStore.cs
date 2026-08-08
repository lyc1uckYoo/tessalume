using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Infrastructure;

internal sealed record StudioState
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public int Port { get; init; }

    public string ThemeId { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; init; }

    public bool Enabled { get; init; } = true;

    public DateTimeOffset? LastSuccessfulApplyAt { get; init; }

    public string? CodexVersionAtLastApply { get; init; }

    public int RuntimeContractVersion { get; init; }

    public string? CompatibilityPackVersionAtLastApply { get; init; }

    public ThemeRuntimeFailureStage LastFailureStage { get; init; }

    public string? LastFailureMessage { get; init; }

    public DateTimeOffset? LastFailureAt { get; init; }
}

internal sealed class StudioStateStore(string dataDirectory)
{
    private readonly string _statePath = Path.Combine(dataDirectory, "state.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<StudioState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_statePath);
            return await JsonSerializer.DeserializeAsync<StudioState>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(StudioState state, CancellationToken cancellationToken = default)
    {
        state = state with { SchemaVersion = StudioState.CurrentSchemaVersion };
        var temporaryPath = _statePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _statePath, overwrite: true);
    }
}
