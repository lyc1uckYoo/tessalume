internal static partial class TestSuite
{
    public static async Task<int> RunAsync(string[] args)
    {

        if (args is ["--probe", var portText] && int.TryParse(portText, out var probePort))
        {
            return await ProbeRuntimeAsync(probePort);
        }

        if (args is ["--composer-probe", var composerPortText] &&
            int.TryParse(composerPortText, out var composerPort))
        {
            return await ProbeComposerAsync(composerPort);
        }

        if (args is ["--composer-alias-probe", var aliasPortText] &&
            int.TryParse(aliasPortText, out var aliasPort))
        {
            return await ProbeComposerAsync(aliasPort, applyAlias: true);
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

        if (args is ["--apply-package-defaults", var defaultsPortText, var defaultsPackagePath] &&
            int.TryParse(defaultsPortText, out var defaultsPort))
        {
            return await ApplyPackageDefaultsRuntimeAsync(defaultsPort, defaultsPackagePath);
        }

        if (args is [
                "--theme-switch-continuity",
                var continuityPortText,
                var continuityFromPath,
                var continuityToPath] &&
            int.TryParse(continuityPortText, out var continuityPort))
        {
            return await ProbeThemeSwitchContinuityAsync(
                continuityPort,
                continuityFromPath,
                continuityToPath);
        }

        if (args is ["--check-live-update", var currentVersionText] &&
            Version.TryParse(currentVersionText, out var currentVersion))
        {
            return await CheckLiveUpdateAsync(currentVersion);
        }

        if (args is [
                "--visual-controls-probe",
                var visualPortText,
                var visualPackagePath,
                var visualDataDirectory] &&
            int.TryParse(visualPortText, out var visualPort))
        {
            return await ProbeVisualControlsAsync(
                visualPort,
                visualPackagePath,
                visualDataDirectory);
        }

        if (args is [
                "--display-preferences-probe",
                var displayPortText,
                var displayPackagePath,
                var displayDataDirectory] &&
            int.TryParse(displayPortText, out var displayPort))
        {
            return await ProbeDisplayPreferencesAsync(
                displayPort,
                displayPackagePath,
                displayDataDirectory);
        }

        if (args is [
                "--creator-snapshots",
                var completeWorkspacePath,
                var completeLightSnapshotPath,
                var completePromptSnapshotPath,
                var completeDetailSnapshotPath,
                var completeDarkSnapshotPath,
                var completeAcceptanceSnapshotPath,
                var completeReleaseSnapshotPath])
        {
            return await RenderCreatorCenterSnapshotsAsync(
                completeWorkspacePath,
                completeLightSnapshotPath,
                completeDetailSnapshotPath,
                completeDarkSnapshotPath,
                completePromptSnapshotPath,
                completeReleaseSnapshotPath,
                completeAcceptanceSnapshotPath);
        }

        if (args is [
                "--creator-snapshots",
                var routedWorkspacePath,
                var routedLightSnapshotPath,
                var routedPromptSnapshotPath,
                var routedDetailSnapshotPath,
                var routedDarkSnapshotPath,
                var routedReleaseSnapshotPath])
        {
            return await RenderCreatorCenterSnapshotsAsync(
                routedWorkspacePath,
                routedLightSnapshotPath,
                routedDetailSnapshotPath,
                routedDarkSnapshotPath,
                routedPromptSnapshotPath,
                routedReleaseSnapshotPath);
        }

        if (args is [
                "--creator-snapshots",
                var expandedWorkspacePath,
                var expandedLightSnapshotPath,
                var promptSnapshotPath,
                var expandedDetailSnapshotPath,
                var expandedDarkSnapshotPath])
        {
            return await RenderCreatorCenterSnapshotsAsync(
                expandedWorkspacePath,
                expandedLightSnapshotPath,
                expandedDetailSnapshotPath,
                expandedDarkSnapshotPath,
                promptSnapshotPath);
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
                var aboutExpandedSnapshotPath,
                var diagnosticsExpandedSnapshotPath,
                var diagnosticsDarkExpandedSnapshotPath,
                var updateBadgeExpandedSnapshotPath,
                var aboutDarkSnapshotPath])
        {
            return await RenderStageDSnapshotsAsync(
                aboutExpandedSnapshotPath,
                diagnosticsExpandedSnapshotPath,
                diagnosticsDarkExpandedSnapshotPath,
                updateBadgeExpandedSnapshotPath,
                aboutDarkSnapshotPath);
        }

        if (args is [
                "--stage-d-snapshots",
                var aboutWithBadgeSnapshotPath,
                var diagnosticsWithBadgeSnapshotPath,
                var diagnosticsDarkWithBadgeSnapshotPath,
                var updateBadgeSnapshotPath])
        {
            return await RenderStageDSnapshotsAsync(
                aboutWithBadgeSnapshotPath,
                diagnosticsWithBadgeSnapshotPath,
                diagnosticsDarkWithBadgeSnapshotPath,
                updateBadgeSnapshotPath);
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

        if (args is [
                "--personalization-snapshots",
                var compactLightSnapshotPath,
                var compactDarkSnapshotPath,
                var personalizationCompactSnapshotPath])
        {
            return await RenderPersonalizationSnapshotsAsync(
                compactLightSnapshotPath,
                compactDarkSnapshotPath,
                personalizationCompactSnapshotPath);
        }

        if (args is [
                "--personalization-snapshots",
                var personalizationLightSnapshotPath,
                var personalizationDarkSnapshotPath])
        {
            return await RenderPersonalizationSnapshotsAsync(
                personalizationLightSnapshotPath,
                personalizationDarkSnapshotPath);
        }

        if (args is [
                "--artwork-snapshots",
                var heroLightArtworkSnapshotPath,
                var sidebarLightArtworkSnapshotPath,
                var sidebarDarkArtworkSnapshotPath,
                var chatDarkArtworkSnapshotPath])
        {
            return await RenderArtworkSnapshotsAsync(
                heroLightArtworkSnapshotPath,
                sidebarLightArtworkSnapshotPath,
                chatDarkArtworkSnapshotPath,
                sidebarDarkArtworkSnapshotPath);
        }

        if (args is [
                "--artwork-snapshots",
                var basicArtworkSnapshotPath,
                var compositionArtworkSnapshotPath,
                var effectsArtworkSnapshotPath])
        {
            return await RenderArtworkSnapshotsAsync(
                basicArtworkSnapshotPath,
                compositionArtworkSnapshotPath,
                effectsArtworkSnapshotPath);
        }

        if (args is [
                "--theme-library-snapshots",
                var libraryLightSnapshotPath,
                var libraryDarkSnapshotPath,
                var detailLightSnapshotPath,
                var detailDarkSnapshotPath])
        {
            return await RenderThemeLibrarySnapshotsAsync(
                libraryLightSnapshotPath,
                detailLightSnapshotPath,
                detailDarkSnapshotPath,
                libraryDarkSnapshotPath);
        }

        if (args is [
                "--theme-library-snapshots",
                var librarySnapshotPath,
                var themeDetailSnapshotPath,
                var themeDetailDarkSnapshotPath])
        {
            return await RenderThemeLibrarySnapshotsAsync(
                librarySnapshotPath,
                themeDetailSnapshotPath,
                themeDetailDarkSnapshotPath);
        }

        if (args is [
                "--shell-surface-snapshots",
                var dialogLightPath,
                var dialogDarkPath,
                var onboardingLightPath,
                var onboardingDarkPath,
                var quickLightPath,
                var quickDarkPath])
        {
            return await RenderShellSurfaceSnapshotsAsync(
                dialogLightPath,
                dialogDarkPath,
                onboardingLightPath,
                onboardingDarkPath,
                quickLightPath,
                quickDarkPath);
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
            ("runtime local image data URL cache is bounded and invalidates", RuntimeImageDataUrlCacheIsBoundedAndInvalidatesAsync),
            ("runtime artwork images use fingerprint deltas", RuntimeArtworkImagesUseFingerprintDeltasAsync),
            ("runtime disposes compatible predecessor injection", RuntimeDisposesCompatiblePredecessorInjectionAsync),
            ("runtime preflights assets before replacing the active theme", RuntimePreflightsAssetsBeforeReplacementAsync),
            ("runtime failures are classified and partial pages roll back", RuntimeFailuresAreClassifiedAndRolledBackAsync),
            ("restore removes predecessor runtime brands", RestoreRemovesPredecessorRuntimeBrandsAsync),
            ("runtime diagnostics use Tessalume markers", RuntimeDiagnosticsUseTessalumeMarkersAsync),
            ("skipped pet overlays retain the processed marker", SkippedPetOverlaysRetainProcessedMarkerAsync),
            ("runtime removes native composer fade", RuntimeRemovesNativeComposerFadeAsync),
            ("runtime preserves wide assistant content", RuntimePreservesWideAssistantContentAsync),
            ("display preferences change effective runtime styles", DisplayPreferencesChangeEffectiveRuntimeStylesAsync),
            ("runtime decorates task surfaces before deferred repair", RuntimeDecoratesTaskSurfacesBeforeDeferredRepairAsync),
            ("published themes use canonical injection contract", PublishedThemesUseCanonicalInjectionContractAsync),
            ("flagship template v1 freezes shared structure", FlagshipTemplateV1FreezesSharedStructureAsync),
            ("artwork adjustments are runtime-owned", ArtworkAdjustmentsAreRuntimeOwnedAsync),
            ("artwork workbench supports precise input and image-source actions", ArtworkWorkbenchSupportsPreciseInputAndSourceActionsAsync),
            ("artwork workbench history and display settings work", ArtworkWorkbenchHistoryAndDisplaySettingsWorkAsync),
            ("artwork workbench keeps six targets isolated", ArtworkWorkbenchKeepsSixTargetsIsolatedAsync),
            ("artwork workbench local reset scopes are strict", ArtworkWorkbenchLocalResetScopesAreStrictAsync),
            ("artwork workbench history coalesces and stays bounded", ArtworkWorkbenchHistoryCoalescesAndStaysBoundedAsync),
            ("artwork workbench canvas mapping and offline session work", ArtworkWorkbenchCanvasMappingAndOfflineSessionWorkAsync),
            ("artwork workbench preview infrastructure caches and resolves", ArtworkWorkbenchPreviewInfrastructureCachesAndResolvesAsync),
            ("artwork workbench WPF view loads and adapts", ArtworkWorkbenchViewLoadsAndAdaptsAsync),
            ("artwork defaults project published final placements", ArtworkThemeDefaultsProjectPublishedPlacementsAsync),
            ("artwork defaults mirror all published themes", ArtworkThemeDefaultsMatchPublishedThemesAsync),
            ("artwork absolute composition and sparse schema migration work", ArtworkAbsoluteCompositionAndSparseSchemaMigrationWorkAsync),
            ("artwork undo preserves external display preferences", ArtworkWorkbenchUndoPreservesExternalDisplayAsync),
            ("artwork studio route stays reachable across layouts", ArtworkStudioRouteLayoutsStayReachableAsync),
            ("personal images are stored and resolved safely", PersonalImagesAreStoredSafelyAsync),
            ("theme library state is normalized and version aware", ThemeLibraryStateIsNormalizedAndVersionAwareAsync),
            ("theme library details and recent sorting work", ThemeLibraryDetailsAndRecentSortingWorkAsync),
            ("cold-start settings are immediately interactive", ColdStartSettingsAreImmediatelyInteractiveAsync),
            ("main product surfaces share the design system", MainProductSurfacesShareDesignSystemAsync),
            ("navigation routes keep dense workflows separated", NavigationRoutesKeepDenseWorkflowsSeparatedAsync),
            ("source layout keeps product feature boundaries", SourceLayoutKeepsFeatureBoundariesAsync),
            ("WPF shell loads split resources", WpfShellLoadsSplitResourcesAsync),
            ("long product dialogs keep a fixed header and scrollable body", LongProductDialogUsesScrollableBodyAsync),
            ("adaptive layout and keyboard accessibility are available", AdaptiveLayoutAndKeyboardAccessibilityAsync),
            ("version 2.0 product foundation is connected", Version20ProductFoundationIsConnectedAsync),
            ("portable Codex creator workspace is self-contained", PortableCreatorWorkspaceIsSelfContainedAsync),
            ("creator prompt composer builds a durable contract prompt", CreatorPromptComposerBuildsDurableContractPromptAsync),
            ("creator repair prompt is scoped to project health", CreatorRepairPromptUsesOnlyBoundedProjectHealthAsync),
            ("creator workflow builds a five-stage release gate", CreatorWorkflowEvaluatorBuildsFiveStageReleaseGateAsync),
            ("creator guidance provides one contextual next action", CreatorGuidanceProvidesOneContextualNextActionAsync),
            ("creator workspace history is normalized and bounded", CreatorWorkspaceHistoryIsNormalizedAsync),
            ("creator workspace contract upgrade preserves projects", CreatorWorkspaceContractUpgradePreservesProjectsAsync),
            ("creator center orchestrates workspace projects", CreatorCenterOrchestratesWorkspaceProjectsAsync),
            ("creator watcher debounces stable changes and releases", CreatorWatcherDebouncesStableChangesAndReleasesAsync),
            ("creator center auto-applies only healthy stable projects", CreatorCenterAutoAppliesOnlyHealthyStableProjectsAsync),
            ("creator runtime acceptance classifies issues and gates release", CreatorRuntimeAcceptanceClassifiesIssuesAndGatesReleaseAsync),
            ("creator project scanner produces structured health", CreatorProjectScannerProducesStructuredHealthAsync),
            ("theme archive export is deterministic and round-trips", ThemeArchiveExportIsDeterministicAndRoundTripsAsync),
            ("portable backup round-trips user data and imported themes", PortableBackupRoundTripsUserDataAndImportedThemesAsync),
            ("portable backup rejects corruption, cancellation, and rolls back", PortableBackupRejectsCorruptionCancellationAndRollsBackAsync),
            ("compatibility health state survives restart", CompatibilityHealthStateIsDurableAsync),
            ("compatibility packs validate, install, and roll back atomically", CompatibilityPacksInstallValidateAndRollBackAsync),
            ("version 2.0 isolated creator-to-recovery flow completes", Version20IsolatedCreatorToRecoveryFlowAsync),
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
            ("compatibility updater discovers dedicated verified packs", CompatibilityUpdaterFindsDedicatedVerifiedPacksAsync),
            ("compatibility updater paginates and ignores prereleases", CompatibilityUpdaterPaginatesAndIgnoresPrereleasesAsync),
            ("portable updater replaces and preserves a rollback backup", PortableUpdaterReplacesAndBacksUpAsync),
            ("portable updater rolls back without touching user data", PortableUpdaterRollsBackWithoutTouchingUserDataAsync),
            ("version rollback snapshots restore compatible settings atomically", UpdateDataSnapshotsRestoreVersionedSettingsAtomicallyAsync),
            ("update helper preserves schemas across successful and failed rollback", UpdateHelperPreservesSchemasAcrossRollbackAsync),
            ("update rollback state rejects a tampered previous executable", UpdateRollbackStateRequiresAnUntamperedBackupAsync),
            ("legacy update results create a pre-migration rollback point", LegacyUpdateResultCreatesAPreMigrationRollbackPointAsync),
            ("updated application writes a startup health marker", UpdatedApplicationWritesAStartupHealthMarkerAsync),
            ("automatic update workflow is connected to the product UI", AutomaticUpdateWorkflowIsConnectedAsync),
            ("first-run onboarding never applies a random theme", FirstRunOnboardingNeverAppliesRandomThemeAsync),
            ("build script launches the published executable by default", BuildScriptLaunchesPublishedExecutableAsync),
            ("release artifacts and feedback paths are documented", ReleaseReadinessAssetsAreDocumentedAsync),
            ("GitHub automation separates application and compatibility releases", GitHubAutomationSeparatesApplicationAndCompatibilityReleasesAsync),
            ("compatibility release packages are reproducible", CompatibilityPackBuildIsDeterministicAsync),
            ("UI preferences migrate through schema five", UiPreferencesMigrateFromUnversionedSchemaAsync),
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
                Console.Error.WriteLine($"FAIL  {name}: {exception}");
            }
        }

        Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} checks passed.");
        return failures.Count == 0 ? 0 : 1;

    }
}
