using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace Tessalume.Core.Pets;

public sealed record PetManagedFile
{
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }
}

public sealed record PetManagedInstallation
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("productVersion")]
    public string ProductVersion { get; init; } = string.Empty;

    [JsonPropertyName("directoryName")]
    public string DirectoryName { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public IReadOnlyList<PetManagedFile> Files { get; init; } = [];

    [JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("selectionAcknowledged")]
    public bool SelectionAcknowledged { get; init; }
}

public sealed record PetManagementState
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonPropertyName("informationalDisclosureShown")]
    public bool InformationalDisclosureShown { get; init; }

    [JsonPropertyName("companionSuggestionShownIds")]
    public IReadOnlyList<string> CompanionSuggestionShownIds { get; init; } = [];

    [JsonPropertyName("pets")]
    public IReadOnlyDictionary<string, PetManagedInstallation> Pets { get; init; } =
        new Dictionary<string, PetManagedInstallation>(StringComparer.OrdinalIgnoreCase);
}

public sealed record PetStateLoadResult(
    PetManagementState State,
    bool Exists,
    bool IsValid,
    string? Error = null);

public sealed class PetManagementStateStore : IDisposable
{
    private const long MaximumStateBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _statePath;

    public PetManagementStateStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = Path.GetFullPath(statePath);
        if (string.IsNullOrWhiteSpace(Path.GetFileName(_statePath)))
        {
            throw new ArgumentException("宠物状态路径必须指向文件。", nameof(statePath));
        }
    }

    public string StatePath => _statePath;

    public void Dispose() => _gate.Dispose();

    public async Task<PetStateLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        PetManagementState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(NormalizeAndValidate(state), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<PetManagementState> MarkInformationalDisclosureShownAsync(
        CancellationToken cancellationToken = default) =>
        MutateAsync(state => state with { InformationalDisclosureShown = true }, cancellationToken);

    public Task<PetManagementState> MarkCompanionSuggestionShownAsync(
        string petId,
        CancellationToken cancellationToken = default)
    {
        ValidatePetId(petId);
        return MutateAsync(
            state => state.CompanionSuggestionShownIds.Contains(petId, StringComparer.OrdinalIgnoreCase)
                ? state
                : state with
                {
                    CompanionSuggestionShownIds = state.CompanionSuggestionShownIds
                        .Append(petId)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                },
            cancellationToken);
    }

    public async Task<bool> TryMarkCompanionSuggestionShownAsync(
        string petId,
        CancellationToken cancellationToken = default)
    {
        ValidatePetId(petId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var loaded = await LoadCoreAsync(cancellationToken);
            if (!loaded.IsValid)
            {
                throw new InvalidDataException(loaded.Error ?? "宠物管理状态已损坏，拒绝覆盖。");
            }
            if (loaded.State.Pets.ContainsKey(petId) ||
                loaded.State.CompanionSuggestionShownIds.Contains(petId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            var next = NormalizeAndValidate(loaded.State with
            {
                CompanionSuggestionShownIds = loaded.State.CompanionSuggestionShownIds
                    .Append(petId)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            });
            await SaveCoreAsync(next, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PetStateRecoveryResult> RecoverCorruptStateAsync(
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException("恢复损坏的宠物管理状态需要明确确认。");
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var loaded = await LoadCoreAsync(cancellationToken);
            if (loaded.IsValid)
            {
                return new PetStateRecoveryResult(false, null, loaded.State);
            }
            var parent = Path.GetDirectoryName(_statePath)!;
            PetPathSafety.EnsureRegularDirectory(parent, parent);
            PetPathSafety.EnsureRegularFile(parent, _statePath);
            if (new FileInfo(_statePath).Length > MaximumStateBytes * 4)
            {
                throw new InvalidDataException("损坏的宠物管理状态超过 4 MiB，拒绝自动归档。");
            }

            var archiveName = $"{Path.GetFileName(_statePath)}.corrupt-" +
                              $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.bak";
            var archivePath = Path.Combine(parent, archiveName);
            var temporaryArchive = Path.Combine(parent, $".{archiveName}.{Guid.NewGuid():N}.tmp");
            PetPathSafety.EnsureContained(parent, archivePath);
            PetPathSafety.EnsureContained(parent, temporaryArchive);
            try
            {
                File.Copy(_statePath, temporaryArchive, overwrite: false);
                var sourceHash = await ComputeStateHashAsync(_statePath, cancellationToken);
                var archiveHash = await ComputeStateHashAsync(temporaryArchive, cancellationToken);
                if (!string.Equals(sourceHash, archiveHash, StringComparison.Ordinal))
                {
                    throw new IOException("损坏状态的归档副本校验失败。");
                }
                File.Move(temporaryArchive, archivePath, overwrite: false);
                var emptyState = new PetManagementState();
                await SaveCoreAsync(emptyState, cancellationToken);
                return new PetStateRecoveryResult(true, archivePath, emptyState);
            }
            catch
            {
                TryDeleteFile(temporaryArchive);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<PetManagementState> MarkCodexSelectionAcknowledgedAsync(
        string petId,
        CancellationToken cancellationToken = default)
    {
        ValidatePetId(petId);
        return MutateAsync(
            state =>
            {
                if (!state.Pets.TryGetValue(petId, out var installation))
                {
                    throw new InvalidOperationException("只能确认由 Tessalume 管理的已安装宠物。");
                }
                if (installation.SelectionAcknowledged) return state;
                var pets = state.Pets.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                pets[petId] = installation with { SelectionAcknowledged = true };
                return state with { Pets = pets };
            },
            cancellationToken);
    }

    internal async Task<PetManagementState> MutateAsync(
        Func<PetManagementState, PetManagementState> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var loaded = await LoadCoreAsync(cancellationToken);
            if (!loaded.IsValid)
            {
                throw new InvalidDataException(loaded.Error ?? "宠物管理状态已损坏，拒绝覆盖。");
            }
            var next = NormalizeAndValidate(mutation(loaded.State));
            if (next == loaded.State) return next;
            await SaveCoreAsync(next, cancellationToken);
            return next;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PetStateLoadResult> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(_statePath))
        {
            return Invalid("宠物管理状态路径被目录占用。", exists: true);
        }
        if (!File.Exists(_statePath))
        {
            return new PetStateLoadResult(new PetManagementState(), false, true);
        }
        try
        {
            var info = new FileInfo(_statePath);
            if (info.Length <= 0 || info.Length > MaximumStateBytes)
            {
                return Invalid("宠物管理状态为空或超过 1 MiB。", exists: true);
            }
            var parent = Path.GetDirectoryName(_statePath)!;
            PetPathSafety.EnsureRegularDirectory(parent, parent);
            PetPathSafety.EnsureRegularFile(parent, _statePath);
            await using var stream = new FileStream(
                _statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<PetManagementState>(
                stream,
                JsonOptions,
                cancellationToken);
            if (state is null) return Invalid("宠物管理状态内容为空。", exists: true);
            return new PetStateLoadResult(NormalizeAndValidate(state), true, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or ArgumentException or NotSupportedException)
        {
            return Invalid($"宠物管理状态不可读取：{exception.Message}", exists: true);
        }
    }

    private async Task SaveCoreAsync(
        PetManagementState state,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(parent);
        PetPathSafety.EnsureRegularDirectory(parent, parent);
        PetPathSafety.EnsureNoReparsePoints(parent, _statePath);
        var temporaryPath = Path.Combine(parent, $".{Path.GetFileName(_statePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    internal static PetManagementState NormalizeAndValidate(PetManagementState state)
    {
        if (state.SchemaVersion != PetManagementState.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"宠物管理状态 schema {state.SchemaVersion} 不受支持。");
        }
        var suggestions = state.CompanionSuggestionShownIds ?? [];
        if (suggestions.Any(string.IsNullOrWhiteSpace) ||
            suggestions.Any(suggestion => !PetPathSafety.IsValidPetId(suggestion)) ||
            suggestions.Count != suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidDataException("宠物配套提示状态包含空值或重复 ID。");
        }

        var sourcePets = state.Pets ??
                         new Dictionary<string, PetManagedInstallation>(StringComparer.OrdinalIgnoreCase);
        var pets = new Dictionary<string, PetManagedInstallation>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, rawInstallation) in sourcePets)
        {
            if (rawInstallation is null)
            {
                throw new InvalidDataException($"宠物 {key} 的受管安装记录不能是 null。");
            }
            var installation = rawInstallation with { Files = rawInstallation.Files ?? [] };
            ValidatePetId(key);
            if (!string.Equals(key, installation.Id, StringComparison.OrdinalIgnoreCase) ||
                !Version.TryParse(installation.ProductVersion, out _) ||
                !PetPathSafety.IsSimpleDirectoryName(installation.DirectoryName) ||
                installation.InstalledAt == default || installation.UpdatedAt == default ||
                installation.Files.Count == 0)
            {
                throw new InvalidDataException($"宠物 {key} 的受管安装记录无效。");
            }
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in installation.Files)
            {
                if (file is null ||
                    !PetPathSafety.IsSafeRelativePath(file.RelativePath) ||
                    file.RelativePath.Contains('\\') || !paths.Add(file.RelativePath) ||
                    file.Sha256 is null || file.Sha256.Length != 64 ||
                    !file.Sha256.All(Uri.IsHexDigit) || file.Size <= 0)
                {
                    throw new InvalidDataException($"宠物 {key} 的受管文件记录无效。");
                }
            }
            if (!pets.TryAdd(key, installation))
            {
                throw new InvalidDataException($"宠物管理状态重复声明了 ID：{key}");
            }
        }
        return state with
        {
            SchemaVersion = PetManagementState.CurrentSchemaVersion,
            CompanionSuggestionShownIds = suggestions
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Pets = pets,
        };
    }

    private static void ValidatePetId(string petId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        if (!PetPathSafety.IsValidPetId(petId))
        {
            throw new ArgumentException("宠物 ID 格式无效。", nameof(petId));
        }
    }

    private static PetStateLoadResult Invalid(string error, bool exists) =>
        new(new PetManagementState(), exists, false, error);

    private static async Task<string> ComputeStateHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
