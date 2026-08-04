internal static partial class TestSuite
{
    public static async Task<int> RunAsync(string[] args)
    {

        if (args is ["--probe", var portText] && int.TryParse(portText, out var probePort))
        {
            return await ProbeRuntimeAsync(probePort);
        }

        if (args is ["--remove", var removePortText] && int.TryParse(removePortText, out var removePort))
        {
            return await RemoveRuntimeAsync(removePort);
        }

        if (args is ["--apply", var applyPortText] && int.TryParse(applyPortText, out var applyPort))
        {
            return await ApplyRuntimeAsync(applyPort);
        }

        if (args is ["--theme-modes", var modePortText] && int.TryParse(modePortText, out var modePort))
        {
            return await ProbeThemeModesAsync(modePort);
        }

        if (args is ["--toggle-color-scheme", var togglePortText] && int.TryParse(togglePortText, out var togglePort))
        {
            return await ToggleColorSchemeAsync(togglePort);
        }

        if (args is ["--appearance-state", var appearancePortText] && int.TryParse(appearancePortText, out var appearancePort))
        {
            return await ProbeAppearanceStateAsync(appearancePort);
        }

        if (args is ["--appearance-bundles", var bundlePortText] && int.TryParse(bundlePortText, out var bundlePort))
        {
            return await ProbeAppearanceBundlesAsync(bundlePort);
        }

        if (args is ["--query-clients", var queryPortText] && int.TryParse(queryPortText, out var queryPort))
        {
            return await ProbeQueryClientsAsync(queryPort);
        }

        if (args is ["--apply-package", var packagePortText, var packagePath] &&
            int.TryParse(packagePortText, out var packagePort))
        {
            return await ApplyPackageRuntimeAsync(packagePort, packagePath);
        }

        if (args is ["--check-live-update", var currentVersionText] &&
            Version.TryParse(currentVersionText, out var currentVersion))
        {
            return await CheckLiveUpdateAsync(currentVersion);
        }

        if (args is [
                "--creator-snapshots",
                var workspacePath,
                var lightSnapshotPath,
                var detailSnapshotPath,
                var darkSnapshotPath])
        {
            return await RenderCreatorCenterSnapshotsAsync(
                workspacePath,
                lightSnapshotPath,
                detailSnapshotPath,
                darkSnapshotPath);
        }

        if (args is [
                "--stage-d-snapshots",
                var aboutSnapshotPath,
                var diagnosticsSnapshotPath,
                var diagnosticsDarkSnapshotPath])
        {
            return await RenderStageDSnapshotsAsync(
                aboutSnapshotPath,
                diagnosticsSnapshotPath,
                diagnosticsDarkSnapshotPath);
        }

        var tests = new (string Name, Func<Task> Run)[]
        {
            ("valid package loads", ValidPackageLoadsAsync),
            ("path traversal is rejected", PathTraversalIsRejectedAsync),
            ("remote CSS is rejected", RemoteCssIsRejectedAsync),
            ("null manifest sections produce validation", NullManifestSectionsProduceValidationAsync),
            ("catalog keeps invalid packages visible", CatalogIncludesInvalidPackagesAsync),
            ("representative open theme loads", RepresentativeOpenThemeLoadsAsync),
            ("published theme library loads and builds", PublishedThemeLibraryLoadsAndBuildsAsync),
            ("theme assets use disposable blob URLs", ThemeAssetsUseBlobUrlsAsync),
            ("runtime payload stages large assets separately", RuntimePayloadStagesLargeAssetsSeparatelyAsync),
            ("runtime disposes compatible predecessor injection", RuntimeDisposesCompatiblePredecessorInjectionAsync),
            ("runtime preflights assets before replacing the active theme", RuntimePreflightsAssetsBeforeReplacementAsync),
            ("runtime failures are classified and partial pages roll back", RuntimeFailuresAreClassifiedAndRolledBackAsync),
            ("restore removes predecessor runtime brands", RestoreRemovesPredecessorRuntimeBrandsAsync),
            ("runtime diagnostics use Tessalume markers", RuntimeDiagnosticsUseTessalumeMarkersAsync),
            ("skipped pet overlays retain the processed marker", SkippedPetOverlaysRetainProcessedMarkerAsync),
            ("runtime removes native composer fade", RuntimeRemovesNativeComposerFadeAsync),
            ("runtime decorates task surfaces before deferred repair", RuntimeDecoratesTaskSurfacesBeforeDeferredRepairAsync),
            ("published themes use canonical injection contract", PublishedThemesUseCanonicalInjectionContractAsync),
            ("flagship template v1 freezes shared structure", FlagshipTemplateV1FreezesSharedStructureAsync),
            ("artwork adjustments are runtime-owned", ArtworkAdjustmentsAreRuntimeOwnedAsync),
            ("main product surfaces share the design system", MainProductSurfacesShareDesignSystemAsync),
            ("source layout keeps product feature boundaries", SourceLayoutKeepsFeatureBoundariesAsync),
            ("WPF shell loads split resources", WpfShellLoadsSplitResourcesAsync),
            ("long product dialogs keep a fixed header and scrollable body", LongProductDialogUsesScrollableBodyAsync),
            ("adaptive layout and keyboard accessibility are available", AdaptiveLayoutAndKeyboardAccessibilityAsync),
            ("version 1.3.0 product workflow is complete", Version13ProductWorkflowIsCompleteAsync),
            ("portable Codex creator workspace is self-contained", PortableCreatorWorkspaceIsSelfContainedAsync),
            ("creator workspace history is normalized and bounded", CreatorWorkspaceHistoryIsNormalizedAsync),
            ("creator center orchestrates workspace projects", CreatorCenterOrchestratesWorkspaceProjectsAsync),
            ("creator watcher debounces stable changes and releases", CreatorWatcherDebouncesStableChangesAndReleasesAsync),
            ("creator center auto-applies only healthy stable projects", CreatorCenterAutoAppliesOnlyHealthyStableProjectsAsync),
            ("creator project scanner produces structured health", CreatorProjectScannerProducesStructuredHealthAsync),
            ("theme archive export is deterministic and round-trips", ThemeArchiveExportIsDeterministicAndRoundTripsAsync),
            ("portable backup round-trips user data and imported themes", PortableBackupRoundTripsUserDataAndImportedThemesAsync),
            ("portable backup rejects corruption, cancellation, and rolls back", PortableBackupRejectsCorruptionCancellationAndRollsBackAsync),
            ("compatibility health state survives restart", CompatibilityHealthStateIsDurableAsync),
            ("version 1.3 isolated creator-to-recovery flow completes", Version13IsolatedCreatorToRecoveryFlowAsync),
            ("local diagnostics and built-in recovery are available", DiagnosticsRecoveryIsAvailableAsync),
            ("local importer copies a validated package", LocalImporterCopiesPackageAsync),
            ("ZIP theme import is bounded and rejects traversal", ZipThemeImportIsBoundedAsync),
            ("bundled adapter builds a complete payload", BundledAdapterBuildsPayloadAsync),
            ("open advanced template loads with a stable revision hash", OpenAdvancedTemplateLoadsWithStableRevisionHashAsync),
            ("advanced import keeps script and revision hash tracks changes", AdvancedImportKeepsScriptAndTracksChangesAsync),
            ("deferred main UI replays the live engine state", DeferredMainUiReplaysEngineStateAsync),
            ("main window disposal is idempotent", MainWindowDisposalIsIdempotentAsync),
            ("startup stays opt-in and cleans the predecessor brand", StartupRegistrationStaysOptInAsync),
            ("release updater checks downloads and verifies SHA-256", ReleaseUpdaterChecksAndDownloadsAsync),
            ("portable updater replaces and preserves a rollback backup", PortableUpdaterReplacesAndBacksUpAsync),
            ("automatic update workflow is connected to the product UI", AutomaticUpdateWorkflowIsConnectedAsync),
            ("first-run onboarding never applies a random theme", FirstRunOnboardingNeverAppliesRandomThemeAsync),
            ("build script launches the published executable by default", BuildScriptLaunchesPublishedExecutableAsync),
            ("release artifacts and feedback paths are documented", ReleaseReadinessAssetsAreDocumentedAsync),
            ("UI preferences migrate from the unversioned schema", UiPreferencesMigrateFromUnversionedSchemaAsync),
        };

        var failures = new List<string>();
        foreach (var (name, run) in tests)
        {
            try
            {
                await run();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures.Add(name);
                Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} checks passed.");
        return failures.Count == 0 ? 0 : 1;

    }
}
