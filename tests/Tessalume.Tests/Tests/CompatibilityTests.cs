internal static partial class TestSuite
{
    static async Task CompatibilityHealthStateIsDurableAsync()
    {
        var data = Path.Combine(Path.GetTempPath(), $"tessalume-compatibility-{Guid.NewGuid():N}");
        Directory.CreateDirectory(data);
        try
        {
            var store = new StudioStateStore(data);
            var failureAt = DateTimeOffset.UtcNow;
            await store.SaveAsync(new StudioState
            {
                Port = 9340,
                ThemeId = "sample.theme",
                Enabled = true,
                LastSuccessfulApplyAt = failureAt.AddMinutes(-2),
                CodexVersionAtLastApply = "1.2.3.4",
                RuntimeContractVersion = ThemeRuntime.ContractVersion,
                LastFailureStage = ThemeRuntimeFailureStage.ThemeScriptFailed,
                LastFailureMessage = "fixture script failure",
                LastFailureAt = failureAt,
            });
            var restored = await store.LoadAsync()
                ?? throw new InvalidOperationException("Compatibility state did not reload.");
            var json = await File.ReadAllTextAsync(Path.Combine(data, "state.json"));
            Ensure(restored.SchemaVersion == StudioState.CurrentSchemaVersion &&
                   restored.RuntimeContractVersion == ThemeRuntime.ContractVersion &&
                   restored.LastFailureStage == ThemeRuntimeFailureStage.ThemeScriptFailed &&
                   restored.CodexVersionAtLastApply == "1.2.3.4" &&
                   json.Contains("\"ThemeScriptFailed\"", StringComparison.Ordinal),
                "Compatibility baselines and failure stages must survive a restart in readable state JSON.");

            var repositoryRoot = FindRepositoryRoot();
            var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
            var mainSource = await ReadMainWindowSourceAsync(appRoot);
            var diagnosticsSource = await File.ReadAllTextAsync(Path.Combine(
                appRoot,
                "Features",
                "Diagnostics",
                "CompatibilityHealthService.cs"));
            var xaml = await File.ReadAllTextAsync(Path.Combine(
                appRoot,
                "Features",
                "Diagnostics",
                "DiagnosticsView.xaml"));
            Ensure(mainSource.Contains("_runtime.PreflightAsync", StringComparison.Ordinal) &&
                   mainSource.Contains("RecordCompatibilityFailureAsync", StringComparison.Ordinal) &&
                   diagnosticsSource.Contains("CodexVersionChanged", StringComparison.Ordinal),
                "Theme application must preflight version changes and preserve actionable failure state.");
            Ensure(xaml.Contains("DiagnosticCodexVersionText", StringComparison.Ordinal) &&
                   xaml.Contains("DiagnosticLastFailureText", StringComparison.Ordinal) &&
                   xaml.Contains("兼容性健康", StringComparison.Ordinal),
                "The diagnostics page must expose the compatibility baseline and latest failure stage.");
        }
        finally
        {
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }

    static async Task CompatibilityPacksInstallValidateAndRollBackAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-compatibility-pack-{Guid.NewGuid():N}");
        var builtIn = Path.Combine(root, "Compatibility");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(builtIn);
        Directory.CreateDirectory(data);
        try
        {
            var source = Path.Combine(repositoryRoot, "src", "Tessalume.App", "Compatibility");
            var sourceAssets = GetSourceRuntimeAssets(repositoryRoot);
            File.Copy(sourceAssets.RuntimePath, Path.Combine(builtIn, CompatibilityPackStore.RuntimeFileName));
            File.Copy(sourceAssets.CompatibilityProfilePath, Path.Combine(builtIn, CompatibilityPackStore.ProfileFileName));
            File.Copy(sourceAssets.SharedTemplateStylePath, Path.Combine(builtIn, ThemePayloadBuilder.SharedTemplateStyleFileName));

            var staleArchive = await CreateCompatibilityArchiveAsync(root, sourceAssets, new Version(3, 0, 1));
            var stalePackDirectory = Path.Combine(data, "compatibility", "packs", "3.0.1");
            Directory.CreateDirectory(stalePackDirectory);
            ZipFile.ExtractToDirectory(staleArchive, stalePackDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(data, "compatibility", "state.json"),
                """
                {
                  "schemaVersion": 1,
                  "activePackVersion": "3.0.1",
                  "previousPackVersion": null
                }
                """);

            var store = new CompatibilityPackStore(
                builtIn,
                data,
                new Version(1, 4, 1),
                ThemeRuntime.ContractVersion);
            var baseline = store.Resolve();
            Ensure(baseline.IsBuiltIn && baseline.PackVersion == new Version(3, 0, 2),
                "A verified embedded compatibility profile must always remain available as the baseline.");

            var staleHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(staleArchive)));
            var staleRejected = false;
            try
            {
                await store.InstallAsync(staleArchive, staleHash);
            }
            catch (InvalidDataException)
            {
                staleRejected = true;
            }
            Ensure(staleRejected && store.Resolve().IsBuiltIn,
                "A stale pack must neither override nor be installed over a newer embedded compatibility baseline.");

            var firstArchive = await CreateCompatibilityArchiveAsync(root, sourceAssets, new Version(3, 0, 3));
            var firstHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(firstArchive)));
            var firstInstall = await store.InstallAsync(firstArchive, firstHash);
            Ensure(firstInstall.Changed && !firstInstall.ActivePack.IsBuiltIn &&
                   firstInstall.ActivePack.PackVersion == new Version(3, 0, 3),
                "A fully verified official compatibility pack must become active without replacing the executable.");

            await File.WriteAllTextAsync(
                Path.Combine(data, "compatibility", "state.json"),
                """
                {
                  "schemaVersion": 1,
                  "activePackVersion": "3.0.3",
                  "previousPackVersion": "3.0.1"
                }
                """);
            var staleRollback = store.Rollback();
            Ensure(staleRollback.IsBuiltIn && staleRollback.PackVersion == baseline.PackVersion,
                "Rollback must prefer the embedded baseline over an older previous pack.");

            firstInstall = await store.InstallAsync(firstArchive, firstHash);
            Ensure(firstInstall.Changed && firstInstall.ActivePack.PackVersion == new Version(3, 0, 3),
                "A newer verified pack must remain installable after falling back to the embedded baseline.");

            var secondArchive = await CreateCompatibilityArchiveAsync(root, sourceAssets, new Version(3, 0, 4));
            var secondHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(secondArchive)));
            var secondInstall = await store.InstallAsync(secondArchive, secondHash);
            Ensure(secondInstall.ActivePack.PackVersion == new Version(3, 0, 4) &&
                   secondInstall.PreviousPack.PackVersion == new Version(3, 0, 3),
                "Installing a newer compatibility pack must preserve the last known-good pack for rollback.");

            var rolledBack = store.Rollback();
            Ensure(!rolledBack.IsBuiltIn && rolledBack.PackVersion == new Version(3, 0, 3),
                "A failed active compatibility pack must roll back atomically to the previous verified pack.");

            await File.AppendAllTextAsync(rolledBack.RuntimeAssets.RuntimePath, "\n// corrupted by fixture");
            var repaired = store.Resolve();
            Ensure(repaired.IsBuiltIn && repaired.PackVersion == baseline.PackVersion,
                "A damaged installed pack must never be loaded and must fall back to the embedded baseline.");

            var rejected = false;
            try
            {
                await store.InstallAsync(secondArchive, new string('0', 64));
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Ensure(rejected && store.Resolve().IsBuiltIn,
                "An archive with a mismatched outer SHA-256 must be rejected without changing the active baseline.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateCompatibilityArchiveAsync(
        string root,
        ThemeRuntimeAssets sourceAssets,
        Version version)
    {
        var fixture = Path.Combine(root, $"pack-source-{version}");
        Directory.CreateDirectory(fixture);
        var runtimePath = Path.Combine(fixture, CompatibilityPackStore.RuntimeFileName);
        var profilePath = Path.Combine(fixture, CompatibilityPackStore.ProfileFileName);
        var runtime = await File.ReadAllTextAsync(sourceAssets.RuntimePath);
        await File.WriteAllTextAsync(runtimePath, $"{runtime}\n// compatibility fixture {version}");
        var profile = await File.ReadAllTextAsync(sourceAssets.CompatibilityProfilePath);
        using (var profileDocument = JsonDocument.Parse(profile))
        {
            var currentProfileVersion = profileDocument.RootElement
                .GetProperty("profileVersion")
                .GetString() ?? throw new InvalidDataException("Compatibility fixture profile version is missing.");
            profile = profile.Replace(
                $"\"profileVersion\": \"{currentProfileVersion}\"",
                $"\"profileVersion\": \"{version}\"",
                StringComparison.Ordinal);
        }
        await File.WriteAllTextAsync(profilePath, profile);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CompatibilityPackStore.RuntimeFileName] = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(runtimePath))),
            [CompatibilityPackStore.ProfileFileName] = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(profilePath))),
        };
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = CompatibilityPackStore.ManifestSchemaVersion,
            packVersion = version.ToString(),
            minimumAppVersion = "1.4.1",
            runtimeContractVersion = ThemeRuntime.ContractVersion,
            runtime = CompatibilityPackStore.RuntimeFileName,
            profile = CompatibilityPackStore.ProfileFileName,
            files,
        });
        await File.WriteAllTextAsync(
            Path.Combine(fixture, CompatibilityPackStore.ManifestFileName),
            manifest);

        var archivePath = Path.Combine(root, $"compatibility-{version}.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var fileName in new[]
                 {
                     CompatibilityPackStore.ManifestFileName,
                     CompatibilityPackStore.RuntimeFileName,
                     CompatibilityPackStore.ProfileFileName,
                 })
        {
            archive.CreateEntryFromFile(Path.Combine(fixture, fileName), fileName, CompressionLevel.Fastest);
        }
        return archivePath;
    }
}
