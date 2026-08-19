using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tessalume.Core.Updates.Delta;

public sealed class UpdateDeltaManifest
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "Tessalume.update.json";
    public const string TargetExecutableName = "Tessalume.exe";

    public int SchemaVersion { get; init; }
    public string TargetVersion { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public long TargetSize { get; init; }
    public string TargetSha256 { get; init; } = string.Empty;
    public IReadOnlyList<UpdateDeltaEntry> Deltas { get; init; } = [];

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

public sealed class UpdateDeltaEntry
{
    public const string SupportedAlgorithm = "octodiff-v1";

    public string FromVersion { get; init; } = string.Empty;
    public string FromSha256 { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public long AssetSize { get; init; }
    public string AssetSha256 { get; init; } = string.Empty;
}

public sealed record ReleaseDeltaAsset(
    string FromVersion,
    string FromSha256,
    string Algorithm,
    string AssetName,
    Uri DownloadUri,
    long DownloadSize,
    string Sha256);
