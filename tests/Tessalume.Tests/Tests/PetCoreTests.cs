using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Tessalume.Core.Pets;

internal static partial class TestSuite
{
    static async Task PetPackageValidationRejectsUnsafeOrInconsistentAssetsAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var builtIn = await new PetPackageLoader().LoadAsync(Path.Combine(
            repositoryRoot,
            "pets",
            "flying-snowfluff"));
        Ensure(builtIn.Validation.IsValid && builtIn.Package is not null,
            "The bundled flying-snowfluff package must pass the Core validator.");
        var builtInPackage = builtIn.Package ?? throw new InvalidOperationException("Bundled pet did not load.");
        Ensure(builtInPackage.Catalog.Protocol.UsedFrameCount == 74 &&
               builtInPackage.Catalog.Protocol.States.Count == 11 &&
               builtInPackage.Catalog.Protocol.States.Sum(state => state.Frames) == 74,
            "The validator must preserve the source atlas's truthful 11-row / 74-cell layout.");
        Ensure(builtInPackage.SpritesheetInfo is
            { Width: 1536, Height: 2288, HasAlpha: true, Encoding: "VP8L" },
            "The bundled lossless WebP header must declare the fixed dimensions and alpha channel.");

        using var fixture = await PetCoreFixture.CreateAsync();
        var loader = new PetPackageLoader();
        var valid = await loader.LoadAsync(fixture.PackageRoot);
        Ensure(valid.Validation.IsValid && valid.Package is not null,
            "A synthetic fixed-protocol pet package should load.");

        var traversalRoot = fixture.CopyPackage("traversal");
        await PetMutateCatalogAsync(traversalRoot, catalog =>
        {
            var files = catalog["files"]!.AsArray();
            files[1]!["path"] = "../escaped.webp";
        });
        var traversal = await loader.LoadAsync(traversalRoot);
        Ensure(!traversal.Validation.IsValid && traversal.Validation.Issues.Any(issue =>
                   issue.Code == "catalog.file.path.invalid"),
            "Catalog path traversal must be rejected before reading outside the package.");

        var remoteRoot = fixture.CopyPackage("remote");
        var remoteManifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(remoteRoot, "pet.json")))!;
        remoteManifest["spritesheetPath"] = "https://example.invalid/pet.webp";
        await File.WriteAllTextAsync(
            Path.Combine(remoteRoot, "pet.json"),
            remoteManifest.ToJsonString(PetJsonOptions));
        await PetRefreshCatalogFileAsync(remoteRoot, "pet.json");
        var remote = await loader.LoadAsync(remoteRoot);
        Ensure(!remote.Validation.IsValid && remote.Validation.Issues.Any(issue =>
                   issue.Code == "manifest.spritesheet-path.invalid"),
            "Remote pet resources must never be accepted.");

        var missingRoot = fixture.CopyPackage("missing");
        File.Delete(Path.Combine(missingRoot, "spritesheet.webp"));
        var missing = await loader.LoadAsync(missingRoot);
        Ensure(!missing.Validation.IsValid && missing.Validation.Issues.Any(issue =>
                   issue.Code == "catalog.file.missing-or-unsafe"),
            "Missing declared pet assets must fail validation.");

        var badHashRoot = fixture.CopyPackage("bad-hash");
        await PetMutateCatalogAsync(badHashRoot, catalog =>
        {
            catalog["files"]!.AsArray()[1]!["sha256"] = new string('0', 64);
        });
        var badHash = await loader.LoadAsync(badHashRoot);
        Ensure(!badHash.Validation.IsValid && badHash.Validation.Issues.Any(issue =>
                   issue.Code == "catalog.file.hash.mismatch"),
            "A spritesheet hash mismatch must fail validation.");

        var falseCountRoot = fixture.CopyPackage("false-count");
        await PetMutateCatalogAsync(falseCountRoot, catalog =>
        {
            catalog["protocol"]!["usedFrameCount"] = 79;
        });
        var falseCount = await loader.LoadAsync(falseCountRoot);
        Ensure(!falseCount.Validation.IsValid && falseCount.Validation.Issues.Any(issue =>
                   issue.Code == "catalog.protocol.frame-count.invalid"),
            "A catalog that falsely claims 79 cells or disagrees with its row sum must be rejected.");

        var structuredNullRoot = fixture.CopyPackage("structured-null");
        await PetMutateCatalogAsync(structuredNullRoot, catalog =>
        {
            catalog["files"]!.AsArray()[0] = null;
            catalog["previews"]!.AsArray()[0] = null;
            catalog["protocol"]!["states"]!.AsArray()[0] = null;
        });
        var structuredNull = await loader.LoadAsync(structuredNullRoot);
        Ensure(!structuredNull.Validation.IsValid &&
               structuredNull.Validation.Issues.Any(issue => issue.Code == "catalog.file.null") &&
               structuredNull.Validation.Issues.Any(issue => issue.Code == "catalog.preview.null") &&
               structuredNull.Validation.Issues.Any(issue => issue.Code == "catalog.protocol.state.null"),
            "Structured null entries must produce validation issues instead of escaping as NullReferenceException.");

        if (OperatingSystem.IsWindows())
        {
            var hardLinkRoot = fixture.CopyPackage("hard-link");
            var linkedPreview = Path.Combine(hardLinkRoot, "previews", "idle.png");
            var externalPreview = Path.Combine(fixture.Root, "outside-preview.png");
            File.Copy(linkedPreview, externalPreview);
            File.Delete(linkedPreview);
            if (CreateHardLink(linkedPreview, externalPreview, IntPtr.Zero))
            {
                var hardLinked = await loader.LoadAsync(hardLinkRoot);
                Ensure(!hardLinked.Validation.IsValid && hardLinked.Validation.Issues.Any(issue =>
                           issue.Code is "catalog.file.missing-or-unsafe" or "package.enumeration.failed"),
                    "Package assets with multiple hard-link names must be rejected at the regular-file boundary.");
            }

            var symbolicLinkRoot = fixture.CopyPackage("symbolic-link");
            var linkedCatalog = Path.Combine(symbolicLinkRoot, "catalog.json");
            var externalCatalog = Path.Combine(fixture.Root, "outside-catalog.json");
            File.Move(linkedCatalog, externalCatalog);
            try
            {
                File.CreateSymbolicLink(linkedCatalog, externalCatalog);
                var symbolic = await loader.LoadAsync(symbolicLinkRoot);
                Ensure(!symbolic.Validation.IsValid && symbolic.Validation.Issues.Any(issue =>
                           issue.Code == "catalog.unreadable"),
                    "A catalog reparse point must be rejected before its external bytes are parsed.");
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // The security assertion runs where the test volume permits symbolic links.
            }
        }

        var duplicateCatalog = Path.Combine(fixture.Root, "duplicate-catalog");
        Directory.CreateDirectory(duplicateCatalog);
        PetCopyDirectory(fixture.PackageRoot, Path.Combine(duplicateCatalog, "folder-one"));
        PetCopyDirectory(fixture.PackageRoot, Path.Combine(duplicateCatalog, "unrelated-folder-name"));
        var candidates = await new PetCatalogScanner(loader).ScanAsync(duplicateCatalog);
        Ensure(candidates.Count == 2 && candidates.All(candidate =>
                   !candidate.Validation.IsValid && candidate.Validation.Issues.Any(issue =>
                       issue.Code == "catalog.pet-id.duplicate")),
            "Catalog scanning must find duplicate IDs from pet.json metadata, not folder names.");

        if (OperatingSystem.IsWindows())
        {
            var isolatedCatalog = Path.Combine(fixture.Root, "isolated-catalog");
            Directory.CreateDirectory(isolatedCatalog);
            PetCopyDirectory(fixture.PackageRoot, Path.Combine(isolatedCatalog, "valid"));
            try
            {
                Directory.CreateSymbolicLink(
                    Path.Combine(isolatedCatalog, "linked"),
                    fixture.PackageRoot);
                var isolated = await new PetCatalogScanner(loader).ScanAsync(isolatedCatalog);
                Ensure(isolated.Count == 2 && isolated.Count(candidate => candidate.Validation.IsValid) == 1,
                    "One reparse-point package must remain an invalid candidate without aborting catalog scanning.");
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // The scanner still has deterministic coverage on volumes that allow directory links.
            }
        }
    }

    static async Task PetInstallerTransactionsAreAtomicAndScopedAsync()
    {
        using var fixtureV1 = await PetCoreFixture.CreateAsync(productVersion: "1.0.0", payloadMarker: 0);
        using var fixtureV2 = await PetCoreFixture.CreateAsync(productVersion: "2.0.0", payloadMarker: 1);
        using var fixtureV3 = await PetCoreFixture.CreateAsync(productVersion: "3.0.0", payloadMarker: 2);
        var packageV1 = (await new PetPackageLoader().LoadAsync(fixtureV1.PackageRoot)).Package!;
        var packageV2 = (await new PetPackageLoader().LoadAsync(fixtureV2.PackageRoot)).Package!;
        var packageV3 = (await new PetPackageLoader().LoadAsync(fixtureV3.PackageRoot)).Package!;
        var operationRoot = Path.Combine(Path.GetTempPath(), $"tessalume-pet-operations-{Guid.NewGuid():N}");
        var petsRoot = Path.Combine(operationRoot, "codex-pets-fixture");
        var backupRoot = Path.Combine(operationRoot, "backups");
        var statePath = Path.Combine(operationRoot, "state", "pets.json");
        Directory.CreateDirectory(operationRoot);
        try
        {
            var options = new PetInstallerOptions(petsRoot, backupRoot, statePath);
            using (var installer = new PetInstaller(options))
            {
                Ensure((await installer.InspectAsync(packageV1)).Status == PetInstallationStatus.NotInstalled,
                    "A fresh injected pets root must report NotInstalled.");
                var first = await installer.InstallAsync(packageV1, PetInstallIntent.Install);
                Ensure(first.Changed && first.Snapshot.Status ==
                           PetInstallationStatus.InstalledAwaitingCodexSelection,
                    "A first install must remain truthful about waiting for Codex selection.");
                var idempotent = await installer.InstallAsync(packageV1, PetInstallIntent.Install);
                Ensure(!idempotent.Changed,
                    "Installing the exact managed package twice must be idempotent.");
                Ensure((await installer.MarkCodexSelectionAcknowledgedAsync(packageV1)).Status ==
                       PetInstallationStatus.Installed,
                    "Only an explicit user acknowledgement may change the managed status to Installed.");

                Ensure((await installer.InspectAsync(packageV2)).Status == PetInstallationStatus.UpdateAvailable,
                    "A newer product version must report UpdateAvailable.");
                Ensure(await PetThrowsAsync<InvalidOperationException>(() =>
                        installer.InstallAsync(packageV2, PetInstallIntent.Install)),
                    "An update must not proceed without the explicit update intent.");
                var update = await installer.InstallAsync(packageV2, PetInstallIntent.UpdateConfirmed);
                Ensure(update.Changed && update.BackupId is not null && update.Snapshot.Status ==
                           PetInstallationStatus.InstalledAwaitingCodexSelection,
                    "A confirmed update must preserve a backup and require selection acknowledgement again.");
            }

            var installedSheet = Path.Combine(petsRoot, packageV2.Manifest.Id, "spritesheet.webp");
            var v2Hash = await PetHashFileAsync(installedSheet);
            using (var failing = new PetInstaller(
                       options,
                       new PetPackageLoader(),
                       new ThrowBeforePromotePetObserver()))
            {
                Ensure((await failing.InspectAsync(packageV3)).Status == PetInstallationStatus.UpdateAvailable,
                    "The failure fixture must begin from a valid update state.");
                Ensure(await PetThrowsAsync<PetInjectedTransactionException>(() =>
                        failing.InstallAsync(packageV3, PetInstallIntent.UpdateConfirmed)),
                    "The injected promote failure must escape the transaction.");
            }
            Ensure(File.Exists(installedSheet) && await PetHashFileAsync(installedSheet) == v2Hash,
                "A failure after moving the old directory must restore the exact previous files.");
            using (var failingAfterPromote = new PetInstaller(
                       options,
                       new PetPackageLoader(),
                       new ThrowAtPetPhasesObserver(PetTransactionPhase.Promoted)))
            {
                Ensure(await PetThrowsAsync<PetInjectedTransactionException>(() =>
                        failingAfterPromote.InstallAsync(packageV3, PetInstallIntent.UpdateConfirmed)),
                    "A failure after promotion must escape only after restoring the previous directory.");
            }
            Ensure(File.Exists(installedSheet) && await PetHashFileAsync(installedSheet) == v2Hash,
                "A promoted replacement must be removed and the old directory restored on failure.");
            using (var inspector = new PetInstaller(options))
            {
                Ensure((await inspector.InspectAsync(packageV2)).Status ==
                       PetInstallationStatus.InstalledAwaitingCodexSelection,
                    "A failed update must leave the previous managed state intact.");

                var sheetBytes = await File.ReadAllBytesAsync(installedSheet);
                sheetBytes[^1] ^= 0x7f;
                await File.WriteAllBytesAsync(installedSheet, sheetBytes);
                Ensure((await inspector.InspectAsync(packageV2)).Status ==
                       PetInstallationStatus.UnknownModification,
                    "A changed managed file must be classified as UnknownModification.");
                Ensure(await PetThrowsAsync<InvalidOperationException>(() =>
                        inspector.InstallAsync(packageV2, PetInstallIntent.RepairConfirmed)),
                    "Unknown modifications must not be silently treated as ordinary damage.");
                await inspector.InstallAsync(packageV2, PetInstallIntent.ReplaceConfirmed);

                File.Delete(installedSheet);
                Ensure((await inspector.InspectAsync(packageV2)).Status == PetInstallationStatus.Damaged,
                    "A missing managed file must be classified as a damaged installation.");
                await inspector.InstallAsync(packageV2, PetInstallIntent.RepairConfirmed);

                var notePath = Path.Combine(petsRoot, packageV2.Manifest.Id, "keep-me.txt");
                await File.WriteAllTextAsync(notePath, "unmanaged user file");
                PetUninstallResult uninstall;
                var noteTimestamp = File.GetLastWriteTimeUtc(notePath);
                await using (var heldUnknownFile = new FileStream(
                                 notePath,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.ReadWrite))
                {
                    using (var failingUninstall = new PetInstaller(
                               options,
                               new PetPackageLoader(),
                               new ThrowAtPetPhasesObserver(PetTransactionPhase.BeforeStateSave)))
                    {
                        Ensure(await PetThrowsAsync<PetInjectedTransactionException>(() =>
                                failingUninstall.UninstallAsync(packageV2, PetUninstallIntent.Safe)) &&
                               File.Exists(installedSheet) && File.Exists(notePath),
                            "A state-save failure during uninstall must put every moved managed file back in place.");
                    }
                    uninstall = await inspector.UninstallAsync(packageV2, PetUninstallIntent.Safe);
                }
                Ensure(uninstall.Changed && uninstall.BackupId is not null &&
                       !File.Exists(Path.Combine(petsRoot, packageV2.Manifest.Id, "pet.json")) &&
                       !File.Exists(installedSheet) && await File.ReadAllTextAsync(notePath) == "unmanaged user file" &&
                       File.GetLastWriteTimeUtc(notePath) == noteTimestamp,
                    "Safe uninstall must remove only recorded managed files and preserve unrelated content.");
                var uninstallBackupId = uninstall.BackupId ??
                                        throw new InvalidOperationException("Uninstall backup was not retained.");

                var traversalBackupId = "tampered-traversal-backup";
                var traversalBackup = Path.Combine(backupRoot, traversalBackupId);
                PetCopyDirectory(Path.Combine(backupRoot, uninstallBackupId), traversalBackup);
                await PetMutateBackupAsync(traversalBackup, backupNode =>
                {
                    backupNode["backupId"] = traversalBackupId;
                    backupNode["petId"] = "../../escape";
                });
                Ensure(await PetThrowsAsync<InvalidDataException>(() =>
                        inspector.RestoreBackupAsync(traversalBackupId, confirmed: true)),
                    "A tampered backup pet ID must be rejected before it can influence a destination path.");

                var nullBackupId = "tampered-null-backup";
                var nullBackup = Path.Combine(backupRoot, nullBackupId);
                PetCopyDirectory(Path.Combine(backupRoot, uninstallBackupId), nullBackup);
                await PetMutateBackupAsync(nullBackup, backupNode =>
                {
                    backupNode["backupId"] = nullBackupId;
                    backupNode["directories"]!.AsArray()[0] = null;
                });
                Ensure(await PetThrowsAsync<InvalidDataException>(() =>
                        inspector.RestoreBackupAsync(nullBackupId, confirmed: true)),
                    "Structured null backup entries must be rejected without a NullReferenceException.");

                var nullHashBackupId = "tampered-null-hash-backup";
                var nullHashBackup = Path.Combine(backupRoot, nullHashBackupId);
                PetCopyDirectory(Path.Combine(backupRoot, uninstallBackupId), nullHashBackup);
                await PetMutateBackupAsync(nullHashBackup, backupNode =>
                {
                    backupNode["backupId"] = nullHashBackupId;
                    backupNode["directories"]!.AsArray()[0]!["files"]!.AsArray()[0]!["sha256"] = null;
                });
                Ensure(await PetThrowsAsync<InvalidDataException>(() =>
                        inspector.RestoreBackupAsync(nullHashBackupId, confirmed: true)),
                    "A structured null backup file hash must be rejected without a NullReferenceException.");

                var restored = await inspector.RestoreBackupAsync(uninstallBackupId, confirmed: true);
                Ensure(restored.Changed && File.Exists(installedSheet) && File.Exists(notePath),
                    "A validated uninstall backup must be recoverable without dropping unrelated files.");
                var restoredState = await inspector.LoadManagementStateAsync();
                Ensure(restoredState.State.Pets.TryGetValue(packageV2.Manifest.Id, out var restoredManaged) &&
                       !restoredManaged.SelectionAcknowledged,
                    "Restoring managed files must always require a fresh Codex selection acknowledgement.");

                var restoredDirectory = Path.Combine(petsRoot, packageV2.Manifest.Id);
                Directory.Delete(restoredDirectory, recursive: true);
                Directory.CreateDirectory(restoredDirectory);
                var otherManifestPath = Path.Combine(restoredDirectory, "pet.json");
                var otherSentinelPath = Path.Combine(restoredDirectory, "other-pet-sentinel.txt");
                await File.WriteAllTextAsync(
                    otherManifestPath,
                    "{\"id\":\"other-pet\",\"displayName\":\"Other\",\"description\":\"fixture\",\"spriteVersionNumber\":2,\"spritesheetPath\":\"other.webp\"}");
                await File.WriteAllTextAsync(otherSentinelPath, "must survive refused restore");
                Ensure(await PetThrowsAsync<InvalidDataException>(() =>
                           inspector.RestoreBackupAsync(uninstallBackupId, confirmed: true)) &&
                       File.Exists(otherSentinelPath) &&
                       (await File.ReadAllTextAsync(otherManifestPath)).Contains(
                           "other-pet",
                           StringComparison.Ordinal),
                    "Backup restore must refuse to replace a directory name that now belongs to another pet ID.");
            }

            var concurrentPetsRoot = Path.Combine(operationRoot, "concurrent-pets");
            var concurrentOptions = new PetInstallerOptions(
                concurrentPetsRoot,
                Path.Combine(operationRoot, "concurrent-backups"),
                Path.Combine(operationRoot, "concurrent-state", "pets.json"));
            using (var initial = new PetInstaller(concurrentOptions))
            {
                await initial.InstallAsync(packageV1, PetInstallIntent.Install);
            }
            using (var concurrentMutation = new PetInstaller(
                       concurrentOptions,
                       new PetPackageLoader(),
                       new PetPhaseActionObserver(
                           PetTransactionPhase.BackupCompleted,
                           () =>
                           {
                               var rollbackDirectory = Directory.EnumerateDirectories(
                                   concurrentPetsRoot,
                                   ".tessalume-rollback-*",
                                   SearchOption.TopDirectoryOnly).Single();
                               File.WriteAllText(
                                   Path.Combine(rollbackDirectory, "late-write.txt"),
                                   "arrived after backup");
                           })))
            {
                Ensure(await PetThrowsAsync<IOException>(() =>
                        concurrentMutation.InstallAsync(packageV2, PetInstallIntent.UpdateConfirmed)),
                    "A directory mutation after durable backup must abort promotion.");
            }
            Ensure(File.Exists(Path.Combine(
                       concurrentPetsRoot,
                       packageV1.Manifest.Id,
                       "late-write.txt")),
                "A post-backup concurrent write must return with the original directory instead of being deleted.");

            var collisionPetsRoot = Path.Combine(operationRoot, "external-collision-pets");
            var collisionOptions = new PetInstallerOptions(
                collisionPetsRoot,
                Path.Combine(operationRoot, "external-collision-backups"),
                Path.Combine(operationRoot, "external-collision-state", "pets.json"));
            var legacyDirectory = Path.Combine(collisionPetsRoot, "legacy-pet-folder");
            PetCopyInstallFiles(packageV1, legacyDirectory);
            var collisionTarget = Path.Combine(collisionPetsRoot, packageV1.Manifest.Id);
            var collisionSentinel = Path.Combine(collisionTarget, "external-sentinel.txt");
            using (var targetCollision = new PetInstaller(
                       collisionOptions,
                       new PetPackageLoader(),
                       new PetPhaseActionObserver(
                           PetTransactionPhase.BeforePromote,
                           () =>
                           {
                               Directory.CreateDirectory(collisionTarget);
                               File.WriteAllText(collisionSentinel, "must not be deleted by rollback");
                           })))
            {
                Ensure((await targetCollision.InspectAsync(packageV1)).Status ==
                       PetInstallationStatus.DuplicateIdConflict,
                    "The collision fixture must start as an unmanaged same-ID directory.");
                Ensure(await PetThrowsAsync<IOException>(() =>
                        targetCollision.InstallAsync(packageV1, PetInstallIntent.ReplaceConfirmed)),
                    "An external directory that claims the target immediately before promote must abort installation.");
            }
            Ensure(File.Exists(collisionSentinel) && Directory.Exists(legacyDirectory) &&
                   await PetHashFileAsync(Path.Combine(legacyDirectory, "spritesheet.webp")) ==
                   await PetHashFileAsync(packageV1.ResolvedFiles["spritesheet.webp"]),
                "Rollback must preserve an externally-created target and restore the displaced pet directory.");

            var changedUpdatePetsRoot = Path.Combine(operationRoot, "changed-update-pets");
            var changedUpdateOptions = new PetInstallerOptions(
                changedUpdatePetsRoot,
                Path.Combine(operationRoot, "changed-update-backups"),
                Path.Combine(operationRoot, "changed-update-state", "pets.json"));
            using (var initial = new PetInstaller(changedUpdateOptions))
            {
                await initial.InstallAsync(packageV1, PetInstallIntent.Install);
            }
            using (var changedUpdate = new PetInstaller(
                       changedUpdateOptions,
                       new PetPackageLoader(),
                       new PetPhaseActionObserver(
                           PetTransactionPhase.ExistingMoved,
                           () =>
                           {
                               var rollbackDirectory = Directory.EnumerateDirectories(
                                   changedUpdatePetsRoot,
                                   ".tessalume-rollback-*",
                                   SearchOption.TopDirectoryOnly).Single();
                               File.WriteAllText(
                                   Path.Combine(rollbackDirectory, "appeared-after-confirmation.txt"),
                                   "unknown concurrent content");
                           })))
            {
                Ensure(await PetThrowsAsync<InvalidOperationException>(() =>
                        changedUpdate.InstallAsync(packageV2, PetInstallIntent.UpdateConfirmed)),
                    "An update intent must be rejected when unknown content appears after confirmation.");
            }
            Ensure(File.Exists(Path.Combine(
                       changedUpdatePetsRoot,
                       packageV1.Manifest.Id,
                       "appeared-after-confirmation.txt")),
                "A rejected update must restore the concurrently-added unknown file with the old directory.");

            var changedRepairPetsRoot = Path.Combine(operationRoot, "changed-repair-pets");
            var changedRepairOptions = new PetInstallerOptions(
                changedRepairPetsRoot,
                Path.Combine(operationRoot, "changed-repair-backups"),
                Path.Combine(operationRoot, "changed-repair-state", "pets.json"));
            using (var initial = new PetInstaller(changedRepairOptions))
            {
                await initial.InstallAsync(packageV1, PetInstallIntent.Install);
            }
            var changedRepairTarget = Path.Combine(changedRepairPetsRoot, packageV1.Manifest.Id);
            File.Delete(Path.Combine(changedRepairTarget, "spritesheet.webp"));
            using (var changedRepair = new PetInstaller(
                       changedRepairOptions,
                       new PetPackageLoader(),
                       new PetPhaseActionObserver(
                           PetTransactionPhase.ExistingMoved,
                           () =>
                           {
                               var rollbackDirectory = Directory.EnumerateDirectories(
                                   changedRepairPetsRoot,
                                   ".tessalume-rollback-*",
                                   SearchOption.TopDirectoryOnly).Single();
                               File.WriteAllText(
                                   Path.Combine(rollbackDirectory, "pet.json"),
                                   "{\"id\":\"other-pet\",\"displayName\":\"Other\",\"description\":\"fixture\"," +
                                   "\"spriteVersionNumber\":2,\"spritesheetPath\":\"other.webp\"}");
                           })))
            {
                Ensure((await changedRepair.InspectAsync(packageV1)).Status == PetInstallationStatus.Damaged,
                    "The repair race fixture must begin as a damaged installation.");
                Ensure(await PetThrowsAsync<InvalidOperationException>(() =>
                        changedRepair.InstallAsync(packageV1, PetInstallIntent.RepairConfirmed)),
                    "A repair intent must not replace a manifest that becomes another valid pet ID.");
            }
            Ensure((await File.ReadAllTextAsync(Path.Combine(changedRepairTarget, "pet.json")))
                       .Contains("other-pet", StringComparison.Ordinal),
                "A rejected repair must restore the newly-recognizable manifest instead of deleting it.");

            var changedUninstallPetsRoot = Path.Combine(operationRoot, "changed-uninstall-pets");
            var changedUninstallOptions = new PetInstallerOptions(
                changedUninstallPetsRoot,
                Path.Combine(operationRoot, "changed-uninstall-backups"),
                Path.Combine(operationRoot, "changed-uninstall-state", "pets.json"));
            using (var initial = new PetInstaller(changedUninstallOptions))
            {
                await initial.InstallAsync(packageV1, PetInstallIntent.Install);
            }
            var changedUninstallTarget = Path.Combine(changedUninstallPetsRoot, packageV1.Manifest.Id);
            using (var changedUninstall = new PetInstaller(
                       changedUninstallOptions,
                       new PetPackageLoader(),
                       new PetPhaseActionObserver(
                           PetTransactionPhase.ExistingMoved,
                           () =>
                           {
                               var rollbackDirectory = Directory.EnumerateDirectories(
                                   changedUninstallPetsRoot,
                                   ".tessalume-uninstall-rollback-*",
                                   SearchOption.TopDirectoryOnly).Single();
                               File.Copy(
                                   packageV2.ResolvedFiles["spritesheet.webp"],
                                   Path.Combine(rollbackDirectory, "spritesheet.webp"),
                                   overwrite: true);
                           })))
            {
                Ensure(await PetThrowsAsync<InvalidOperationException>(() =>
                        changedUninstall.UninstallAsync(packageV1, PetUninstallIntent.Safe)),
                    "Safe uninstall must stop when a managed file changes after the initial integrity check.");
            }
            Ensure(await PetHashFileAsync(Path.Combine(changedUninstallTarget, "spritesheet.webp")) ==
                   await PetHashFileAsync(packageV2.ResolvedFiles["spritesheet.webp"]),
                "A rejected safe uninstall must put the concurrently-modified managed file back in place.");

            var changedRestorePetsRoot = Path.Combine(operationRoot, "changed-restore-pets");
            var changedRestoreOptions = new PetInstallerOptions(
                changedRestorePetsRoot,
                Path.Combine(operationRoot, "changed-restore-backups"),
                Path.Combine(operationRoot, "changed-restore-state", "pets.json"));
            string changedRestoreBackupId;
            using (var initial = new PetInstaller(changedRestoreOptions))
            {
                await initial.InstallAsync(packageV1, PetInstallIntent.Install);
                changedRestoreBackupId = (await initial.UninstallAsync(
                    packageV1,
                    PetUninstallIntent.Safe)).BackupId ??
                    throw new InvalidOperationException("Restore race fixture did not retain its uninstall backup.");
                await initial.RestoreBackupAsync(changedRestoreBackupId, confirmed: true);
            }
            var changedRestoreTarget = Path.Combine(changedRestorePetsRoot, packageV1.Manifest.Id);
            using (var changedRestore = new PetInstaller(
                       changedRestoreOptions,
                       new PetPackageLoader(),
                       new PetPhaseActionObserver(
                           PetTransactionPhase.ExistingMoved,
                           () =>
                           {
                               var rollbackDirectory = Directory.EnumerateDirectories(
                                   changedRestorePetsRoot,
                                   ".tessalume-rollback-*",
                                   SearchOption.TopDirectoryOnly).Single();
                               File.WriteAllText(
                                   Path.Combine(rollbackDirectory, "pet.json"),
                                   "{\"id\":\"other-pet\",\"displayName\":\"Other\",\"description\":\"fixture\"," +
                                   "\"spriteVersionNumber\":2,\"spritesheetPath\":\"other.webp\"}");
                           })))
            {
                Ensure(await PetThrowsAsync<InvalidDataException>(() =>
                        changedRestore.RestoreBackupAsync(changedRestoreBackupId, confirmed: true)),
                    "Restore must re-check a displaced target's pet ID after the initial confirmation.");
            }
            Ensure((await File.ReadAllTextAsync(Path.Combine(changedRestoreTarget, "pet.json")))
                       .Contains("other-pet", StringComparison.Ordinal),
                "A refused restore must preserve the target that changed to another pet ID.");

            var secondaryPetsRoot = Path.Combine(operationRoot, "secondary-failure-pets");
            var secondaryOptions = new PetInstallerOptions(
                secondaryPetsRoot,
                Path.Combine(operationRoot, "secondary-failure-backups"),
                Path.Combine(operationRoot, "secondary-failure-state", "pets.json"));
            using (var initial = new PetInstaller(secondaryOptions))
            {
                await initial.InstallAsync(packageV1, PetInstallIntent.Install);
            }
            PetTransactionRollbackException? rollbackException = null;
            using (var doubleFailure = new PetInstaller(
                       secondaryOptions,
                       new PetPackageLoader(),
                       new ThrowAtPetPhasesObserver(
                           PetTransactionPhase.BeforeStateSave,
                           PetTransactionPhase.BeforeRollbackDeletePromoted)))
            {
                try
                {
                    await doubleFailure.InstallAsync(packageV2, PetInstallIntent.UpdateConfirmed);
                }
                catch (PetTransactionRollbackException exception)
                {
                    rollbackException = exception;
                }
            }
            var oldRollbackDirectory = Directory.EnumerateDirectories(
                    secondaryPetsRoot,
                    ".tessalume-rollback-*",
                    SearchOption.TopDirectoryOnly)
                .SingleOrDefault();
            Ensure(rollbackException is not null && rollbackException.RecoveryPaths.Count > 0 &&
                   oldRollbackDirectory is not null &&
                   await PetHashFileAsync(Path.Combine(oldRollbackDirectory, "spritesheet.webp")) ==
                   await PetHashFileAsync(packageV1.ResolvedFiles["spritesheet.webp"]),
                "A secondary rollback failure must be surfaced while retaining the exact old directory.");

            var missingPetsRoot = Path.Combine(operationRoot, "missing-managed-pets");
            var missingOptions = new PetInstallerOptions(
                missingPetsRoot,
                Path.Combine(operationRoot, "missing-managed-backups"),
                Path.Combine(operationRoot, "missing-managed-state", "pets.json"));
            using (var missingInstaller = new PetInstaller(missingOptions))
            {
                await missingInstaller.InstallAsync(packageV1, PetInstallIntent.Install);
                var missingDirectory = Path.Combine(missingPetsRoot, packageV1.Manifest.Id);
                File.Delete(Path.Combine(missingDirectory, "pet.json"));
                File.Delete(Path.Combine(missingDirectory, "spritesheet.webp"));
                await missingInstaller.UninstallAsync(packageV1, PetUninstallIntent.Safe);
                Ensure(!Directory.Exists(missingDirectory) &&
                       (await missingInstaller.InspectAsync(packageV1)).Status ==
                       PetInstallationStatus.NotInstalled,
                    "Uninstalling an empty damaged managed directory must remove the empty directory and state.");
            }

            Ensure(!Directory.EnumerateDirectories(petsRoot, ".tessalume-*", SearchOption.TopDirectoryOnly).Any(),
                "Completed and rolled-back operations must not leave staging or local rollback directories.");
            Ensure(Directory.EnumerateDirectories(backupRoot, "*", SearchOption.TopDirectoryOnly).Any(),
                "Updates, repairs, replacement, and uninstall must retain durable recovery backups.");
        }
        finally
        {
            if (Directory.Exists(operationRoot)) Directory.Delete(operationRoot, recursive: true);
        }
    }

    static async Task PetStatusStateAndIdScanningRemainTruthfulAsync()
    {
        using var fixture = await PetCoreFixture.CreateAsync();
        var package = (await new PetPackageLoader().LoadAsync(fixture.PackageRoot)).Package!;
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-pet-status-{Guid.NewGuid():N}");
        var petsRoot = Path.Combine(root, "pets");
        var backupRoot = Path.Combine(root, "backups");
        var statePath = Path.Combine(root, "state", "pets.json");
        Directory.CreateDirectory(petsRoot);
        try
        {
            var differentlyNamed = Path.Combine(petsRoot, "folder-name-does-not-identify-pet");
            Directory.CreateDirectory(differentlyNamed);
            foreach (var file in package.InstallFiles)
            {
                File.Copy(package.ResolvedFiles[file.Path], Path.Combine(differentlyNamed, file.Path));
            }
            var options = new PetInstallerOptions(petsRoot, backupRoot, statePath);
            using (var installer = new PetInstaller(options))
            {
                var conflict = await installer.InspectAsync(package);
                Ensure(conflict.Status == PetInstallationStatus.DuplicateIdConflict &&
                       conflict.InstalledDirectories.Single() == differentlyNamed,
                    "Installed pet discovery must use pet.json ID rather than the directory name.");
                var replacement = await installer.InstallAsync(package, PetInstallIntent.ReplaceConfirmed);
                Ensure(replacement.Changed && replacement.BackupId is not null &&
                       Directory.Exists(Path.Combine(petsRoot, package.Manifest.Id)) &&
                       !Directory.Exists(differentlyNamed),
                    "An explicitly confirmed ID-conflict replacement must back up the old directory.");

                var disclosure = await installer.MarkInformationalDisclosureShownAsync();
                Ensure(disclosure.InformationalDisclosureShown,
                    "The privacy note is informational state, not a trust gate.");
                var suggestion = await installer.MarkCompanionSuggestionShownAsync(package.Manifest.Id);
                var duplicateSuggestion = await installer.MarkCompanionSuggestionShownAsync(package.Manifest.Id);
                Ensure(suggestion.CompanionSuggestionShownIds.SequenceEqual([package.Manifest.Id]) &&
                       duplicateSuggestion.CompanionSuggestionShownIds.Count == 1,
                    "Companion theme suggestions must be persisted once per pet ID.");
                var claims = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
                    installer.TryMarkCompanionSuggestionShownAsync("test.atomic-claim")));
                Ensure(claims.Count(claimed => claimed) == 1 &&
                       !await installer.TryMarkCompanionSuggestionShownAsync(package.Manifest.Id),
                    "Concurrent companion suggestion claims must produce one toast and never claim an installed pet.");
            }

            using (var store = new PetManagementStateStore(statePath))
            {
                var state = await store.LoadAsync();
                Ensure(state.IsValid && state.State.SchemaVersion == 1 &&
                       state.State.Pets.ContainsKey(package.Manifest.Id),
                    "Pet state must remain independently schema-versioned and readable.");
            }
            Ensure(!Directory.EnumerateFiles(Path.GetDirectoryName(statePath)!, "*.tmp").Any(),
                "Atomic state writes must clean unique sibling temporary files.");

            var managedManifestPath = Path.Combine(petsRoot, package.Manifest.Id, "pet.json");
            var changedManifest = JsonNode.Parse(await File.ReadAllTextAsync(managedManifestPath))!;
            changedManifest["id"] = "other-pet";
            await File.WriteAllTextAsync(managedManifestPath, changedManifest.ToJsonString(PetJsonOptions));
            using (var changedIdInstaller = new PetInstaller(options))
            {
                Ensure((await changedIdInstaller.InspectAsync(package)).Status ==
                       PetInstallationStatus.UnknownModification,
                    "A syntactically valid managed manifest changed to another ID is an unknown modification.");
                Ensure(await PetThrowsAsync<InvalidOperationException>(() =>
                        changedIdInstaller.InstallAsync(package, PetInstallIntent.RepairConfirmed)),
                    "A changed manifest ID must require explicit replacement rather than ordinary repair.");
                await changedIdInstaller.InstallAsync(package, PetInstallIntent.ReplaceConfirmed);
            }

            await File.WriteAllTextAsync(
                statePath,
                "{\"schemaVersion\":1,\"informationalDisclosureShown\":false," +
                "\"companionSuggestionShownIds\":[],\"pets\":{\"test.flying-pet\":null}}");
            using (var nullStateStore = new PetManagementStateStore(statePath))
            {
                Ensure(!(await nullStateStore.LoadAsync()).IsValid,
                    "A structured null managed installation must return IsValid=false without throwing.");
            }

            const string corruptStateBytes = "{ broken-json";
            await File.WriteAllTextAsync(statePath, corruptStateBytes);
            using var damagedStateInstaller = new PetInstaller(
                new PetInstallerOptions(petsRoot, backupRoot, statePath));
            var damaged = await damagedStateInstaller.InspectAsync(package);
            Ensure(damaged.Status == PetInstallationStatus.Damaged && !damaged.StateIsValid,
                "A corrupt management state must be reported, never silently overwritten.");
            Ensure(await PetThrowsAsync<InvalidDataException>(() =>
                    damagedStateInstaller.InstallAsync(package, PetInstallIntent.RepairConfirmed)),
                "A corrupt global pet state must block an unsafe repair.");
            Ensure(await PetThrowsAsync<InvalidOperationException>(() =>
                    damagedStateInstaller.RecoverManagementStateAsync(confirmed: false)),
                "Management state recovery must require explicit confirmation.");
            var installedHashBeforeRecovery = await PetHashFileAsync(
                Path.Combine(petsRoot, package.Manifest.Id, "spritesheet.webp"));
            var recovery = await damagedStateInstaller.RecoverManagementStateAsync(confirmed: true);
            Ensure(recovery.Changed && recovery.ArchivedStatePath is not null &&
                   await File.ReadAllTextAsync(recovery.ArchivedStatePath) == corruptStateBytes &&
                   await PetHashFileAsync(Path.Combine(petsRoot, package.Manifest.Id, "spritesheet.webp")) ==
                   installedHashBeforeRecovery &&
                   (await damagedStateInstaller.InspectAsync(package)).Status ==
                   PetInstallationStatus.DuplicateIdConflict,
                "Confirmed state recovery must archive exact corrupt bytes, preserve pet files, and expose an unmanaged ID conflict.");

            var compoundPetsRoot = Path.Combine(root, "compound-pets");
            Directory.CreateDirectory(compoundPetsRoot);
            PetCopyInstallFiles(package, Path.Combine(compoundPetsRoot, "duplicate-one"));
            PetCopyInstallFiles(package, Path.Combine(compoundPetsRoot, "duplicate-two"));
            var occupiedTarget = Path.Combine(compoundPetsRoot, package.Manifest.Id);
            Directory.CreateDirectory(occupiedTarget);
            await File.WriteAllTextAsync(
                Path.Combine(occupiedTarget, "pet.json"),
                "{\"id\":\"other-pet\",\"displayName\":\"Other\",\"description\":\"fixture\"," +
                "\"spriteVersionNumber\":2,\"spritesheetPath\":\"other.webp\"}");
            await File.WriteAllTextAsync(Path.Combine(occupiedTarget, "sentinel.txt"), "other pet");
            var compoundOptions = new PetInstallerOptions(
                compoundPetsRoot,
                Path.Combine(root, "compound-backups"),
                Path.Combine(root, "compound-state", "pets.json"));
            using (var compoundInstaller = new PetInstaller(compoundOptions))
            {
                var compound = await compoundInstaller.InspectAsync(package);
                Ensure(compound.Status == PetInstallationStatus.UnknownModification &&
                       compound.InstalledDirectories.Count == 3 &&
                       compound.Detail.Contains("全部受影响目录", StringComparison.Ordinal),
                    "A target occupied by another ID plus duplicate target IDs must disclose every affected directory.");
                var replaced = await compoundInstaller.InstallAsync(package, PetInstallIntent.ReplaceConfirmed);
                Ensure(replaced.BackupId is not null,
                    "Explicit compound replacement must preserve all affected directories in a durable backup.");
            }

            Ensure(PetThrows<ArgumentException>(() =>
                {
                    _ = new PetInstallerOptions(
                        petsRoot,
                        Path.Combine(petsRoot, "backups"),
                        statePath);
                }),
                "Installer paths must reject overlapping pets and backup roots.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static readonly JsonSerializerOptions PetJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static async Task PetMutateCatalogAsync(
        string packageRoot,
        Action<JsonNode> mutation)
    {
        var path = Path.Combine(packageRoot, "catalog.json");
        var catalog = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        mutation(catalog);
        await File.WriteAllTextAsync(path, catalog.ToJsonString(PetJsonOptions));
    }

    private static async Task PetMutateBackupAsync(
        string backupRoot,
        Action<JsonNode> mutation)
    {
        var path = Path.Combine(backupRoot, "backup.json");
        var backup = JsonNode.Parse(await File.ReadAllTextAsync(path)) ??
                     throw new InvalidDataException("Test backup manifest was null.");
        mutation(backup);
        await File.WriteAllTextAsync(path, backup.ToJsonString(PetJsonOptions));
    }

    private static async Task PetRefreshCatalogFileAsync(string packageRoot, string relativePath)
    {
        var catalogPath = Path.Combine(packageRoot, "catalog.json");
        var catalog = JsonNode.Parse(await File.ReadAllTextAsync(catalogPath))!;
        var entry = catalog["files"]!.AsArray().Single(item =>
            string.Equals(item!["path"]!.GetValue<string>(), relativePath, StringComparison.Ordinal));
        var filePath = Path.Combine(packageRoot, relativePath);
        entry!["size"] = new FileInfo(filePath).Length;
        entry["sha256"] = await PetHashFileAsync(filePath);
        await File.WriteAllTextAsync(catalogPath, catalog.ToJsonString(PetJsonOptions));
    }

    private static async Task<string> PetHashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static async Task<bool> PetThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static bool PetThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void PetCopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void PetCopyInstallFiles(PetPackage package, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in package.InstallFiles)
        {
            var target = Path.Combine(
                destination,
                file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(package.ResolvedFiles[file.Path], target, overwrite: false);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);

    private sealed class PetCoreFixture : IDisposable
    {
        private PetCoreFixture(string root, string packageRoot)
        {
            Root = root;
            PackageRoot = packageRoot;
        }

        public string Root { get; }

        public string PackageRoot { get; }

        public static async Task<PetCoreFixture> CreateAsync(
            string productVersion = "1.0.0",
            byte payloadMarker = 0)
        {
            var root = Path.Combine(Path.GetTempPath(), $"tessalume-pet-package-{Guid.NewGuid():N}");
            var packageRoot = Path.Combine(root, "source-package");
            Directory.CreateDirectory(Path.Combine(packageRoot, "previews"));
            var manifest = new
            {
                id = "test.flying-pet",
                displayName = "Test Flying Pet",
                description = "Temporary test-only pet package.",
                spriteVersionNumber = 2,
                spritesheetPath = "spritesheet.webp",
            };
            await File.WriteAllTextAsync(
                Path.Combine(packageRoot, "pet.json"),
                JsonSerializer.Serialize(manifest, PetJsonOptions));
            await File.WriteAllBytesAsync(
                Path.Combine(packageRoot, "spritesheet.webp"),
                CreateWebP(payloadMarker));
            await File.WriteAllBytesAsync(
                Path.Combine(packageRoot, "previews", "idle.png"),
                [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

            var files = new[]
            {
                await CatalogFileAsync(packageRoot, "pet.json", PetPackageContract.ManifestRole),
                await CatalogFileAsync(packageRoot, "spritesheet.webp", PetPackageContract.SpritesheetRole),
                await CatalogFileAsync(packageRoot, "previews/idle.png", PetPackageContract.PreviewRole),
            };
            var catalog = new
            {
                schemaVersion = 1,
                id = "test.flying-pet",
                displayName = "Test Flying Pet",
                description = "Temporary test-only pet package.",
                productVersion,
                protocol = new
                {
                    spriteVersionNumber = 2,
                    atlasWidth = 1536,
                    atlasHeight = 2288,
                    columns = 8,
                    rows = 11,
                    cellWidth = 192,
                    cellHeight = 208,
                    usedFrameCount = 74,
                    states = PetPackageContract.RequiredStates.Select(state => new
                    {
                        key = state.Key,
                        row = state.Row,
                        frames = state.Frames,
                    }),
                },
                author = new { name = "Tests" },
                license = new
                {
                    kind = "test-only",
                    spdx = "LicenseRef-Test-Only",
                    name = "Test fixture",
                },
                rights = new { kind = "test-fixture", notice = "Generated only in a temporary test directory." },
                files,
                previews = new[]
                {
                    new { path = "previews/idle.png", kind = "primary", label = "Idle", stateKey = "idle" },
                },
                recommendedThemeIds = new[] { "test.companion-theme" },
            };
            await File.WriteAllTextAsync(
                Path.Combine(packageRoot, "catalog.json"),
                JsonSerializer.Serialize(catalog, PetJsonOptions));
            return new PetCoreFixture(root, packageRoot);
        }

        public string CopyPackage(string name)
        {
            var destination = Path.Combine(Root, name);
            PetCopyDirectory(PackageRoot, destination);
            return destination;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static async Task<object> CatalogFileAsync(string root, string path, string role)
        {
            var fullPath = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            return new
            {
                path,
                sha256 = await PetHashFileAsync(fullPath),
                size = new FileInfo(fullPath).Length,
                role,
            };
        }

        private static byte[] CreateWebP(byte payloadMarker)
        {
            var bytes = new byte[26];
            "RIFF"u8.CopyTo(bytes);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 18);
            "WEBP"u8.CopyTo(bytes.AsSpan(8));
            "VP8L"u8.CopyTo(bytes.AsSpan(12));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 5);
            bytes[20] = 0x2f;
            bytes[21] = 0xff;
            bytes[22] = 0xc5;
            bytes[23] = 0x3b;
            bytes[24] = 0x12;
            bytes[25] = payloadMarker;
            return bytes;
        }
    }
}

internal sealed class ThrowBeforePromotePetObserver : IPetTransactionObserver
{
    public void OnPhase(PetTransactionPhase phase)
    {
        if (phase == PetTransactionPhase.BeforePromote)
        {
            throw new PetInjectedTransactionException();
        }
    }
}

internal sealed class ThrowAtPetPhasesObserver : IPetTransactionObserver
{
    private readonly HashSet<PetTransactionPhase> _phases;

    public ThrowAtPetPhasesObserver(params PetTransactionPhase[] phases)
    {
        _phases = new HashSet<PetTransactionPhase>(phases);
    }

    public void OnPhase(PetTransactionPhase phase)
    {
        if (_phases.Contains(phase))
        {
            throw new PetInjectedTransactionException();
        }
    }
}

internal sealed class PetPhaseActionObserver : IPetTransactionObserver
{
    private readonly PetTransactionPhase _phase;
    private readonly Action _action;
    private bool _invoked;

    public PetPhaseActionObserver(PetTransactionPhase phase, Action action)
    {
        _phase = phase;
        _action = action;
    }

    public void OnPhase(PetTransactionPhase phase)
    {
        if (!_invoked && phase == _phase)
        {
            _invoked = true;
            _action();
        }
    }
}

internal sealed class PetInjectedTransactionException : Exception;
