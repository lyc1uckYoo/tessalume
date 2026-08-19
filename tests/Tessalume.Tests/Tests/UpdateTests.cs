internal static partial class TestSuite
{
    static async Task ReleaseUpdaterChecksAndDownloadsAsync()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"tessalume-update-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var executableBytes = Encoding.UTF8.GetBytes("Tessalume test executable v1.3.0");
            var sha256 = Convert.ToHexString(SHA256.HashData(executableBytes));
            var requestedUris = new List<Uri>();
            using var httpClient = new HttpClient(new StubHttpHandler(request =>
            {
                requestedUris.Add(request.RequestUri!);
                if (request.RequestUri!.Host == "api.github.com")
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        tag_name = "v1.3.0",
                        html_url = "https://github.com/lyc1uckYoo/tessalume/releases/tag/v1.3.0",
                        body = "Update test release",
                        draft = false,
                        prerelease = false,
                        assets = new[]
                        {
                            new
                            {
                                name = "Tessalume.exe",
                                browser_download_url = "https://downloads.example.test/Tessalume.exe",
                                size = executableBytes.Length,
                                digest = $"sha256:{sha256.ToLowerInvariant()}",
                            },
                        },
                    });
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    };
                }

                if (request.RequestUri.Host == "downloads.example.test")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(executableBytes),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }));
            using var client = new ReleaseUpdateClient(
                httpClient,
                "lyc1uckYoo",
                "tessalume",
                dataDirectory,
                new Version(1, 2, 0));
            var release = await client.CheckLatestAsync();
            Ensure(release is not null && release.Version == new Version(1, 3, 0) && release.Sha256 == sha256,
                "The updater must accept a newer stable GitHub Release and its asset digest.");
            UpdateDownloadProgress? lastProgress = null;
            var downloaded = await client.DownloadAsync(
                release!,
                new Progress<UpdateDownloadProgress>(value => lastProgress = value));
            Ensure(File.ReadAllBytes(downloaded).SequenceEqual(executableBytes),
                "The updater must persist the verified release asset without modifying its bytes.");
            Ensure(requestedUris.Any(uri => uri.Host == "api.github.com") &&
                   requestedUris.Any(uri => uri.Host == "downloads.example.test"),
                "The updater must use the release metadata endpoint and the declared asset URL.");
            Ensure(lastProgress is null || lastProgress.BytesReceived == executableBytes.Length,
                "Download progress must never report an invalid final byte count.");
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    static async Task ReleaseUpdaterUsesVerifiedDeltaAndFallsBackAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-delta-client-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(root, "data");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var basisPath = Path.Combine(root, "Tessalume.exe");
            var targetPath = Path.Combine(root, "Tessalume-next.exe");
            var deltaName = "Tessalume-1.2.0-to-1.3.0.delta";
            var deltaPath = Path.Combine(root, deltaName);
            var basisBytes = new byte[2 * 1024 * 1024];
            new Random(173).NextBytes(basisBytes);
            var targetBytes = basisBytes.ToArray();
            Encoding.UTF8.GetBytes("verified incremental update payload")
                .CopyTo(targetBytes, 640 * 1024);
            await File.WriteAllBytesAsync(basisPath, basisBytes);
            await File.WriteAllBytesAsync(targetPath, targetBytes);
            await BinaryDeltaCodec.CreateAsync(basisPath, targetPath, deltaPath);
            var deltaBytes = await File.ReadAllBytesAsync(deltaPath);
            Ensure(deltaBytes.Length < targetBytes.Length / 4,
                "A localized executable change must produce a materially smaller delta asset.");

            var basisSha256 = Convert.ToHexString(SHA256.HashData(basisBytes));
            var targetSha256 = Convert.ToHexString(SHA256.HashData(targetBytes));
            var deltaSha256 = Convert.ToHexString(SHA256.HashData(deltaBytes));
            var manifest = new UpdateDeltaManifest
            {
                SchemaVersion = UpdateDeltaManifest.CurrentSchemaVersion,
                TargetVersion = "1.3.0",
                TargetFileName = UpdateDeltaManifest.TargetExecutableName,
                TargetSize = targetBytes.Length,
                TargetSha256 = targetSha256,
                Deltas =
                [
                    new UpdateDeltaEntry
                    {
                        FromVersion = "1.2.0",
                        FromSha256 = basisSha256,
                        Algorithm = UpdateDeltaEntry.SupportedAlgorithm,
                        AssetName = deltaName,
                        AssetSize = deltaBytes.Length,
                        AssetSha256 = deltaSha256,
                    },
                ],
            };
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                UpdateDeltaManifest.JsonOptions);
            var manifestSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes));
            var requestedUris = new List<Uri>();
            using var httpClient = new HttpClient(new StubHttpHandler(request =>
            {
                requestedUris.Add(request.RequestUri!);
                if (request.RequestUri!.Host == "api.github.com")
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        tag_name = "v1.3.0",
                        html_url = "https://github.com/lyc1uckYoo/tessalume/releases/tag/v1.3.0",
                        body = "Delta update test release",
                        draft = false,
                        prerelease = false,
                        assets = new[]
                        {
                            new
                            {
                                name = UpdateDeltaManifest.TargetExecutableName,
                                browser_download_url = "https://downloads.example.test/Tessalume.exe",
                                size = targetBytes.Length,
                                digest = $"sha256:{targetSha256.ToLowerInvariant()}",
                            },
                            new
                            {
                                name = UpdateDeltaManifest.FileName,
                                browser_download_url = "https://downloads.example.test/Tessalume.update.json",
                                size = manifestBytes.Length,
                                digest = $"sha256:{manifestSha256.ToLowerInvariant()}",
                            },
                            new
                            {
                                name = deltaName,
                                browser_download_url = $"https://downloads.example.test/{deltaName}",
                                size = deltaBytes.Length,
                                digest = $"sha256:{deltaSha256.ToLowerInvariant()}",
                            },
                        },
                    });
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    };
                }

                var bytes = request.RequestUri.AbsolutePath switch
                {
                    "/Tessalume.exe" => targetBytes,
                    "/Tessalume.update.json" => manifestBytes,
                    _ when request.RequestUri.AbsolutePath.EndsWith(deltaName, StringComparison.Ordinal) => deltaBytes,
                    _ => null,
                };
                return bytes is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(bytes),
                    };
            }));
            using var client = new ReleaseUpdateClient(
                httpClient,
                "lyc1uckYoo",
                "tessalume",
                dataDirectory,
                new Version(1, 2, 0),
                basisPath);
            var release = await client.CheckLatestAsync();
            Ensure(release is { UsesDelta: true } &&
                   release.PreferredDownloadSize == deltaBytes.Length &&
                   release.Delta?.FromSha256 == basisSha256,
                "A matching signed manifest must select the smaller delta for the installed version.");
            var reconstructed = await client.DownloadAsync(release!);
            Ensure(File.ReadAllBytes(reconstructed).SequenceEqual(targetBytes) &&
                   requestedUris.Any(uri => uri.AbsolutePath.EndsWith(deltaName, StringComparison.Ordinal)) &&
                   !requestedUris.Any(uri => uri.AbsolutePath == "/Tessalume.exe"),
                "The updater must reconstruct the exact target from the verified delta without downloading the full EXE.");

            requestedUris.Clear();
            basisBytes[0] ^= 0xff;
            await File.WriteAllBytesAsync(basisPath, basisBytes);
            var fallback = await client.DownloadAsync(release!);
            Ensure(File.ReadAllBytes(fallback).SequenceEqual(targetBytes) &&
                   requestedUris.Any(uri => uri.AbsolutePath == "/Tessalume.exe") &&
                   !requestedUris.Any(uri => uri.AbsolutePath.EndsWith(deltaName, StringComparison.Ordinal)),
                "A mismatched local EXE must skip the delta and safely fall back to the verified full asset.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task DeltaUpdatePackIsDeterministicAndExactAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-delta-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var basisPath = Path.Combine(root, "basis.exe");
            var targetPath = Path.Combine(root, "target.exe");
            var firstOutput = Path.Combine(root, "first");
            var secondOutput = Path.Combine(root, "second");
            var basis = new byte[1024 * 1024];
            new Random(811).NextBytes(basis);
            var target = basis.ToArray();
            Encoding.UTF8.GetBytes("deterministic release delta")
                .CopyTo(target, 384 * 1024);
            await File.WriteAllBytesAsync(basisPath, basis);
            await File.WriteAllBytesAsync(targetPath, target);
            var repositoryRoot = FindRepositoryRoot();
            var project = Path.Combine(
                repositoryRoot,
                "tools",
                "Tessalume.UpdatePack",
                "Tessalume.UpdatePack.csproj");

            foreach (var output in new[] { firstOutput, secondOutput })
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = repositoryRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var argument in new[]
                         {
                             "run",
                             "--project",
                             project,
                             "--configuration",
                             "Release",
                             "--no-build",
                             "--",
                             basisPath,
                             "2.1.1",
                             targetPath,
                             "2.1.2",
                             output,
                         })
                {
                    startInfo.ArgumentList.Add(argument);
                }
                using var process = System.Diagnostics.Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Could not start the incremental update pack builder.");
                var standardOutput = await process.StandardOutput.ReadToEndAsync();
                var standardError = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                Ensure(process.ExitCode == 0 && standardOutput.Contains("\"published\":true", StringComparison.Ordinal),
                    $"The incremental pack builder failed: {standardError}");
            }

            var deltaName = "Tessalume-2.1.1-to-2.1.2.delta";
            foreach (var name in new[] { deltaName, UpdateDeltaManifest.FileName, "SHA256SUMS.txt" })
            {
                var first = await File.ReadAllBytesAsync(Path.Combine(firstOutput, name));
                var second = await File.ReadAllBytesAsync(Path.Combine(secondOutput, name));
                Ensure(first.SequenceEqual(second),
                    $"Incremental release asset generation must be deterministic: {name}.");
            }

            var manifest = JsonSerializer.Deserialize<UpdateDeltaManifest>(
                await File.ReadAllBytesAsync(Path.Combine(firstOutput, UpdateDeltaManifest.FileName)),
                UpdateDeltaManifest.JsonOptions);
            Ensure(manifest is { SchemaVersion: UpdateDeltaManifest.CurrentSchemaVersion } &&
                   manifest.TargetVersion == "2.1.2" &&
                   manifest.Deltas.Count == 1 &&
                   manifest.Deltas[0].FromVersion == "2.1.1",
                "The generated manifest must bind one exact basis version to one target executable.");
            var reconstructed = Path.Combine(root, "reconstructed.exe");
            await BinaryDeltaCodec.ApplyAsync(
                basisPath,
                Path.Combine(firstOutput, deltaName),
                reconstructed);
            Ensure(File.ReadAllBytes(reconstructed).SequenceEqual(target),
                "The published delta asset must reconstruct the target executable byte-for-byte.");
            var checksums = await File.ReadAllTextAsync(Path.Combine(firstOutput, "SHA256SUMS.txt"));
            Ensure(checksums.Contains($"*{UpdateDeltaManifest.TargetExecutableName}", StringComparison.Ordinal) &&
                   checksums.Contains($"*{deltaName}", StringComparison.Ordinal) &&
                   checksums.Contains($"*{UpdateDeltaManifest.FileName}", StringComparison.Ordinal),
                "The release checksum file must cover the full EXE, delta, and delta manifest.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task PortableUpdaterReplacesAndBacksUpAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-update-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "Tessalume.exe");
            var source = Path.Combine(root, "Tessalume.exe.download");
            var helper = Path.Combine(root, "Tessalume.UpdateHelper.exe");
            var resultPath = Path.Combine(root, "update-result.json");
            var preferencesPath = Path.Combine(root, "data", "ui-settings.json");
            var oldBytes = Encoding.UTF8.GetBytes("old version");
            var newBytes = Encoding.UTF8.GetBytes("new version");
            var preferencesBytes = Encoding.UTF8.GetBytes("{\"DarkMode\":true,\"FavoriteThemeIds\":[\"kept-theme\"]}");
            await File.WriteAllBytesAsync(destination, oldBytes);
            await File.WriteAllBytesAsync(source, newBytes);
            await File.WriteAllTextAsync(helper, "helper");
            Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
            await File.WriteAllBytesAsync(preferencesPath, preferencesBytes);
            var request = new PortableUpdateRequest(
                0,
                source,
                destination,
                Convert.ToHexString(SHA256.HashData(newBytes)),
                "v1.3.0",
                resultPath,
                helper);
            var result = await PortableUpdateInstaller.ApplyAndWriteResultAsync(request);
            Ensure(result.Success && File.ReadAllBytes(destination).SequenceEqual(newBytes),
                "The portable installer must replace the destination with the verified release.");
            Ensure(result.BackupPath is not null && File.ReadAllBytes(result.BackupPath).SequenceEqual(oldBytes),
                "The portable installer must keep the previous executable as a rollback backup.");
            Ensure(File.ReadAllBytes(preferencesPath).SequenceEqual(preferencesBytes),
                "Replacing the executable must not modify portable user settings or other data files.");
            var persisted = PortableUpdateInstaller.ReadResult(resultPath);
            Ensure(persisted is { Success: true, VersionLabel: "v1.3.0" },
                "The update result must survive the updater process restart boundary.");

            var rejectedSource = Path.Combine(root, "tampered.exe.download");
            await File.WriteAllTextAsync(rejectedSource, "tampered");
            var rejected = await PortableUpdateInstaller.ApplyAndWriteResultAsync(request with
            {
                SourcePath = rejectedSource,
                ExpectedSha256 = new string('0', 64),
                ResultPath = Path.Combine(root, "rejected-result.json"),
            });
            Ensure(!rejected.Success && File.ReadAllBytes(destination).SequenceEqual(newBytes),
                "A checksum mismatch must leave the currently installed executable untouched.");
            Ensure(File.ReadAllBytes(preferencesPath).SequenceEqual(preferencesBytes),
                "A rejected update must also leave portable user settings untouched.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task PortableUpdaterRollsBackWithoutTouchingUserDataAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-update-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "Tessalume.exe");
            var previous = destination + ".previous";
            var helper = Path.Combine(root, "Tessalume.UpdateHelper.exe");
            var resultPath = Path.Combine(root, "data", "update-result.json");
            var preferencesPath = Path.Combine(root, "data", "ui-settings.json");
            var currentBytes = Encoding.UTF8.GetBytes("current v2.0 executable");
            var previousBytes = Encoding.UTF8.GetBytes("previous v1.4 executable");
            var preferencesBytes = Encoding.UTF8.GetBytes("{\"DarkMode\":true,\"FavoriteThemeIds\":[\"kept-theme\"]}");
            await File.WriteAllBytesAsync(destination, currentBytes);
            await File.WriteAllBytesAsync(previous, previousBytes);
            await File.WriteAllTextAsync(helper, "helper");
            Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
            await File.WriteAllBytesAsync(preferencesPath, preferencesBytes);

            var request = new PortableUpdateRequest(
                0,
                previous,
                destination,
                Convert.ToHexString(SHA256.HashData(previousBytes)),
                "v1.4.1",
                resultPath,
                helper)
            {
                Operation = PortableUpdateOperation.Rollback,
                PreviousVersionLabel = "v2.0.0",
                StartupHealthToken = Guid.NewGuid().ToString("N"),
            };
            var result = await PortableUpdateInstaller.ApplyAndWriteResultAsync(request);
            Ensure(result.Success && result.Operation == PortableUpdateOperation.Rollback &&
                   File.ReadAllBytes(destination).SequenceEqual(previousBytes),
                "A requested version rollback must replace only the executable with the verified previous version.");
            Ensure(result.BackupPath is not null &&
                   File.ReadAllBytes(result.BackupPath).SequenceEqual(currentBytes),
                "The rollback helper must temporarily retain the current executable until the previous version starts.");
            Ensure(File.ReadAllBytes(preferencesPath).SequenceEqual(preferencesBytes),
                "Rolling back the executable must not alter portable preferences or user data.");

            await PortableUpdateInstaller.RestoreBackupAsync(destination, result.BackupPath!);
            Ensure(File.ReadAllBytes(destination).SequenceEqual(currentBytes),
                "A failed previous-version restart must be able to restore the current executable transactionally.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task UpdateRollbackStateRequiresAnUntamperedBackupAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-rollback-state-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        try
        {
            var backup = Path.Combine(root, "Tessalume.exe.previous");
            await File.WriteAllTextAsync(backup, "verified previous executable");
            await File.WriteAllTextAsync(Path.Combine(data, "ui-settings.json"), "{\"SchemaVersion\":3}");
            var dataSnapshots = new UpdateDataSnapshotStore(data);
            var snapshot = await dataSnapshots.CreateAsync(
                Guid.NewGuid().ToString("N"),
                "v1.4.1");
            var store = new UpdateRollbackStore(root, data, "Tessalume.exe");
            var saved = await store.SaveAsync(
                "v2.0.0",
                "v1.4.1",
                backup,
                snapshot.SnapshotId);
            var loaded = await store.LoadAsync();
            Ensure(loaded == saved && loaded?.PreviousVersionLabel == "v1.4.1",
                "A successful startup must persist a readable rollback point for the immediately previous version.");

            await File.AppendAllTextAsync(backup, "tampered");
            Ensure(await store.LoadAsync() is null,
                "A modified previous executable must never remain available through the rollback UI.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task LegacyUpdateResultCreatesAPreMigrationRollbackPointAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-legacy-update-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "data");
        var themes = Path.Combine(root, "themes");
        var downloads = Path.Combine(data, "updates", "downloads");
        var helpers = Path.Combine(data, "updates", "helpers");
        Directory.CreateDirectory(themes);
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(helpers);
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The test executable path is unavailable.");
            var destination = Path.Combine(root, "Tessalume.exe");
            var backup = destination + ".previous";
            var source = Path.Combine(downloads, "Tessalume-2.0.0.exe.download");
            var helper = Path.Combine(helpers, "Tessalume.UpdateHelper.legacy.exe");
            File.Copy(executable, destination);
            File.Copy(executable, backup);
            await File.WriteAllTextAsync(source, "already moved by the legacy updater");
            await File.WriteAllTextAsync(helper, "legacy helper path");
            var oldPreferences = "{\"SchemaVersion\":3,\"FavoriteThemeIds\":[\"kept-theme\"]}";
            var oldState = "{\"SchemaVersion\":2,\"ThemeId\":\"kept-theme\",\"Enabled\":true}";
            await File.WriteAllTextAsync(Path.Combine(data, "ui-settings.json"), oldPreferences);
            await File.WriteAllTextAsync(Path.Combine(data, "state.json"), oldState);
            var resultPath = Path.Combine(data, "update-result.json");
            var legacyResult = new PortableUpdateResult(
                true,
                "v2.0.0",
                "legacy update completed",
                source,
                destination,
                backup,
                helper,
                0,
                DateTimeOffset.Now);
            await PortableUpdateInstaller.WriteResultAsync(resultPath, legacyResult);

            var layout = new PortableLayout(root, themes, data);
            var prepared = await LegacyUpdateRecoveryAdapter.PrepareAsync(layout, legacyResult);
            Ensure(prepared is
            {
                Success: true,
                Operation: PortableUpdateOperation.Install,
                DataSnapshotId.Length: 32,
            } && !string.IsNullOrWhiteSpace(prepared.PreviousVersionLabel),
                "A successful 1.x update result must gain a pre-migration data snapshot before the new app loads settings.");
            var preparedResult = prepared
                ?? throw new InvalidOperationException("The legacy update result was not prepared.");
            var persisted = PortableUpdateInstaller.ReadResult(resultPath);
            Ensure(persisted?.DataSnapshotId == preparedResult.DataSnapshotId &&
                   persisted.PreviousVersionLabel == preparedResult.PreviousVersionLabel,
                "The adapted legacy result must survive a crash or restart before the success dialog is acknowledged.");

            var snapshots = new UpdateDataSnapshotStore(data);
            Ensure(await snapshots.ValidateAsync(preparedResult.DataSnapshotId) is not null,
                "The legacy recovery snapshot must pass the same integrity validation as a native 2.x snapshot.");
            await File.WriteAllTextAsync(Path.Combine(data, "ui-settings.json"), "{\"SchemaVersion\":4}");
            await File.WriteAllTextAsync(Path.Combine(data, "state.json"), "{\"SchemaVersion\":3}");
            await snapshots.RestoreAsync(preparedResult.DataSnapshotId);
            Ensure(await File.ReadAllTextAsync(Path.Combine(data, "ui-settings.json")) == oldPreferences &&
                   await File.ReadAllTextAsync(Path.Combine(data, "state.json")) == oldState,
                "A direct 1.x to 2.x rollback must restore both legacy configuration schemas exactly.");

            var rollbackStore = new UpdateRollbackStore(root, data, "Tessalume.exe");
            await rollbackStore.SaveAsync(
                preparedResult.VersionLabel,
                preparedResult.PreviousVersionLabel,
                backup,
                preparedResult.DataSnapshotId);
            Ensure(await rollbackStore.LoadAsync() is not null,
                "The recovered legacy result must establish a usable manual rollback point.");

            var unsafeResult = legacyResult with
            {
                BackupPath = Path.Combine(root, "outside.exe.previous"),
            };
            var rejected = await LegacyUpdateRecoveryAdapter.PrepareAsync(layout, unsafeResult);
            Ensure(string.IsNullOrWhiteSpace(rejected?.DataSnapshotId),
                "Legacy result adaptation must reject backup paths outside the exact portable rollback location.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task UpdateDataSnapshotsRestoreVersionedSettingsAtomicallyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-update-data-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        try
        {
            var preferencesPath = Path.Combine(data, "ui-settings.json");
            var statePath = Path.Combine(data, "state.json");
            var oldPreferences = "{\"SchemaVersion\":3,\"FavoriteThemeIds\":[\"kept\"]}";
            var oldState = "{\"SchemaVersion\":2,\"ThemeId\":\"kept-theme\"}";
            await File.WriteAllTextAsync(preferencesPath, oldPreferences);
            await File.WriteAllTextAsync(statePath, oldState);
            var store = new UpdateDataSnapshotStore(data);
            var snapshot = await store.CreateAsync(Guid.NewGuid().ToString("N"), "v1.4.1");

            await File.WriteAllTextAsync(preferencesPath, "{\"SchemaVersion\":4,\"FavoriteThemeIds\":[]}");
            await File.WriteAllTextAsync(statePath, "{\"SchemaVersion\":3,\"ThemeId\":\"changed\"}");
            await store.RestoreAsync(snapshot.SnapshotId, snapshot.ManifestSha256);
            Ensure(await File.ReadAllTextAsync(preferencesPath) == oldPreferences &&
                   await File.ReadAllTextAsync(statePath) == oldState,
                "Rolling back an executable must restore the configuration schema that the previous version can read.");

            var currentSnapshot = await store.CreateAsync(Guid.NewGuid().ToString("N"), "v2.0.0");
            var recoveryPath = await store.PreserveRecoveryCopyAsync(currentSnapshot.SnapshotId, "v2.0.0");
            Ensure(File.Exists(Path.Combine(recoveryPath, "snapshot.json")) &&
                   File.Exists(Path.Combine(recoveryPath, "ui-settings.json")),
                "A manual downgrade must retain a recovery copy of the newer configuration without deleting it.");

            await File.WriteAllTextAsync(preferencesPath, "preserve-current");
            await File.AppendAllTextAsync(
                Path.Combine(snapshot.DirectoryPath, "ui-settings.json"),
                "tampered");
            var rejected = false;
            try
            {
                await store.RestoreAsync(snapshot.SnapshotId, snapshot.ManifestSha256);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Ensure(rejected && await File.ReadAllTextAsync(preferencesPath) == "preserve-current",
                "A tampered configuration snapshot must be rejected before modifying live user settings.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task UpdateHelperPreservesSchemasAcrossRollbackAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-update-helper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var fixtureBytes = await PrepareUpdateFixtureAsync(root);
            var data = Path.Combine(root, "data");
            var downloads = Path.Combine(data, "updates", "downloads");
            var helpers = Path.Combine(data, "updates", "helpers");
            Directory.CreateDirectory(downloads);
            Directory.CreateDirectory(helpers);
            var destination = Path.Combine(root, "Tessalume.exe");
            var source = Path.Combine(downloads, "Tessalume-v2.exe.download");
            var helper = Path.Combine(helpers, "Tessalume.UpdateHelper.fixture.exe");
            var resultPath = Path.Combine(data, "update-result.json");
            var preferencesPath = Path.Combine(data, "ui-settings.json");
            var oldExecutable = fixtureBytes.Concat("-old-v1.4.1"u8.ToArray()).ToArray();
            var newExecutable = fixtureBytes.Concat("-new-v2.0.0"u8.ToArray()).ToArray();
            await File.WriteAllBytesAsync(destination, oldExecutable);
            await File.WriteAllBytesAsync(source, newExecutable);
            await File.WriteAllBytesAsync(helper, fixtureBytes);
            var oldSettings = "{\"SchemaVersion\":3,\"FavoriteThemeIds\":[\"kept\"]}";
            var newSettings = "{\"SchemaVersion\":4,\"FavoriteThemeIds\":[\"kept\"],\"ThemeLibrarySort\":\"recent\"}";
            await File.WriteAllTextAsync(preferencesPath, oldSettings);
            await File.WriteAllTextAsync(Path.Combine(root, "fixture-next-settings.json"), newSettings);
            await File.WriteAllTextAsync(Path.Combine(root, "fixture-mode.txt"), "healthy-migrate");
            await File.WriteAllTextAsync(Path.Combine(root, "fixture-version.txt"), "v2.0.0");
            var snapshots = new UpdateDataSnapshotStore(data);
            var preUpdate = await snapshots.CreateAsync(Guid.NewGuid().ToString("N"), "v1.4.1");
            var installRequest = new PortableUpdateRequest(
                0,
                source,
                destination,
                Convert.ToHexString(SHA256.HashData(newExecutable)),
                "v2.0.0",
                resultPath,
                helper)
            {
                Operation = PortableUpdateOperation.Install,
                PreviousVersionLabel = "v1.4.1",
                StartupHealthToken = Guid.NewGuid().ToString("N"),
                DataSnapshotId = preUpdate.SnapshotId,
                DataSnapshotManifestSha256 = preUpdate.ManifestSha256,
            };
            var installExitCode = await UpdateBootstrapper.RunHelperAsync(installRequest);
            Ensure(installExitCode == 0 &&
                   File.ReadAllBytes(destination).SequenceEqual(newExecutable) &&
                   await File.ReadAllTextAsync(preferencesPath) == newSettings,
                "A healthy update must keep the new executable and its migrated configuration.");
            var rollbackStore = new UpdateRollbackStore(root, data, "Tessalume.exe");
            var rollback = await rollbackStore.LoadAsync();
            Ensure(rollback is not null && rollback.DataSnapshotId == preUpdate.SnapshotId,
                "A healthy update must retain the executable and pre-migration data snapshot as one rollback point.");
            await Task.Delay(2200);

            await File.WriteAllTextAsync(Path.Combine(root, "fixture-mode.txt"), "require-schema-3-stable");
            var forwardSnapshot = await snapshots.CreateAsync(Guid.NewGuid().ToString("N"), "v2.0.0");
            var rollbackRequest = new PortableUpdateRequest(
                0,
                destination + ".previous",
                destination,
                rollback!.BackupSha256,
                rollback.PreviousVersionLabel,
                resultPath,
                helper)
            {
                Operation = PortableUpdateOperation.Rollback,
                PreviousVersionLabel = rollback.CurrentVersionLabel,
                StartupHealthToken = Guid.NewGuid().ToString("N"),
                DataSnapshotId = rollback.DataSnapshotId,
                DataSnapshotManifestSha256 = rollback.DataSnapshotManifestSha256,
                RecoveryDataSnapshotId = forwardSnapshot.SnapshotId,
                RecoveryDataSnapshotManifestSha256 = forwardSnapshot.ManifestSha256,
            };
            var rollbackExitCode = await UpdateBootstrapper.RunHelperAsync(rollbackRequest);
            Ensure(rollbackExitCode == 0 &&
                   File.ReadAllBytes(destination).SequenceEqual(oldExecutable) &&
                   await File.ReadAllTextAsync(preferencesPath) == oldSettings &&
                   !File.Exists(destination + ".rollback-current") &&
                   await rollbackStore.LoadAsync() is null,
                "A stable manual rollback must start the old executable with its readable schema and retire the transactional executable backup.");
            Ensure(Directory.EnumerateFiles(
                       Path.Combine(data, "backups", "version-rollback"),
                       "ui-settings.json",
                       SearchOption.AllDirectories).Any(),
                "Manual rollback must preserve a recovery copy of the newer settings.");
            await WaitForFileWriteAccessAsync(destination, TimeSpan.FromSeconds(5));

            await File.WriteAllBytesAsync(destination, oldExecutable);
            await File.WriteAllBytesAsync(source, newExecutable);
            await File.WriteAllTextAsync(preferencesPath, oldSettings);
            await File.WriteAllTextAsync(Path.Combine(root, "fixture-mode.txt"), "exit");
            var failedPreUpdate = await snapshots.CreateAsync(Guid.NewGuid().ToString("N"), "v1.4.1");
            await File.WriteAllTextAsync(preferencesPath, newSettings);
            var failedRequest = installRequest with
            {
                StartupHealthToken = Guid.NewGuid().ToString("N"),
                DataSnapshotId = failedPreUpdate.SnapshotId,
                DataSnapshotManifestSha256 = failedPreUpdate.ManifestSha256,
            };
            _ = await UpdateBootstrapper.RunHelperAsync(failedRequest);
            var failedResult = PortableUpdateInstaller.ReadResult(resultPath);
            Ensure(failedResult is { Success: false, RolledBack: true } &&
                   File.ReadAllBytes(destination).SequenceEqual(oldExecutable) &&
                   await File.ReadAllTextAsync(preferencesPath) == oldSettings,
                "A new executable that exits before health confirmation must atomically restore both the old executable and its configuration schema.");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<byte[]> PrepareUpdateFixtureAsync(string destination)
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = Directory.GetParent(Directory.GetParent(AppContext.BaseDirectory)!.FullName)!.Name;
        var output = Path.Combine(
            repositoryRoot,
            "tests",
            "Tessalume.UpdateFixture",
            "bin",
            configuration,
            "net8.0");
        var executable = Path.Combine(output, "Tessalume.UpdateFixture.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Update integration fixture was not built.", executable);
        foreach (var file in Directory.EnumerateFiles(output, "Tessalume.UpdateFixture.*"))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        return await File.ReadAllBytesAsync(executable);
    }

    private static async Task WaitForFileWriteAccessAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
        throw new TimeoutException($"Timed out waiting for update fixture file access: {path}");
    }

    static async Task UpdatedApplicationWritesAStartupHealthMarkerAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-update-health-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "data");
        var themes = Path.Combine(root, "themes");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(themes);
        try
        {
            var token = Guid.NewGuid().ToString("N");
            Ensure(UpdateBootstrapper.TryParseStartupHealthToken(
                    ["--update-health", token],
                    out var parsedToken) && parsedToken == token,
                "Only a well-formed helper health token may enter normal application startup.");
            await UpdateBootstrapper.ConfirmStartupHealthyAsync(
                new PortableLayout(root, themes, data),
                token);
            var markerPath = Path.Combine(data, "updates", "health", $"{token}.json");
            var marker = await File.ReadAllTextAsync(markerPath);
            Ensure(marker.Contains(token, StringComparison.Ordinal) &&
                   marker.Contains(BrandInfo.VersionLabel, StringComparison.Ordinal),
                "The updated app must confirm its process and version after the startup catalog is ready.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task CompatibilityUpdaterFindsDedicatedVerifiedPacksAsync()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"tessalume-compat-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var archiveBytes = Encoding.UTF8.GetBytes("verified compatibility archive fixture");
            var sha256 = Convert.ToHexString(SHA256.HashData(archiveBytes));
            using var httpClient = new HttpClient(new StubHttpHandler(request =>
            {
                if (request.RequestUri!.Host == "api.github.com")
                {
                    var json = JsonSerializer.Serialize(new object[]
                    {
                        new
                        {
                            tag_name = "v2.0.0",
                            html_url = "https://github.com/lyc1uckYoo/tessalume/releases/tag/v2.0.0",
                            body = "Application release must be ignored by the compatibility client.",
                            draft = false,
                            prerelease = false,
                            assets = Array.Empty<object>(),
                        },
                        new
                        {
                            tag_name = "compat-v3.0.1",
                            html_url = "https://github.com/lyc1uckYoo/tessalume/releases/tag/compat-v3.0.1",
                            body = "Compatibility patch",
                            draft = false,
                            prerelease = false,
                            assets = new[]
                            {
                                new
                                {
                                    name = CompatibilityUpdateClient.ArchiveAssetName,
                                    browser_download_url = "https://downloads.example.test/Tessalume-Compatibility.zip",
                                    size = archiveBytes.Length,
                                    digest = $"sha256:{sha256.ToLowerInvariant()}",
                                },
                            },
                        },
                    });
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    };
                }
                if (request.RequestUri.Host == "downloads.example.test")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(archiveBytes),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }));
            using var client = new CompatibilityUpdateClient(
                httpClient,
                "lyc1uckYoo",
                "tessalume",
                dataDirectory,
                new Version(1, 4, 1));
            var release = await client.CheckLatestAsync(new Version(3, 0, 0));
            Ensure(release is not null && release.PackVersion == new Version(3, 0, 1) &&
                   release.Sha256 == sha256,
                "Compatibility updates must be discovered only from dedicated compatibility release tags.");
            var downloaded = await client.DownloadAsync(release!);
            Ensure(File.ReadAllBytes(downloaded).SequenceEqual(archiveBytes),
                "The compatibility updater must persist only the archive whose outer SHA-256 matches GitHub metadata.");
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    static async Task CompatibilityUpdaterPaginatesAndIgnoresPrereleasesAsync()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"tessalume-compat-pages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var archiveBytes = Encoding.UTF8.GetBytes("compatibility page fixture");
            var sha256 = Convert.ToHexString(SHA256.HashData(archiveBytes));
            var requestedPages = new List<string>();
            using var httpClient = new HttpClient(new StubHttpHandler(request =>
            {
                var query = request.RequestUri!.Query;
                requestedPages.Add(query);
                object[] releases;
                if (query.Contains("&page=1", StringComparison.Ordinal))
                {
                    releases = Enumerable.Range(0, 100).Select(index => (object)new
                    {
                        tag_name = $"v1.0.{index}",
                        html_url = $"https://github.com/lyc1uckYoo/tessalume/releases/tag/v1.0.{index}",
                        body = string.Empty,
                        draft = false,
                        prerelease = false,
                        assets = Array.Empty<object>(),
                    }).ToArray();
                }
                else
                {
                    releases =
                    [
                        new
                        {
                            tag_name = "compat-v9.0.0",
                            html_url = "https://github.com/lyc1uckYoo/tessalume/releases/tag/compat-v9.0.0",
                            body = "must be ignored",
                            draft = false,
                            prerelease = true,
                            assets = Array.Empty<object>(),
                        },
                        new
                        {
                            tag_name = "compat-v3.0.1",
                            html_url = "https://github.com/lyc1uckYoo/tessalume/releases/tag/compat-v3.0.1",
                            body = "stable compatibility patch",
                            draft = false,
                            prerelease = false,
                            assets = new[]
                            {
                                new
                                {
                                    name = CompatibilityUpdateClient.ArchiveAssetName,
                                    browser_download_url = "https://downloads.example.test/Tessalume-Compatibility.zip",
                                    size = archiveBytes.Length,
                                    digest = $"sha256:{sha256.ToLowerInvariant()}",
                                },
                            },
                        },
                    ];
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(releases), Encoding.UTF8, "application/json"),
                };
            }));
            using var client = new CompatibilityUpdateClient(
                httpClient,
                "lyc1uckYoo",
                "tessalume",
                dataDirectory,
                new Version(2, 0, 0));
            var release = await client.CheckLatestAsync(new Version(3, 0, 0));
            Ensure(release?.PackVersion == new Version(3, 0, 1) && requestedPages.Count == 2,
                "Compatibility discovery must continue beyond the first GitHub page and never install prerelease rules.");
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

}
