using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Tessalume.Core.Runtime;

namespace Tessalume.Core.Compatibility;

public sealed record CompatibilityPackResolution(
    Version PackVersion,
    string PackVersionLabel,
    bool IsBuiltIn,
    ThemeRuntimeAssets RuntimeAssets);

public sealed record CompatibilityPackInstallResult(
    bool Changed,
    CompatibilityPackResolution ActivePack,
    CompatibilityPackResolution PreviousPack);

public sealed class CompatibilityPackStore
{
    public const int ManifestSchemaVersion = 1;
    public const string ManifestFileName = "compatibility-pack.json";
    public const string RuntimeFileName = "theme-runtime-v2.js";
    public const string ProfileFileName = "compatibility-profile-v3.json";

    private const long MaximumArchiveBytes = 4L * 1024L * 1024L;
    private const long MaximumExpandedBytes = 8L * 1024L * 1024L;
    private const int MaximumManifestBytes = 128 * 1024;
    private const int MaximumEntryCount = 8;
    private static readonly string[] RequiredSelectorGroups =
    [
        "main",
        "homeIcon",
        "homeAncestor",
        "sidebar",
        "workspace",
        "composerLegacySurface",
        "composerEditor",
        "composerRootAncestor",
        "composerBodyAncestor",
        "composerFooter",
        "settingsSurface",
        "settingsScrollChild",
        "windowBar",
        "markdownContent",
        "messageUnitAncestor",
        "userMessageBubble",
        "chatPaperAncestor",
        "taskHeader",
        "outputPanelItem",
        "homeSuggestions",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _builtInDirectory;
    private readonly string _sharedTemplateStylePath;
    private readonly string _packsDirectory;
    private readonly string _statePath;
    private readonly Version _currentAppVersion;
    private readonly int _requiredRuntimeContractVersion;
    private readonly CompatibilityPackResolution _builtInPack;

    public CompatibilityPackStore(
        string builtInDirectory,
        string dataDirectory,
        Version currentAppVersion,
        int requiredRuntimeContractVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(builtInDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(currentAppVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredRuntimeContractVersion);

        _builtInDirectory = Path.GetFullPath(builtInDirectory);
        _sharedTemplateStylePath = Path.Combine(
            _builtInDirectory,
            ThemePayloadBuilder.SharedTemplateStyleFileName);
        var compatibilityDataDirectory = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "compatibility");
        _packsDirectory = Path.Combine(compatibilityDataDirectory, "packs");
        _statePath = Path.Combine(compatibilityDataDirectory, "state.json");
        _currentAppVersion = currentAppVersion;
        _requiredRuntimeContractVersion = requiredRuntimeContractVersion;
        Directory.CreateDirectory(_packsDirectory);
        _builtInPack = LoadBuiltInPack();
    }

    public CompatibilityPackResolution Resolve()
    {
        lock (_gate)
        {
            return ResolveAndRepairState();
        }
    }

    public async Task<CompatibilityPackInstallResult> InstallAsync(
        string archivePath,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ValidateSha256(expectedSha256, "兼容补丁包");
        archivePath = Path.GetFullPath(archivePath);
        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists || archiveInfo.Length <= 0 || archiveInfo.Length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("兼容补丁包大小无效或超出安全限制。");
        }

        var actualArchiveHash = await CalculateFileHashAsync(archivePath, cancellationToken);
        if (!string.Equals(actualArchiveHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("兼容补丁包的 SHA-256 校验失败，已拒绝安装。");
        }

        CompatibilityPackManifest manifest;
        byte[] manifestBytes;
        var stagingDirectory = Path.Combine(_packsDirectory, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count is < 3 or > MaximumEntryCount)
            {
                throw new InvalidDataException("兼容补丁包文件数量无效。");
            }

            var entries = archive.Entries.ToDictionary(
                entry => NormalizeArchiveEntry(entry.FullName),
                StringComparer.OrdinalIgnoreCase);
            if (!entries.TryGetValue(ManifestFileName, out var manifestEntry))
            {
                throw new InvalidDataException("兼容补丁包缺少清单文件。");
            }
            manifestBytes = await ReadEntryAsync(
                manifestEntry,
                MaximumManifestBytes,
                cancellationToken);
            manifest = JsonSerializer.Deserialize<CompatibilityPackManifest>(manifestBytes, JsonOptions)
                ?? throw new InvalidDataException("兼容补丁包清单为空。");
            ValidateManifest(manifest);

            var allowedEntries = manifest.Files.Keys
                .Append(ManifestFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (entries.Keys.Any(name => !allowedEntries.Contains(name)) ||
                allowedEntries.Any(name => !entries.ContainsKey(name)))
            {
                throw new InvalidDataException("兼容补丁包包含未声明文件或缺少必需文件。");
            }

            long expandedBytes = manifestBytes.Length;
            foreach (var (fileName, expectedFileHash) in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = entries[fileName];
                expandedBytes += entry.Length;
                if (entry.Length <= 0 || expandedBytes > MaximumExpandedBytes)
                {
                    throw new InvalidDataException("兼容补丁包展开大小无效或超出安全限制。");
                }

                var destination = Path.Combine(stagingDirectory, fileName);
                await using (var input = entry.Open())
                await using (var output = new FileStream(
                                 destination,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 64 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await input.CopyToAsync(output, cancellationToken);
                }

                var actualFileHash = await CalculateFileHashAsync(destination, cancellationToken);
                if (!string.Equals(actualFileHash, expectedFileHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"兼容补丁文件 {fileName} 的 SHA-256 校验失败。");
                }
            }

            await File.WriteAllBytesAsync(
                Path.Combine(stagingDirectory, ManifestFileName),
                manifestBytes,
                cancellationToken);
            _ = LoadInstalledPack(stagingDirectory, manifest);

            lock (_gate)
            {
                var previous = ResolveAndRepairState();
                var finalDirectory = GetPackDirectory(manifest.PackVersion);
                CompatibilityPackResolution installed;
                if (Directory.Exists(finalDirectory))
                {
                    installed = LoadInstalledPack(finalDirectory);
                    Directory.Delete(stagingDirectory, recursive: true);
                }
                else
                {
                    Directory.Move(stagingDirectory, finalDirectory);
                    installed = LoadInstalledPack(finalDirectory, manifest);
                }

                var state = LoadState();
                var changed = previous.PackVersion != installed.PackVersion || previous.IsBuiltIn;
                SaveState(new CompatibilityPackState
                {
                    ActivePackVersion = installed.PackVersion.ToString(),
                    PreviousPackVersion = changed && !previous.IsBuiltIn
                        ? previous.PackVersion.ToString()
                        : state?.PreviousPackVersion,
                });
                return new CompatibilityPackInstallResult(changed, installed, previous);
            }
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
            throw;
        }
    }

    public CompatibilityPackResolution Rollback()
    {
        lock (_gate)
        {
            var state = LoadState();
            var active = ResolveAndRepairState();
            if (active.IsBuiltIn)
            {
                return active;
            }

            var fallback = TryLoadInstalledPack(state?.PreviousPackVersion) ?? _builtInPack;
            SaveState(new CompatibilityPackState
            {
                ActivePackVersion = fallback.IsBuiltIn ? null : fallback.PackVersion.ToString(),
                PreviousPackVersion = null,
            });
            return fallback;
        }
    }

    private CompatibilityPackResolution ResolveAndRepairState()
    {
        var state = LoadState();
        var active = TryLoadInstalledPack(state?.ActivePackVersion);
        if (active is not null)
        {
            return active;
        }

        var previous = TryLoadInstalledPack(state?.PreviousPackVersion);
        if (previous is not null)
        {
            SaveState(new CompatibilityPackState
            {
                ActivePackVersion = previous.PackVersion.ToString(),
                PreviousPackVersion = null,
            });
            return previous;
        }

        if (state is not null &&
            (!string.IsNullOrWhiteSpace(state.ActivePackVersion) ||
             !string.IsNullOrWhiteSpace(state.PreviousPackVersion)))
        {
            SaveState(new CompatibilityPackState());
        }
        return _builtInPack;
    }

    private CompatibilityPackResolution? TryLoadInstalledPack(string? packVersion)
    {
        if (!TryParseVersion(packVersion, out var version)) return null;
        try
        {
            return LoadInstalledPack(GetPackDirectory(version.ToString()));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private CompatibilityPackResolution LoadBuiltInPack()
    {
        var runtimePath = Path.Combine(_builtInDirectory, RuntimeFileName);
        var profilePath = Path.Combine(_builtInDirectory, ProfileFileName);
        EnsureRequiredAsset(runtimePath);
        EnsureRequiredAsset(_sharedTemplateStylePath);
        EnsureRequiredAsset(profilePath);
        var profileVersion = ValidateProfile(profilePath, expectedVersion: null);
        return new CompatibilityPackResolution(
            profileVersion,
            $"v{profileVersion}",
            true,
            new ThemeRuntimeAssets(runtimePath, _sharedTemplateStylePath, profilePath));
    }

    private CompatibilityPackResolution LoadInstalledPack(string directory) =>
        LoadInstalledPack(
            directory,
            JsonSerializer.Deserialize<CompatibilityPackManifest>(
                File.ReadAllBytes(Path.Combine(directory, ManifestFileName)),
                JsonOptions) ?? throw new InvalidDataException("兼容补丁包清单为空。"));

    private CompatibilityPackResolution LoadInstalledPack(
        string directory,
        CompatibilityPackManifest manifest)
    {
        ValidateManifest(manifest);
        directory = Path.GetFullPath(directory);
        EnsureContained(_packsDirectory, directory);
        foreach (var (fileName, expectedHash) in manifest.Files)
        {
            var path = Path.Combine(directory, fileName);
            EnsureRequiredAsset(path);
            var actualHash = CalculateFileHash(path);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"兼容补丁文件 {fileName} 已损坏。");
            }
        }

        var packVersion = ParseVersion(manifest.PackVersion, "packVersion");
        _ = ValidateProfile(Path.Combine(directory, manifest.Profile), packVersion);
        return new CompatibilityPackResolution(
            packVersion,
            $"v{packVersion}",
            false,
            new ThemeRuntimeAssets(
                Path.Combine(directory, manifest.Runtime),
                _sharedTemplateStylePath,
                Path.Combine(directory, manifest.Profile)));
    }

    private void ValidateManifest(CompatibilityPackManifest manifest)
    {
        if (manifest.SchemaVersion != ManifestSchemaVersion)
        {
            throw new InvalidDataException("兼容补丁包清单版本不受支持。");
        }

        _ = ParseVersion(manifest.PackVersion, "packVersion");
        var minimumAppVersion = ParseVersion(manifest.MinimumAppVersion, "minimumAppVersion");
        if (minimumAppVersion > _currentAppVersion)
        {
            throw new InvalidDataException("兼容补丁包需要更新版本的 Tessalume。");
        }
        if (manifest.RuntimeContractVersion != _requiredRuntimeContractVersion)
        {
            throw new InvalidDataException("兼容补丁包与当前主题运行时契约不匹配。");
        }
        if (!string.Equals(manifest.Runtime, RuntimeFileName, StringComparison.Ordinal) ||
            !string.Equals(manifest.Profile, ProfileFileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("兼容补丁包声明了不受支持的运行时文件。");
        }
        if (manifest.Files.Count != 2 ||
            !manifest.Files.ContainsKey(RuntimeFileName) ||
            !manifest.Files.ContainsKey(ProfileFileName))
        {
            throw new InvalidDataException("兼容补丁包文件清单不完整。");
        }
        foreach (var (fileName, sha256) in manifest.Files)
        {
            if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
                fileName.Contains(Path.AltDirectorySeparatorChar) ||
                fileName.Contains(Path.DirectorySeparatorChar))
            {
                throw new InvalidDataException("兼容补丁包文件名无效。");
            }
            ValidateSha256(sha256, fileName);
        }
    }

    private Version ValidateProfile(string profilePath, Version? expectedVersion)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(profilePath));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var schemaVersion) ||
            schemaVersion.GetInt32() != 1 ||
            !root.TryGetProperty("profileVersion", out var profileVersionElement) ||
            profileVersionElement.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("runtimeContractVersion", out var contractVersion) ||
            contractVersion.GetInt32() != _requiredRuntimeContractVersion ||
            !root.TryGetProperty("selectors", out var selectors) ||
            selectors.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("兼容配置与当前主题运行时契约不匹配。");
        }

        var profileVersion = ParseVersion(profileVersionElement.GetString(), "profileVersion");
        if (expectedVersion is not null && profileVersion != expectedVersion)
        {
            throw new InvalidDataException("兼容配置版本与补丁包版本不一致。");
        }
        foreach (var group in RequiredSelectorGroups)
        {
            if (!selectors.TryGetProperty(group, out var values) ||
                values.ValueKind != JsonValueKind.Array ||
                values.GetArrayLength() == 0 ||
                values.EnumerateArray().Any(value =>
                    value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())))
            {
                throw new InvalidDataException($"兼容配置缺少有效选择器组：{group}。");
            }
        }
        return profileVersion;
    }

    private CompatibilityPackState? LoadState()
    {
        if (!File.Exists(_statePath)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<CompatibilityPackState>(
                File.ReadAllBytes(_statePath),
                JsonOptions);
            return state?.SchemaVersion == 1 ? state : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    private void SaveState(CompatibilityPackState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var temporaryPath = _statePath + ".tmp";
        File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(
            state with { SchemaVersion = 1 },
            JsonOptions));
        File.Move(temporaryPath, _statePath, overwrite: true);
    }

    private string GetPackDirectory(string version)
    {
        var parsed = ParseVersion(version, "packVersion");
        var path = Path.GetFullPath(Path.Combine(_packsDirectory, parsed.ToString()));
        EnsureContained(_packsDirectory, path);
        return path;
    }

    private static string NormalizeArchiveEntry(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Contains('/') ||
            normalized is "." or "..")
        {
            throw new InvalidDataException("兼容补丁包包含无效路径。");
        }
        return normalized;
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length <= 0 || entry.Length > maximumBytes)
        {
            throw new InvalidDataException("兼容补丁包清单大小无效。");
        }
        await using var stream = entry.Open();
        using var memory = new MemoryStream((int)entry.Length);
        await stream.CopyToAsync(memory, cancellationToken);
        if (memory.Length > maximumBytes)
        {
            throw new InvalidDataException("兼容补丁包清单超出安全限制。");
        }
        return memory.ToArray();
    }

    private static void EnsureRequiredAsset(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length <= 0)
        {
            throw new InvalidDataException($"兼容运行时文件不存在或为空：{Path.GetFileName(path)}");
        }
    }

    private static Version ParseVersion(string? value, string fieldName)
    {
        if (!TryParseVersion(value, out var version))
        {
            throw new InvalidDataException($"兼容补丁包的 {fieldName} 版本号无效。");
        }
        return version;
    }

    private static bool TryParseVersion(string? value, out Version version) =>
        Version.TryParse(value?.Trim(), out version!);

    private static void ValidateSha256(string? value, string label)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"{label} 的 SHA-256 校验值无效。");
        }
    }

    private static string CalculateFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static async Task<string> CalculateFileHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("兼容补丁路径超出本地兼容目录。");
        }
    }

    private sealed record CompatibilityPackState
    {
        public int SchemaVersion { get; init; } = 1;
        public string? ActivePackVersion { get; init; }
        public string? PreviousPackVersion { get; init; }
    }

    private sealed record CompatibilityPackManifest
    {
        public int SchemaVersion { get; init; }
        public string PackVersion { get; init; } = string.Empty;
        public string MinimumAppVersion { get; init; } = string.Empty;
        public int RuntimeContractVersion { get; init; }
        public string Runtime { get; init; } = string.Empty;
        public string Profile { get; init; } = string.Empty;
        public Dictionary<string, string> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
