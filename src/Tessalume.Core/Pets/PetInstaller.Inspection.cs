using System.Text.Json;

namespace Tessalume.Core.Pets;

public sealed partial class PetInstaller : IDisposable
{
    private const long MaximumInstalledManifestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions InstalledJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PetPackageLoader _loader;
    private readonly PetInstallerOptions _options;
    private readonly PetManagementStateStore _stateStore;
    private readonly IPetTransactionObserver? _observer;

    public PetInstaller(PetInstallerOptions options, PetPackageLoader? loader = null)
        : this(options, loader ?? new PetPackageLoader(), observer: null)
    {
    }

    internal PetInstaller(
        PetInstallerOptions options,
        PetPackageLoader loader,
        IPetTransactionObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loader);
        _options = options;
        _loader = loader;
        _observer = observer;
        _stateStore = new PetManagementStateStore(options.StatePath);
    }

    public void Dispose()
    {
        _stateStore.Dispose();
        _gate.Dispose();
    }

    public async Task<PetInstallationSnapshot> InspectAsync(
        PetPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await InspectCoreAsync(package, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<PetManagementState> MarkInformationalDisclosureShownAsync(
        CancellationToken cancellationToken = default) =>
        _stateStore.MarkInformationalDisclosureShownAsync(cancellationToken);

    public Task<PetStateLoadResult> LoadManagementStateAsync(
        CancellationToken cancellationToken = default) =>
        _stateStore.LoadAsync(cancellationToken);

    public async Task<PetStateRecoveryResult> RecoverManagementStateAsync(
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _stateStore.RecoverCorruptStateAsync(confirmed, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<PetManagementState> MarkCompanionSuggestionShownAsync(
        string petId,
        CancellationToken cancellationToken = default) =>
        _stateStore.MarkCompanionSuggestionShownAsync(petId, cancellationToken);

    public async Task<bool> TryMarkCompanionSuggestionShownAsync(
        string petId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _stateStore.TryMarkCompanionSuggestionShownAsync(petId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PetInstallationSnapshot> MarkCodexSelectionAcknowledgedAsync(
        PetPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _stateStore.MarkCodexSelectionAcknowledgedAsync(package.Manifest.Id, cancellationToken);
            return await InspectCoreAsync(package, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PetInstallationSnapshot> InspectCoreAsync(
        PetPackage package,
        CancellationToken cancellationToken)
    {
        var stateResult = await _stateStore.LoadAsync(cancellationToken);
        var probes = await ProbeInstalledPetsAsync(package.Manifest.Id, cancellationToken);
        var targetDirectory = GetPetDirectory(package.Manifest.Id);
        var matchingDirectories = probes
            .Where(probe => string.Equals(probe.Manifest?.Id, package.Manifest.Id, StringComparison.OrdinalIgnoreCase))
            .Select(probe => probe.DirectoryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!stateResult.IsValid)
        {
            return Snapshot(
                PetInstallationStatus.Damaged,
                package.Manifest.Id,
                null,
                matchingDirectories,
                stateResult.Error ?? "宠物管理状态已损坏。",
                stateIsValid: false);
        }

        stateResult.State.Pets.TryGetValue(package.Manifest.Id, out var managed);
        var occupiedTarget = probes.FirstOrDefault(probe =>
            string.Equals(probe.DirectoryPath, targetDirectory, StringComparison.OrdinalIgnoreCase));
        if (occupiedTarget?.Manifest is { } occupant &&
            !string.Equals(occupant.Id, package.Manifest.Id, StringComparison.OrdinalIgnoreCase))
        {
            var allAffectedDirectories = matchingDirectories
                .Append(targetDirectory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Snapshot(
                PetInstallationStatus.UnknownModification,
                package.Manifest.Id,
                managed?.ProductVersion,
                allAffectedDirectories,
                $"标准目标目录当前属于其他宠物 ID（{occupant.Id}）" +
                (matchingDirectories.Length == 0
                    ? "；明确替换会先备份并移走该目录。"
                    : $"，另有 {matchingDirectories.Length} 个目录声明目标 ID；明确替换会先备份全部受影响目录。"),
                stateIsValid: true);
        }
        if (matchingDirectories.Length > 1)
        {
            return Snapshot(
                PetInstallationStatus.DuplicateIdConflict,
                package.Manifest.Id,
                managed?.ProductVersion,
                matchingDirectories,
                "多个 Codex 宠物目录声明了同一 ID，需要明确处理冲突。",
                stateIsValid: true);
        }

        if (managed is null)
        {
            if (matchingDirectories.Length == 1)
            {
                return Snapshot(
                    PetInstallationStatus.DuplicateIdConflict,
                    package.Manifest.Id,
                    null,
                    matchingDirectories,
                    "已存在同 ID 的非 Tessalume 受管宠物，拒绝静默覆盖。",
                    stateIsValid: true);
            }
            if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
            {
                return Snapshot(
                    PetInstallationStatus.UnknownModification,
                    package.Manifest.Id,
                    null,
                    [targetDirectory],
                    "目标目录已被未知内容占用，拒绝静默覆盖。",
                    stateIsValid: true);
            }
            return Snapshot(
                PetInstallationStatus.NotInstalled,
                package.Manifest.Id,
                null,
                [],
                "尚未安装到 Codex Pets。",
                stateIsValid: true);
        }

        var managedDirectory = GetPetDirectory(managed.DirectoryName);
        if (!Directory.Exists(managedDirectory))
        {
            return Snapshot(
                PetInstallationStatus.Damaged,
                package.Manifest.Id,
                managed.ProductVersion,
                matchingDirectories,
                "受管宠物目录已经丢失。",
                stateIsValid: true);
        }
        var managedProbe = probes.FirstOrDefault(probe =>
            string.Equals(probe.DirectoryPath, managedDirectory, StringComparison.OrdinalIgnoreCase));
        if (managedProbe?.Manifest is { } changedManifest &&
            !string.Equals(changedManifest.Id, package.Manifest.Id, StringComparison.OrdinalIgnoreCase))
        {
            return Snapshot(
                PetInstallationStatus.UnknownModification,
                package.Manifest.Id,
                managed.ProductVersion,
                matchingDirectories.Append(managedDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                $"受管 pet.json 已被改为其他宠物 ID（{changedManifest.Id}），需要明确替换。",
                stateIsValid: true);
        }
        if (matchingDirectories.Length == 0 ||
            !string.Equals(matchingDirectories[0], managedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return Snapshot(
                matchingDirectories.Length == 0
                    ? PetInstallationStatus.Damaged
                    : PetInstallationStatus.DuplicateIdConflict,
                package.Manifest.Id,
                managed.ProductVersion,
                matchingDirectories.Append(managedDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                "受管目录的 pet.json 已损坏、被替换，或同 ID 宠物被移动到其它目录。",
                stateIsValid: true);
        }

        var integrity = await CheckManagedIntegrityAsync(managedDirectory, managed, cancellationToken);
        if (integrity.Status is not null)
        {
            return Snapshot(
                integrity.Status.Value,
                package.Manifest.Id,
                managed.ProductVersion,
                [managedDirectory],
                integrity.Detail,
                stateIsValid: true);
        }

        if (IsPackageNewerOrDifferent(package, managed))
        {
            return Snapshot(
                PetInstallationStatus.UpdateAvailable,
                package.Manifest.Id,
                managed.ProductVersion,
                [managedDirectory],
                $"已安装 {managed.ProductVersion}，可更新到 {package.Catalog.ProductVersion}。",
                stateIsValid: true);
        }

        return Snapshot(
            managed.SelectionAcknowledged
                ? PetInstallationStatus.Installed
                : PetInstallationStatus.InstalledAwaitingCodexSelection,
            package.Manifest.Id,
            managed.ProductVersion,
            [managedDirectory],
            managed.SelectionAcknowledged
                ? "文件已安装；Tessalume 只记录你已确认完成 Codex 选择，无法自动检测宠物是否正在显示。"
                : "文件已安装，等待你在 Codex Settings → Pets 中刷新并选择。",
            stateIsValid: true);
    }

    private async Task<IReadOnlyList<InstalledPetProbe>> ProbeInstalledPetsAsync(
        string petId,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_options.PetsRoot)) return [];
        PetPathSafety.EnsureRegularDirectory(_options.PetsRoot, _options.PetsRoot);
        var probes = new List<InstalledPetProbe>();
        foreach (var directory in Directory.EnumerateDirectories(
                     _options.PetsRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (name.StartsWith(".tessalume-", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                PetPathSafety.EnsureRegularDirectory(_options.PetsRoot, directory);
                var manifestPath = Path.Combine(directory, PetPackageContract.ManifestFileName);
                if (!File.Exists(manifestPath)) continue;
                PetPathSafety.EnsureRegularFile(_options.PetsRoot, manifestPath);
                if (new FileInfo(manifestPath).Length is <= 0 or > MaximumInstalledManifestBytes)
                {
                    probes.Add(new InstalledPetProbe(directory, null));
                    continue;
                }
                await using var stream = File.OpenRead(manifestPath);
                var manifest = await JsonSerializer.DeserializeAsync<PetManifest>(
                    stream,
                    InstalledJsonOptions,
                    cancellationToken);
                if (manifest is not null &&
                    (string.Equals(manifest.Id, petId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(name, petId, StringComparison.OrdinalIgnoreCase)))
                {
                    probes.Add(new InstalledPetProbe(directory, manifest));
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                if (string.Equals(name, petId, StringComparison.OrdinalIgnoreCase))
                {
                    probes.Add(new InstalledPetProbe(directory, null));
                }
            }
        }
        return probes;
    }

    private async Task<ManagedIntegrityResult> CheckManagedIntegrityAsync(
        string directory,
        PetManagedInstallation managed,
        CancellationToken cancellationToken)
    {
        try
        {
            PetPathSafety.EnsureRegularDirectory(_options.PetsRoot, directory);
            var managedPaths = managed.Files
                .Select(file => file.RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in managed.Files)
            {
                var path = PetPathSafety.ResolveContainedPath(directory, file.RelativePath);
                if (!File.Exists(path))
                {
                    return new ManagedIntegrityResult(
                        PetInstallationStatus.Damaged,
                        $"受管文件缺失：{file.RelativePath}");
                }
                PetPathSafety.EnsureRegularFile(directory, path);
                var info = new FileInfo(path);
                var hash = await PetPackageLoader.ComputeSha256Async(path, cancellationToken);
                if (info.Length != file.Size ||
                    !string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new ManagedIntegrityResult(
                        PetInstallationStatus.UnknownModification,
                        $"受管文件已被未知修改：{file.RelativePath}");
                }
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                PetPathSafety.EnsureRegularFile(directory, path);
                var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                if (!managedPaths.Contains(relative))
                {
                    return new ManagedIntegrityResult(
                        PetInstallationStatus.UnknownModification,
                        $"受管目录包含未知文件：{relative}");
                }
            }
            return new ManagedIntegrityResult(null, string.Empty);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return new ManagedIntegrityResult(PetInstallationStatus.Damaged, exception.Message);
        }
    }

    private static bool IsPackageNewerOrDifferent(
        PetPackage package,
        PetManagedInstallation managed)
    {
        var packagedVersion = Version.Parse(package.Catalog.ProductVersion);
        var managedVersion = Version.Parse(managed.ProductVersion);
        if (packagedVersion > managedVersion) return true;
        if (packagedVersion < managedVersion) return false;

        var packageFiles = package.InstallFiles.ToDictionary(
            file => file.Path,
            file => file,
            StringComparer.OrdinalIgnoreCase);
        return managed.Files.Count != packageFiles.Count || managed.Files.Any(file =>
            !packageFiles.TryGetValue(file.RelativePath, out var packaged) ||
            packaged.Size != file.Size ||
            !string.Equals(packaged.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private string GetPetDirectory(string directoryName)
    {
        if (!PetPathSafety.IsSimpleDirectoryName(directoryName))
        {
            throw new InvalidDataException("宠物目录名无效。");
        }
        var path = Path.GetFullPath(Path.Combine(_options.PetsRoot, directoryName));
        PetPathSafety.EnsureContained(_options.PetsRoot, path);
        return path;
    }

    private static PetInstallationSnapshot Snapshot(
        PetInstallationStatus status,
        string petId,
        string? managedVersion,
        IReadOnlyList<string> directories,
        string detail,
        bool stateIsValid) =>
        new(status, petId, managedVersion, directories, detail, stateIsValid);

    private sealed record InstalledPetProbe(string DirectoryPath, PetManifest? Manifest);

    private sealed record ManagedIntegrityResult(PetInstallationStatus? Status, string Detail);
}
