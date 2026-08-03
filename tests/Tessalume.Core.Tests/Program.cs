using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tessalume.Core.Themes;
using Tessalume.Core.Runtime;
using Tessalume.Core.Updates;

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

var tests = new (string Name, Func<Task> Run)[]
{
    ("valid package loads", ValidPackageLoadsAsync),
    ("path traversal is rejected", PathTraversalIsRejectedAsync),
    ("remote CSS is rejected", RemoteCssIsRejectedAsync),
    ("catalog keeps invalid packages visible", CatalogIncludesInvalidPackagesAsync),
    ("representative open theme loads", RepresentativeOpenThemeLoadsAsync),
    ("published theme library loads and builds", PublishedThemeLibraryLoadsAndBuildsAsync),
    ("theme assets use disposable blob URLs", ThemeAssetsUseBlobUrlsAsync),
    ("runtime payload stages large assets separately", RuntimePayloadStagesLargeAssetsSeparatelyAsync),
    ("runtime disposes compatible predecessor injection", RuntimeDisposesCompatiblePredecessorInjectionAsync),
    ("runtime preflights assets before replacing the active theme", RuntimePreflightsAssetsBeforeReplacementAsync),
    ("restore removes predecessor runtime brands", RestoreRemovesPredecessorRuntimeBrandsAsync),
    ("runtime diagnostics use Tessalume markers", RuntimeDiagnosticsUseTessalumeMarkersAsync),
    ("skipped pet overlays retain the processed marker", SkippedPetOverlaysRetainProcessedMarkerAsync),
    ("runtime removes native composer fade", RuntimeRemovesNativeComposerFadeAsync),
    ("runtime decorates task surfaces before deferred repair", RuntimeDecoratesTaskSurfacesBeforeDeferredRepairAsync),
    ("published themes use canonical injection contract", PublishedThemesUseCanonicalInjectionContractAsync),
    ("flagship template v1 freezes shared structure", FlagshipTemplateV1FreezesSharedStructureAsync),
    ("artwork adjustments are runtime-owned", ArtworkAdjustmentsAreRuntimeOwnedAsync),
    ("main product surfaces share the design system", MainProductSurfacesShareDesignSystemAsync),
    ("adaptive layout and keyboard accessibility are available", AdaptiveLayoutAndKeyboardAccessibilityAsync),
    ("version 1.2 product workflow is complete", Version12ProductWorkflowIsCompleteAsync),
    ("portable Codex creator workspace is self-contained", PortableCreatorWorkspaceIsSelfContainedAsync),
    ("local diagnostics and built-in recovery are available", DiagnosticsRecoveryIsAvailableAsync),
    ("local importer copies a validated package", LocalImporterCopiesPackageAsync),
    ("ZIP theme import is bounded and rejects traversal", ZipThemeImportIsBoundedAsync),
    ("bundled adapter builds a complete payload", BundledAdapterBuildsPayloadAsync),
    ("open advanced template loads with a stable revision hash", OpenAdvancedTemplateLoadsWithStableRevisionHashAsync),
    ("advanced import keeps script and revision hash tracks changes", AdvancedImportKeepsScriptAndTracksChangesAsync),
    ("deferred main UI replays the live engine state", DeferredMainUiReplaysEngineStateAsync),
    ("startup stays opt-in and cleans the predecessor brand", StartupRegistrationStaysOptInAsync),
    ("release updater checks downloads and verifies SHA-256", ReleaseUpdaterChecksAndDownloadsAsync),
    ("portable updater replaces and preserves a rollback backup", PortableUpdaterReplacesAndBacksUpAsync),
    ("automatic update workflow is connected to the product UI", AutomaticUpdateWorkflowIsConnectedAsync),
    ("first-run onboarding never applies a random theme", FirstRunOnboardingNeverAppliesRandomThemeAsync),
    ("build script launches the published executable by default", BuildScriptLaunchesPublishedExecutableAsync),
    ("release artifacts and feedback paths are documented", ReleaseReadinessAssetsAreDocumentedAsync),
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

static async Task ValidPackageLoadsAsync()
{
    using var fixture = await ThemeFixture.CreateAsync();
    var result = await new ThemePackageLoader().LoadAsync(fixture.Root);
    Ensure(result.Validation.IsValid, FormatIssues(result.Validation));
    var package = result.Package ?? throw new InvalidOperationException("Expected the sample theme to load.");
    Ensure(package.Manifest.Id == "sample.theme", "Expected the sample theme to load.");
    Ensure(package.AssetPaths.ContainsKey("hero"), "Expected hero asset mapping.");
}

static async Task PathTraversalIsRejectedAsync()
{
    using var fixture = await ThemeFixture.CreateAsync(cssPath: "../outside.css");
    var outsidePath = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "outside.css");
    try
    {
        await File.WriteAllTextAsync(outsidePath, "body {}");
        var result = await new ThemePackageLoader().LoadAsync(fixture.Root);
        Ensure(!result.Validation.IsValid, "Traversal package must be invalid.");
        Ensure(result.Validation.Issues.Any(issue => issue.Code == "path.outside-package"), "Traversal issue was not reported.");
    }
    finally
    {
        File.Delete(outsidePath);
    }
}

static async Task RemoteCssIsRejectedAsync()
{
    using var fixture = await ThemeFixture.CreateAsync(css: "@import url('https://example.com/theme.css');");
    var result = await new ThemePackageLoader().LoadAsync(fixture.Root);
    Ensure(!result.Validation.IsValid, "Remote CSS package must be invalid.");
    Ensure(result.Validation.Issues.Any(issue => issue.Code == "css.import.forbidden"), "Remote import issue was not reported.");
}

static async Task CatalogIncludesInvalidPackagesAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"tessalume-catalog-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        using var valid = await ThemeFixture.CreateAsync(Path.Combine(root, "valid"));
        Directory.CreateDirectory(Path.Combine(root, "broken"));
        var catalog = await new ThemeCatalog(new ThemePackageLoader()).ScanAsync(root);
        Ensure(catalog.Count == 2, "Catalog should report both valid and invalid directories.");
        Ensure(catalog.Count(item => item.Validation.IsValid) == 1, "Expected one valid package.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task RepresentativeOpenThemeLoadsAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);
    Ensure(package.IsAdvanced, "The representative theme must use the open advanced lifecycle.");
}

static async Task PublishedThemeLibraryLoadsAndBuildsAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var themesRoot = Path.Combine(repositoryRoot, "themes");
    if (!Directory.Exists(themesRoot))
    {
        return;
    }

    var discoveredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var catalog = await new ThemeCatalog(new ThemePackageLoader()).ScanAsync(themesRoot);
    foreach (var item in catalog)
    {
        Ensure(item.Validation.IsValid, $"{Path.GetFileName(item.Directory)}: {FormatIssues(item.Validation)}");
        var package = item.Package
            ?? throw new InvalidOperationException($"{Path.GetFileName(item.Directory)} did not load.");
        Ensure(discoveredIds.Add(package.Manifest.Id), $"Duplicate bundled theme id: {package.Manifest.Id}");
        var payload = await BuildPayloadAsync(repositoryRoot, package);
        Ensure(payload.Contains(package.Manifest.Id, StringComparison.Ordinal),
            $"Payload is missing {package.Manifest.Id} metadata.");
    }
}

static async Task ThemeAssetsUseBlobUrlsAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);

    var payload = await BuildPayloadAsync(repositoryRoot, package);
    Ensure(payload.Contains("URL.createObjectURL", StringComparison.Ordinal),
        "Large theme assets must be converted to short blob URLs before entering CSS values.");
    Ensure(payload.Contains("URL.revokeObjectURL", StringComparison.Ordinal),
        "Theme blob URLs must be released when the theme is removed.");
    Ensure(!payload.Contains("setProperty(variable, `url(\"${dataUrl}\")`)", StringComparison.Ordinal),
        "Raw data URLs must not be assigned directly to CSS custom properties.");
}

static async Task RuntimePayloadStagesLargeAssetsSeparatelyAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);
    var builder = new ThemePayloadBuilder(new Dictionary<string, string>
    {
        [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            "theme-runtime-v2.js"),
    });
    var payload = await builder.BuildRuntimeAsync(package);
    Ensure(payload.Contains("__TESSALUME_STAGED_ASSETS__", StringComparison.Ordinal),
        "Runtime payload must consume separately staged assets.");
    Ensure(payload.Length < 512 * 1024,
        $"Runtime payload unexpectedly embeds large assets ({payload.Length} characters).");
}

static async Task SkippedPetOverlaysRetainProcessedMarkerAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var runtimePath = Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        "theme-runtime-v2.js");
    var runtime = await File.ReadAllTextAsync(runtimePath);
    var skippedBranchStart = runtime.IndexOf(
        "if (isPetOverlay && !allowPetOverlay)",
        StringComparison.Ordinal);
    Ensure(skippedBranchStart >= 0, "The pet-overlay skip branch is missing.");
    var skippedBranch = runtime.Substring(
        skippedBranchStart,
        Math.Min(600, runtime.Length - skippedBranchStart));
    Ensure(
        skippedBranch.Contains("window.__TESSALUME_THEME_ID__ = themeId", StringComparison.Ordinal),
        "Skipped pet overlays must be marked as processed to prevent repeated large-payload injection.");
}

static async Task RuntimeDisposesCompatiblePredecessorInjectionAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var runtimePath = Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        "theme-runtime-v2.js");
    var runtime = await File.ReadAllTextAsync(runtimePath);
    Ensure(runtime.Contains("Object.getOwnPropertyNames(window)", StringComparison.Ordinal),
        "The runtime must discover an already injected compatible predecessor without retaining its brand key.");
    Ensure(runtime.Contains("typeof candidate.context.mountCanonicalTheme === \"function\"", StringComparison.Ordinal),
        "Predecessor discovery must require the canonical Tessalume runtime shape.");
    Ensure(runtime.Contains("await candidate.dispose()", StringComparison.Ordinal),
        "A compatible predecessor runtime must be disposed before the renamed runtime mounts.");
}

static async Task RuntimePreflightsAssetsBeforeReplacementAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var runtime = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        "theme-runtime-v2.js"));
    var preflightIndex = runtime.IndexOf(
        "assetAssignments.push([variable, createAssetObjectUrl(dataUrl)]);",
        StringComparison.Ordinal);
    var replacementIndex = preflightIndex < 0
        ? -1
        : runtime.IndexOf(
            "if (!(await disposeCompatibleRuntime())",
            preflightIndex,
            StringComparison.Ordinal);
    var attachIndex = replacementIndex < 0
        ? -1
        : runtime.IndexOf(
            "appendChild(style);",
            replacementIndex,
            StringComparison.Ordinal);
    Ensure(preflightIndex >= 0 && replacementIndex > preflightIndex && attachIndex > replacementIndex,
        "Theme assets must be decoded before the active runtime is disposed, then attached after replacement.");
    Ensure(runtime.Contains(
            "for (const objectUrl of assetObjectUrls) URL.revokeObjectURL(objectUrl);",
            StringComparison.Ordinal),
        "Failed theme preflight must release every prepared object URL.");
}

static async Task RestoreRemovesPredecessorRuntimeBrandsAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var source = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.Core",
        "Runtime",
        "ThemeRuntime.cs"));
    Ensure(source.Contains("window.__CODEX_THEME_STUDIO_RUNTIME__", StringComparison.Ordinal) &&
           source.Contains("delete window.__CODEX_THEME_STUDIO_THEME_ID__", StringComparison.Ordinal),
        "Restore must remove the predecessor runtime and marker after a brand migration.");
    Ensure(source.Contains("Object.getOwnPropertyNames(window)", StringComparison.Ordinal) &&
           source.Contains("candidate.context.mountCanonicalTheme", StringComparison.Ordinal),
        "Restore must also discover compatible runtime brands by canonical shape.");
    Ensure(source.Contains("await _applyLock.WaitAsync(cancellationToken);", StringComparison.Ordinal),
        "Restore must serialize with live theme application.");
}

static async Task RuntimeDiagnosticsUseTessalumeMarkersAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var source = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "tests",
        "Tessalume.Core.Tests",
        "Program.cs"));
    Ensure(source.Contains(
            "window.__TESSALUME_RUNTIME__ && document.documentElement.classList.contains('tessalume-theme-active')",
            StringComparison.Ordinal),
        "Runtime mode diagnostics must select the active Tessalume runtime.");
    var predecessorClassCheck =
        "document.documentElement.classList.contains('codex-" + "dream-skin')";
    Ensure(!source.Contains(predecessorClassCheck, StringComparison.Ordinal),
        "Runtime mode diagnostics must not depend on the predecessor skin class.");
}

static async Task DeferredMainUiReplaysEngineStateAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var source = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "MainWindow.xaml.cs"));
    var setterStart = source.IndexOf("private void SetEngineState(string status)", StringComparison.Ordinal);
    var cacheIndex = setterStart < 0
        ? -1
        : source.IndexOf("_engineStateText = status;", setterStart, StringComparison.Ordinal);
    var uiGuardIndex = setterStart < 0
        ? -1
        : source.IndexOf("if (_uiInitialized)", setterStart, StringComparison.Ordinal);
    Ensure(setterStart >= 0 && cacheIndex > setterStart && uiGuardIndex > cacheIndex,
        "Engine state must be cached before the deferred main UI guard.");
    Ensure(source.Contains("SetEngineState(_engineStateText);", StringComparison.Ordinal),
        "Main UI initialization and recoloring must replay the cached live engine state.");
}

static async Task StartupRegistrationStaysOptInAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var startupSource = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Infrastructure",
        "StartupRegistration.cs"));
    var appSource = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "App.xaml.cs"));
    var mainWindowSource = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "MainWindow.xaml.cs"));
    var cleanupStart = startupSource.IndexOf("public static bool TryCleanLegacyRegistration()", StringComparison.Ordinal);
    var cleanupEnd = cleanupStart < 0
        ? -1
        : startupSource.IndexOf("public static bool IsEnabled()", cleanupStart, StringComparison.Ordinal);
    Ensure(cleanupStart >= 0 && cleanupEnd > cleanupStart,
        "Startup registration must expose a bounded predecessor cleanup path.");
    var cleanupBlock = startupSource[cleanupStart..cleanupEnd];
    Ensure(startupSource.Contains("LegacyValueName = \"CodexThemeStudio\"", StringComparison.Ordinal) &&
           cleanupBlock.Contains("key.DeleteValue(LegacyValueName", StringComparison.Ordinal) &&
           !cleanupBlock.Contains("key.SetValue(ValueName", StringComparison.Ordinal),
        "Application startup may clean the predecessor value but must never opt users into startup.");
    Ensure(appSource.Contains("StartupRegistration.TryCleanLegacyRegistration()", StringComparison.Ordinal),
        "Application startup must clean only the predecessor registration.");
    Ensure(mainWindowSource.Contains("StartupCheckBox.IsChecked = enabled;", StringComparison.Ordinal),
        "The settings checkbox and toolbar startup button must share one current registry state.");
}

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

static async Task AutomaticUpdateWorkflowIsConnectedAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
    var xaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
    var mainSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml.cs"));
    var appSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "App.xaml.cs"));
    var bootstrapper = await File.ReadAllTextAsync(Path.Combine(appRoot, "Infrastructure", "UpdateBootstrapper.cs"));
    var preferences = await File.ReadAllTextAsync(Path.Combine(appRoot, "Infrastructure", "UiPreferencesStore.cs"));
    Ensure(xaml.Contains("x:Name=\"AutomaticUpdatesCheckBox\"", StringComparison.Ordinal) &&
           xaml.Contains("x:Name=\"CheckForUpdatesButton\"", StringComparison.Ordinal) &&
           xaml.Contains("x:Name=\"UpdateProgressBar\"", StringComparison.Ordinal),
        "Settings must expose automatic checks, a manual check, and download progress.");
    Ensure(preferences.Contains("AutomaticUpdateChecks { get; init; } = true", StringComparison.Ordinal) &&
           preferences.Contains("LastUpdateCheckAt", StringComparison.Ordinal),
        "Automatic update checks must default on and retain their last-check timestamp.");
    Ensure(mainSource.Contains("ScheduleAutomaticUpdateCheck", StringComparison.Ordinal) &&
           mainSource.Contains("DownloadAndInstallUpdateAsync", StringComparison.Ordinal) &&
           mainSource.Contains("UpdateBootstrapper.StartHelper", StringComparison.Ordinal),
        "The main product flow must check, download, verify, and hand off installation.");
    Ensure(appSource.Contains("UpdateBootstrapper.TryParseHelperArguments", StringComparison.Ordinal) &&
           bootstrapper.Contains("PortableUpdateInstaller.ApplyAndWriteResultAsync", StringComparison.Ordinal) &&
           bootstrapper.Contains("UseShellExecute = false", StringComparison.Ordinal),
        "A hidden standalone helper path must apply the update after the main EXE exits.");
    var readResultAt = appSource.IndexOf("var startupUpdateResult = UpdateBootstrapper.ReadResult", StringComparison.Ordinal);
    var cleanupAt = appSource.IndexOf("UpdateBootstrapper.CleanupStaleArtifactsAsync", StringComparison.Ordinal);
    var handoffAt = appSource.IndexOf("mainWindow.SetStartupUpdateResult(startupUpdateResult)", StringComparison.Ordinal);
    Ensure(readResultAt >= 0 && cleanupAt > readResultAt && handoffAt > cleanupAt &&
           appSource.Contains("if (startupUpdateResult is null)", StringComparison.Ordinal),
        "The rollback backup must remain available until the updated application has completed startup.");
}

static async Task FirstRunOnboardingNeverAppliesRandomThemeAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var source = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "MainWindow.xaml.cs"));
    var onboardingXaml = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "FirstRunWindow.xaml"));
    var startupStart = source.IndexOf("internal async Task StartInQuickModeAsync()", StringComparison.Ordinal);
    var startupEnd = startupStart < 0
        ? -1
        : source.IndexOf("private async void MainWindow_Closed", startupStart, StringComparison.Ordinal);
    Ensure(startupStart >= 0 && startupEnd > startupStart,
        "Quick-mode startup must remain a distinct testable block.");
    var startupBlock = source[startupStart..startupEnd];
    var loadStateIndex = startupBlock.IndexOf("var state = await _stateStore.LoadAsync();", StringComparison.Ordinal);
    var firstRunIndex = startupBlock.IndexOf("if (state is null && !_onboardingCompleted)", StringComparison.Ordinal);
    var onboardingIndex = startupBlock.IndexOf("FirstRunWindow.Show", StringComparison.Ordinal);
    var resumeIndex = startupBlock.IndexOf("await TryResumeAsync(state);", StringComparison.Ordinal);
    Ensure(loadStateIndex >= 0 && firstRunIndex > loadStateIndex && onboardingIndex > firstRunIndex && resumeIndex > onboardingIndex,
        "Startup must show onboarding before resuming an existing theme state.");
    Ensure(!source.Contains("ApplyRandomThemeOnStartupAsync", StringComparison.Ordinal) &&
           !source.Contains("Random.Shared", StringComparison.Ordinal),
        "First-run startup must never choose or apply a random theme.");
    Ensure(source.Contains("需要重新启动 Codex", StringComparison.Ordinal) &&
           source.Contains("ShowProductConfirmation", StringComparison.Ordinal),
        "Restarting an existing Codex session must require an explicit confirmation.");
    Ensure(onboardingXaml.Contains("首次启动不会自动换肤", StringComparison.Ordinal) &&
           onboardingXaml.Contains("进入主题库", StringComparison.Ordinal) &&
           onboardingXaml.Contains("必要时重新连接", StringComparison.Ordinal),
        "The first-run window must explain choice, restart behavior, and the next action.");
    Ensure(source.Contains("private ThemeCardModel[] GetQuickSwitchCandidates()", StringComparison.Ordinal) &&
           source.Contains("GetQuickSwitchCandidates());", StringComparison.Ordinal),
        "Quick switching must retain the dynamic favorites-first candidate rule.");
}

static async Task BuildScriptLaunchesPublishedExecutableAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var source = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "一键构建EXE.ps1"));
    Ensure(source.Contains("[switch]$NoLaunch", StringComparison.Ordinal) &&
           source.Contains("if (-not $NoLaunch)", StringComparison.Ordinal) &&
           source.Contains("Start-Process -FilePath $finalExe -WorkingDirectory $output", StringComparison.Ordinal),
        "The one-click build must launch the newly published EXE by default and retain an explicit opt-out.");
}

static async Task ReleaseReadinessAssetsAreDocumentedAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var buildScript = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "一键构建EXE.ps1"));
    var readme = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "README.md"));
    var securityPath = Path.Combine(repositoryRoot, "SECURITY.md");
    var licensePath = Path.Combine(repositoryRoot, "LICENSE");
    var issueTemplatePath = Path.Combine(repositoryRoot, ".github", "ISSUE_TEMPLATE", "bug-report.yml");
    var releaseChecklistPath = Path.Combine(repositoryRoot, "docs", "RELEASE_CHECKLIST.md");
    var license = await File.ReadAllTextAsync(licensePath);

    Ensure(buildScript.Contains("Get-FileHash -LiteralPath $finalExe -Algorithm SHA256", StringComparison.Ordinal) &&
           buildScript.Contains("SHA256SUMS.txt", StringComparison.Ordinal),
        "The release build must create a SHA-256 manifest beside the executable.");
    Ensure(File.Exists(securityPath) && File.Exists(issueTemplatePath) && File.Exists(releaseChecklistPath) &&
           license.Contains("MIT License", StringComparison.Ordinal) &&
           license.Contains("Permission is hereby granted", StringComparison.Ordinal),
        "Public testing requires an MIT license, security guidance, a structured bug form, and a release checklist.");
    Ensure(readme.Contains("issues/new?template=bug-report.yml", StringComparison.Ordinal) &&
           readme.Contains("Microsoft Defender SmartScreen", StringComparison.Ordinal) &&
           readme.Contains("SHA256SUMS.txt", StringComparison.Ordinal) &&
           readme.Contains("[MIT License](LICENSE)", StringComparison.Ordinal),
        "The download guide must expose feedback, signature status, checksum verification, and licensing.");
}

static async Task RuntimeRemovesNativeComposerFadeAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);

    var payload = await BuildPayloadAsync(repositoryRoot, package);
    Ensure(payload.Contains("from-token-main-surface-primary", StringComparison.Ordinal),
        "The runtime must neutralize Codex's native bottom composer fade for every active theme.");
    Ensure(payload.Contains("background:transparent!important", StringComparison.Ordinal),
        "The native composer fade override must remain transparent.");
    Ensure(payload.Contains(":has(.composer-surface-chrome) .sticky.bottom-0", StringComparison.Ordinal),
        "The runtime must keep the sticky composer visible on Codex home layout changes.");
    Ensure(payload.Contains("min-height:64px!important", StringComparison.Ordinal),
        "The composer surface must keep a visible minimum hit area.");
    Ensure(payload.Contains("tessalume-code-review-open", StringComparison.Ordinal),
        "The runtime must track Codex's code-review diff state.");
    Ensure(payload.Contains("data-tessalume-side-panel-overlay", StringComparison.Ordinal),
        "The runtime must hide theme overlays while the native sidebar is open.");
    Ensure(payload.Contains("data-tessalume-auto-hidden", StringComparison.Ordinal),
        "The runtime must fade theme widgets that do not fit beside the real chat content.");
    Ensure(payload.Contains("data-tessalume-left-rail", StringComparison.Ordinal) &&
           payload.Contains("data-tessalume-right-rail", StringComparison.Ordinal),
        "The runtime must expose geometry-based left and right task rail capacity.");
    Ensure(payload.Contains("settings-surface", StringComparison.Ordinal) &&
           payload.Contains("is-settings", StringComparison.Ordinal),
        "The runtime must expose a stable semantic state for settings surfaces.");
    Ensure(payload.Contains("new ResizeObserver", StringComparison.Ordinal),
        "Adaptive task rails must react to workspace and composer resizing.");
    Ensure(payload.Contains("syncStageGeometry", StringComparison.Ordinal) &&
           payload.Contains("startLayoutTracking", StringComparison.Ordinal),
        "The runtime must keep its fixed theme stage aligned throughout native layout transitions.");
    Ensure(payload.Contains("validateTemplateStructure", StringComparison.Ordinal) &&
           payload.Contains("data-tessalume-template-version", StringComparison.Ordinal),
        "The runtime must validate and expose the declared flagship template version.");
    Ensure(payload.Contains(
            "Template 1.0 requires one primary and one secondary task-right card",
            StringComparison.Ordinal) &&
           payload.Contains(
            "Template 1.0 sync-panel must hide with the secondary task card",
            StringComparison.Ordinal),
        "Template 1.0 must enforce its paired-card and sync-panel visibility contract.");
    var resizeObserverIndex = payload.IndexOf("layoutResizeObserver = new ResizeObserver", StringComparison.Ordinal);
    var immediateLayoutIndex = resizeObserverIndex < 0
        ? -1
        : payload.IndexOf("syncLiveLayout();", resizeObserverIndex, StringComparison.Ordinal);
    var delayedRepairIndex = resizeObserverIndex < 0
        ? -1
        : payload.IndexOf("schedule();", resizeObserverIndex, StringComparison.Ordinal);
    Ensure(resizeObserverIndex >= 0 &&
           immediateLayoutIndex > resizeObserverIndex &&
           delayedRepairIndex > immediateLayoutIndex,
        "ResizeObserver must align live geometry before scheduling the debounced DOM repair.");
    Ensure(!payload.Contains("min-height: 0 !important", StringComparison.Ordinal),
        "The runtime must not flatten themed home hero containers.");
    Ensure(payload.Contains("home-banners:empty", StringComparison.Ordinal),
        "The runtime must repair Codex's empty home-banners wrapper before themes inspect the home DOM.");
    Ensure(payload.Contains(".group\\\\/home-suggestions", StringComparison.Ordinal),
        "The runtime must hide Codex's native home suggestion cards for themed home pages.");
    Ensure(payload.Contains(".group\\\\/title", StringComparison.Ordinal),
        "The runtime must hide Codex's native home prompt title for themed home pages.");
}

static async Task RuntimeDecoratesTaskSurfacesBeforeDeferredRepairAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var runtimePath = Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        "theme-runtime-v2.js");
    var runtime = await File.ReadAllTextAsync(runtimePath);
    const string criticalSignature = "const decorateTaskCriticalSurfaces = (mutations) => {";
    const string sharedSignature = "const decorateSharedSurfaces = (main, aside, home) => {";
    var criticalStart = runtime.IndexOf(criticalSignature, StringComparison.Ordinal);
    var sharedStart = runtime.IndexOf(sharedSignature, StringComparison.Ordinal);
    Ensure(criticalStart >= 0 && sharedStart > criticalStart,
        "The canonical runtime must expose a lightweight task-surface decorator.");
    var criticalBlock = runtime[criticalStart..sharedStart];

    foreach (var role in new[]
             {
                 "markdown",
                 "message-assistant",
                 "message-user",
                 "chat-paper",
                 "task-header",
                 "task-title",
             })
    {
        Ensure(criticalBlock.Contains($"roleClass(\"{role}\")", StringComparison.Ordinal) ||
               runtime[..criticalStart].Contains($"roleClass(\"{role}\")", StringComparison.Ordinal),
            $"The immediate task decorator must cover the {role} semantic role.");
    }
    Ensure(criticalBlock.Contains("mutation.addedNodes", StringComparison.Ordinal) &&
           criticalBlock.Contains("mutation.target", StringComparison.Ordinal),
        "The immediate task decorator must cover inserted task trees and streamed child updates.");
    Ensure(!criticalBlock.Contains("getBoundingClientRect", StringComparison.Ordinal) &&
           !criticalBlock.Contains("requestAnimationFrame", StringComparison.Ordinal) &&
           !criticalBlock.Contains("setTimeout", StringComparison.Ordinal) &&
           !criticalBlock.Contains("document.querySelectorAll('[class*=\"_markdownContent_\"]')", StringComparison.Ordinal),
        "The immediate task decorator must not perform layout work, defer itself, or scan every message.");

    const string mutationSignature = "const onDocumentMutations = (mutations) => {";
    const string cleanupSignature = "const cleanup = () => {";
    var mutationStart = runtime.IndexOf(mutationSignature, StringComparison.Ordinal);
    var cleanupStart = runtime.IndexOf(cleanupSignature, StringComparison.Ordinal);
    Ensure(mutationStart >= 0 && cleanupStart > mutationStart,
        "The runtime must use a dedicated document-mutation fast path.");
    var mutationBlock = runtime[mutationStart..cleanupStart];
    var immediateIndex = mutationBlock.IndexOf(
        "decorateTaskCriticalSurfaces(mutations);",
        StringComparison.Ordinal);
    var deferredIndex = mutationBlock.IndexOf("schedule(true);", StringComparison.Ordinal);
    Ensure(immediateIndex >= 0 && deferredIndex > immediateIndex,
        "Task surfaces must be decorated before the debounced full repair is scheduled.");
    Ensure(runtime.Contains(
            "context.observe(document.documentElement, { childList:true, subtree:true }, onDocumentMutations);",
            StringComparison.Ordinal),
        "The document observer must use the immediate task-surface callback.");
}

static async Task PublishedThemesUseCanonicalInjectionContractAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var sharedCss = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        "theme-template-v1.css"));
    var runtimePath = Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        "theme-runtime-v2.js");
    var runtime = await File.ReadAllTextAsync(runtimePath);
    Ensure(runtime.Contains("mountCanonicalTheme", StringComparison.Ordinal),
        "The open runtime must expose the canonical theme host.");
    Ensure(runtime.Contains("renderTemplateV1", StringComparison.Ordinal) &&
           runtime.Contains("appendDecorations", StringComparison.Ordinal) &&
           runtime.Contains("data-tessalume-surface", StringComparison.Ordinal),
        "The open runtime must own Template 1.0 outer DOM and generic surface markers.");
    Ensure(runtime.Contains("syncRouteState();", StringComparison.Ordinal),
        "The canonical host must synchronize route state before its debounced repair.");
    Ensure(runtime.Contains("const decorateOutputPanels = () => {", StringComparison.Ordinal) &&
           runtime.Contains("[data-slot=\"thread-summary-panel-item-button\"]", StringComparison.Ordinal),
        "The canonical host must bind environment panels by their stable item slot, not only visible labels.");

    var themes = new[]
    {
        (Directory: "xin.moonfox-sovereign", Namespace: "xmf"),
        (Directory: "aemeath-star-voyage", Namespace: "ae3"),
        (Directory: "danya.bubble-void-duality", Namespace: "dny"),
        (Directory: "qingxiao.cloudsword-gate", Namespace: "qxo"),
    };
    foreach (var (directory, themeNamespace) in themes)
    {
        var themeRoot = Path.Combine(repositoryRoot, "themes", directory);
        var script = await File.ReadAllTextAsync(Path.Combine(themeRoot, "theme.js"));
        var css = await File.ReadAllTextAsync(Path.Combine(themeRoot, "skin.css"));
        Ensure(script.Contains("context.mountCanonicalTheme(", StringComparison.Ordinal),
            $"{directory} must use the canonical theme host.");
        Ensure(script.Contains("context.renderTemplateV1(", StringComparison.Ordinal),
            $"{directory} must use the shared Template 1.0 renderer.");
        Ensure(!script.Contains("context.observe(", StringComparison.Ordinal) &&
               !script.Contains("MutationObserver", StringComparison.Ordinal),
            $"{directory} must not own route observers.");
        Ensure(!script.Contains("data-theme-role=", StringComparison.Ordinal) &&
               !script.Contains("data-theme-stage", StringComparison.Ordinal),
            $"{directory} must not duplicate runtime-owned outer roles.");
        Ensure(!css.Contains("TESSALUME_TEMPLATE_V1_", StringComparison.Ordinal) &&
               !css.Contains("[data-theme-role=", StringComparison.Ordinal),
            $"{directory} skin must not duplicate shared surfaces or geometry.");
        Ensure(css.Contains("-is-task main.", StringComparison.Ordinal) &&
               css.Contains("-chat-art)", StringComparison.Ordinal),
            $"{directory} must paint chat art on the stable task main.");
        Ensure(!css.Contains($"main.{themeNamespace}-main>*{{position:relative", StringComparison.Ordinal),
            $"{directory} must not override every direct main child; doing so breaks Codex fixed headers.");
        Ensure(!css.Contains($"main.{themeNamespace}-main::before {{\n  content:\"\";\n  position:", StringComparison.Ordinal) &&
               !css.Contains($"main.{themeNamespace}-main::after {{\n  content:\"\";\n  position:", StringComparison.Ordinal) &&
               sharedCss.Contains("[data-tessalume-surface=\"main\"]::before { z-index:-2; }", StringComparison.Ordinal) &&
               sharedCss.Contains("[data-tessalume-surface=\"main\"]::after { z-index:-1; }", StringComparison.Ordinal),
            $"{directory} must inherit task-canvas stacking from the shared template stylesheet.");
        if (directory == "aemeath-star-voyage")
        {
            Ensure(script.Contains("stageDecorations:", StringComparison.Ordinal) &&
                   script.Contains("ae3-orbit", StringComparison.Ordinal),
                "Aemeath's character-specific stage orbit must survive shared-DOM migration.");
        }
        if (directory == "danya.bubble-void-duality")
        {
            Ensure(script.Contains("stageDecorations:", StringComparison.Ordinal) &&
                   script.Contains("data-dny-home-fx=\"bubble-prism-v2\"", StringComparison.Ordinal) &&
                   script.Contains("data-dny-home-fx=\"void-lattice-v2\"", StringComparison.Ordinal) &&
                   script.Contains("dny-main-frame", StringComparison.Ordinal) &&
                   script.Contains("class=\"dny-domain-line\" data-theme-part=\"hero-motion\"", StringComparison.Ordinal) &&
                   !script.Contains("homeEffects", StringComparison.Ordinal) &&
                   css.Contains(".dny-domain-phases-light", StringComparison.Ordinal) &&
                   css.Contains(".dny-domain-phases-dark", StringComparison.Ordinal) &&
                   script.Contains("data-dny-sync-fx=\"duality-chamber-v2\"", StringComparison.Ordinal) &&
                   css.Contains(".dny-sync-core", StringComparison.Ordinal) &&
                   css.Contains(".dny-sync-state", StringComparison.Ordinal),
                "Danya's light/dark home effects must live in the canonical hero-motion slot.");
        }
        if (directory == "qingxiao.cloudsword-gate")
        {
            Ensure(script.Contains("class=\"qxo-score\" data-theme-part=\"hero-motion\"", StringComparison.Ordinal) &&
                   script.Contains("data-qxo-home-fx=\"cloud-heart-sword-v2\"", StringComparison.Ordinal) &&
                   script.Contains("data-qxo-home-fx=\"moon-sword-array-v2\"", StringComparison.Ordinal) &&
                   !script.Contains("qxo-banner-fx", StringComparison.Ordinal) &&
                   css.Contains(".qxo-score-form-light", StringComparison.Ordinal) &&
                   css.Contains(".qxo-score-form-dark", StringComparison.Ordinal),
                "Qingxiao's light/dark sword arrays must live in the canonical hero-motion slot.");
        }
        if (directory == "shorekeeper.tethys-reverie")
        {
            Ensure(script.Contains("class=\"sk3-tide\" data-theme-part=\"hero-motion\"", StringComparison.Ordinal) &&
                   script.Contains("data-sk3-home-fx=\"shoreline-butterfly-v2\"", StringComparison.Ordinal) &&
                   script.Contains("data-sk3-home-fx=\"tethys-probability-v2\"", StringComparison.Ordinal) &&
                   css.Contains(".sk3-tide-form-light", StringComparison.Ordinal) &&
                   css.Contains(".sk3-tide-form-dark", StringComparison.Ordinal) &&
                   !css.Contains("sk3-route-scan", StringComparison.Ordinal),
                "Shorekeeper's light/dark home effects must live in the canonical hero-motion slot.");
        }
        if (directory == "suisui.inkscape-dawn")
        {
            Ensure(script.Contains("class=\"sui-river\" data-theme-part=\"hero-motion\"", StringComparison.Ordinal) &&
                   script.Contains("data-sui-home-fx=\"dawn-fan-scroll-v2\"", StringComparison.Ordinal) &&
                   script.Contains("data-sui-home-fx=\"moonlit-chongming-v2\"", StringComparison.Ordinal) &&
                   !script.Contains("sui-banner-fx", StringComparison.Ordinal) &&
                   css.Contains(".sui-river-form-light", StringComparison.Ordinal) &&
                   css.Contains(".sui-river-form-dark", StringComparison.Ordinal) &&
                   script.Contains("data-sui-sync-fx=\"shanhe-fan-v2\"", StringComparison.Ordinal) &&
                   css.Contains(".sui-sync-core", StringComparison.Ordinal) &&
                   css.Contains(".sui-sync-state", StringComparison.Ordinal),
                "Suisui's light/dark home effects must live in the canonical hero-motion slot.");
        }
        if (directory == "xin.moonfox-sovereign")
        {
            Ensure(script.Contains("adaptiveLayout: true", StringComparison.Ordinal),
                "The flagship candidate must opt into geometry-based task widget visibility.");
            Ensure(script.Contains("taskSecondary:", StringComparison.Ordinal) &&
                   script.Contains("taskPrimary:", StringComparison.Ordinal),
                "The flagship candidate must fill both canonical right-card slots.");
            Ensure(!css.Contains("height:502px!important", StringComparison.Ordinal) &&
                   !css.Contains("min-height:502px!important", StringComparison.Ordinal),
                "The flagship candidate home hero must not regress to its fixed-height crop.");
            Ensure(css.Contains(".xmf-is-settings .xmf-settings-surface", StringComparison.Ordinal) &&
                   css.Contains("electron-dark.xmf-is-settings", StringComparison.Ordinal),
                "The flagship candidate must reveal its light and dark chat artwork behind settings.");
        }
    }
}

static async Task FlagshipTemplateV1FreezesSharedStructureAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var skillRoot = Path.Combine(
        repositoryRoot,
        ".agents",
        "skills",
        "author-tessalume-theme");
    var templateRoot = Path.Combine(skillRoot, "assets", "theme-template");
    var templateScript = await File.ReadAllTextAsync(Path.Combine(templateRoot, "theme.js"));
    var templateCss = await File.ReadAllTextAsync(Path.Combine(templateRoot, "skin.css"));
    var sharedCss = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        "theme-template-v1.css"));
    var templateManifest = await File.ReadAllTextAsync(Path.Combine(templateRoot, "manifest.json"));
    var validator = await File.ReadAllTextAsync(
        Path.Combine(skillRoot, "scripts", "validate_theme_contract.py"));
    var geometrySync = await File.ReadAllTextAsync(
        Path.Combine(skillRoot, "scripts", "sync_template_geometry.py"));
    var exampleSync = await File.ReadAllTextAsync(
        Path.Combine(skillRoot, "scripts", "sync_template_example.py"));

    Ensure(templateScript.Contains("templateVersion: \"1.0\"", StringComparison.Ordinal) &&
           templateScript.Contains("adaptiveLayout: true", StringComparison.Ordinal) &&
           templateScript.Contains("context.renderTemplateV1(", StringComparison.Ordinal) &&
           templateScript.Contains("data-theme-draft=", StringComparison.Ordinal),
        "The reusable template must opt into Template 1.0 and adaptive layout.");
    Ensure(templateManifest.Contains("\"version\": \"1.0\"", StringComparison.Ordinal) &&
           templateManifest.Contains("\"style\": \"shared\"", StringComparison.Ordinal) &&
           templateManifest.Contains("\"qualityGate\": \"flagship-complete-1\"", StringComparison.Ordinal) &&
           templateManifest.Contains("assets/placeholder.svg", StringComparison.Ordinal),
        "The reusable template must be valid before custom artwork is added.");
    Ensure(validator.Contains("REQUIRED_SLOTS", StringComparison.Ordinal) &&
           validator.Contains("DRAFT_TOKENS", StringComparison.Ordinal) &&
           validator.Contains("flagship visual coverage missing", StringComparison.Ordinal) &&
           templateCss.Contains("aside.app-shell-left-panel::after", StringComparison.Ordinal) &&
           templateCss.Contains("-task-title", StringComparison.Ordinal) &&
           templateCss.Contains("thread-summary-panel-item-button", StringComparison.Ordinal) &&
           templateCss.Contains("_footer_", StringComparison.Ordinal) &&
           validator.Contains("skin.css", StringComparison.Ordinal) &&
           geometrySync.Contains("--check", StringComparison.Ordinal) &&
           exampleSync.Contains("repo_root / \"examples\"", StringComparison.Ordinal) &&
           !Directory.Exists(Path.Combine(repositoryRoot, "examples", "advanced-theme")),
        "The authoring skill must validate shared structure and skin isolation.");

    var requiredParts = new[]
    {
        "hero-kicker",
        "hero-title-light",
        "hero-title-dark",
        "hero-motion",
        "hero-note",
        "identity-emblem",
        "identity-copy",
        "identity-status",
        "task-card-art",
        "task-card-caption",
        "memory-meter",
        "sync-copy",
        "sync-core",
        "sync-meter",
        "sync-state",
    };
    foreach (var part in requiredParts)
    {
        Ensure(templateScript.Contains($"data-theme-part=\"{part}\"", StringComparison.Ordinal),
            $"The reusable template is missing structure part {part}.");
    }

    Ensure(sharedCss.Contains("width:146px;", StringComparison.Ordinal) &&
           sharedCss.Contains("height:234px;", StringComparison.Ordinal) &&
           sharedCss.Contains("top:334px;", StringComparison.Ordinal) &&
           sharedCss.Contains("width:320px;", StringComparison.Ordinal) &&
           sharedCss.Contains("height:56px;", StringComparison.Ordinal) &&
           sharedCss.Contains("--tessalume-v1-home-composer-reserve:240px;", StringComparison.Ordinal) &&
           sharedCss.Contains("calc(100cqh - var(--tessalume-v1-home-composer-reserve))", StringComparison.Ordinal) &&
           sharedCss.Contains("data-tessalume-surface=\"chat-paper\"", StringComparison.Ordinal),
        "Runtime-owned Template 1.0 geometry must preserve the accepted Xin layout.");
    Ensure(!templateCss.Contains("[data-theme-role=", StringComparison.Ordinal) &&
           !templateCss.Contains("TESSALUME_TEMPLATE_V1_", StringComparison.Ordinal),
        "The reusable skin must not contain shared geometry.");

    var implementations = new[]
    {
        (Root: Path.Combine(repositoryRoot, "themes", "xin.moonfox-sovereign"), Namespace: "xmf"),
        (Root: Path.Combine(repositoryRoot, "examples"), Namespace: "example"),
    };
    foreach (var (root, themeNamespace) in implementations)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(root, "theme.js"));
        var css = await File.ReadAllTextAsync(Path.Combine(root, "skin.css"));
        Ensure(script.Contains("templateVersion: \"1.0\"", StringComparison.Ordinal),
            $"{Path.GetFileName(root)} must declare Template 1.0.");
        Ensure(!script.Contains('\0'),
            $"{Path.GetFileName(root)} contains an invalid null character.");
        foreach (var part in requiredParts)
        {
            Ensure(script.Contains($"data-theme-part=\"{part}\"", StringComparison.Ordinal),
                $"{Path.GetFileName(root)} is missing Template 1.0 part {part}.");
        }
        Ensure(script.Contains("context.renderTemplateV1(", StringComparison.Ordinal) &&
               !script.Contains("data-theme-role=", StringComparison.Ordinal) &&
               !css.Contains("[data-theme-role=", StringComparison.Ordinal) &&
               !css.Contains("home-hero-height", StringComparison.Ordinal) &&
               !css.Contains("height:502px!important", StringComparison.Ordinal) &&
               !css.Contains("flex:0 0 526px!important", StringComparison.Ordinal),
            $"{Path.GetFileName(root)} has duplicated runtime-owned Template 1.0 structure.");
    }
}

static async Task ArtworkAdjustmentsAreRuntimeOwnedAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var runtimeSource = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        "theme-runtime-v2.js"));
    var sharedCss = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        ThemePayloadBuilder.SharedTemplateStyleFileName));
    var mainWindowXaml = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "MainWindow.xaml"));
    var mainWindowSource = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "MainWindow.xaml.cs"));
    Ensure(runtimeSource.Contains("setVisualSettings", StringComparison.Ordinal) &&
           runtimeSource.Contains("__TESSALUME_STAGED_VISUAL_SETTINGS__", StringComparison.Ordinal),
        "The runtime must stage and live-update persisted artwork settings.");
    Ensure(mainWindowXaml.Contains("x:Name=\"SettingsThemeControlBar\"", StringComparison.Ordinal) &&
           mainWindowXaml.Contains("Click=\"SettingsPreviousTheme_Click\"", StringComparison.Ordinal) &&
           mainWindowXaml.Contains("Click=\"SettingsNextTheme_Click\"", StringComparison.Ordinal) &&
           mainWindowXaml.Contains("Click=\"SettingsColorMode_Click\"", StringComparison.Ordinal) &&
           mainWindowXaml.Contains("x:Name=\"VisualEditingModeText\"", StringComparison.Ordinal),
        "Advanced artwork settings must expose the compact live theme and color-mode controls.");
    Ensure(mainWindowSource.Contains("ApplyRelativeSettingsThemeAsync", StringComparison.Ordinal) &&
           mainWindowSource.Contains("ToggleCodexColorSchemeAsync", StringComparison.Ordinal) &&
           !mainWindowXaml.Contains("VisualLightModeButton", StringComparison.Ordinal) &&
           !mainWindowXaml.Contains("VisualDarkModeButton", StringComparison.Ordinal),
        "The settings editor must follow the real Codex mode instead of a detached parameter-only toggle.");
    foreach (var region in new[] { "hero", "sidebar", "chat" })
    {
        foreach (var mode in new[] { "light", "dark" })
        {
            Ensure(sharedCss.Contains($"--tessalume-visual-{region}-{mode}-filter", StringComparison.Ordinal) &&
                   sharedCss.Contains($"--tessalume-visual-{region}-{mode}-opacity", StringComparison.Ordinal),
                $"The shared template is missing {mode} {region} adjustment variables.");
        }
    }

    var normalized = new ThemeVisualSettings
    {
        Light = new ThemeVisualModeSettings
        {
            Hero = new ThemeArtworkAdjustment
            {
                Brightness = -1,
                Contrast = 900,
                Saturation = -5,
                Opacity = 400,
            },
        },
    }.Normalize();
    Ensure(normalized.Light.Hero.Brightness == 20 &&
           normalized.Light.Hero.Contrast == 180 &&
           normalized.Light.Hero.Saturation == 0 &&
           normalized.Light.Hero.Opacity == 100,
        "Persisted artwork values must be normalized before entering the renderer.");

    var rulePattern = new Regex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.CultureInvariant);
    foreach (var directory in Directory.EnumerateDirectories(Path.Combine(repositoryRoot, "themes")))
    {
        var cssPath = Path.Combine(directory, "skin.css");
        var css = await File.ReadAllTextAsync(cssPath);
        var rules = rulePattern.Matches(css).Cast<Match>().Where(match =>
        {
            var selector = match.Groups["selector"].Value;
            return selector.Contains("aside.app-shell-left-panel::after", StringComparison.Ordinal) ||
                   selector.Contains("-is-task main.", StringComparison.Ordinal) && selector.Contains("-main::before", StringComparison.Ordinal) ||
                   selector.Contains("-home>div:first-child>div:first-child>div:first-child::before", StringComparison.Ordinal);
        }).ToArray();
        Ensure(rules.Length >= 3, $"{directory} must expose all three adjustable artwork layers.");
        foreach (var rule in rules)
        {
            var body = rule.Groups["body"].Value;
            Ensure(!body.Contains("filter:", StringComparison.OrdinalIgnoreCase) &&
                   !body.Contains("opacity:", StringComparison.OrdinalIgnoreCase),
                $"{directory} hard-codes artwork correction inside {rule.Groups["selector"].Value.Trim()}.");
        }
    }
}

static async Task MainProductSurfacesShareDesignSystemAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var xaml = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "MainWindow.xaml"));
    var source = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "MainWindow.xaml.cs"));
    var cardModel = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Models",
        "ThemeCardModel.cs"));

    foreach (var marker in new[]
             {
                 "x:Key=\"PageTitleText\"",
                 "x:Key=\"ProductCard\"",
                 "x:Name=\"EmptyStateActionButton\"",
                 "x:Name=\"DiagnosticHealthTitleText\"",
                 "Content=\"选择主题文件夹\"",
                 "Text=\"推荐工作流\"",
             })
    {
        Ensure(xaml.Contains(marker, StringComparison.Ordinal),
            $"The unified product surface is missing {marker}.");
    }

    Ensure(source.Contains("DiagnosticHealthBodyText", StringComparison.Ordinal) &&
           source.Contains("EmptyStateAction_Click", StringComparison.Ordinal),
        "Product surfaces must expose live diagnostic summaries and useful empty-state actions.");
    Ensure(!cardModel.Contains("BUILT-IN", StringComparison.Ordinal) &&
           !cardModel.Contains("LOCAL", StringComparison.Ordinal),
        "Chinese product surfaces should not fall back to legacy English theme badges.");
}

static async Task AdaptiveLayoutAndKeyboardAccessibilityAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
    var mainXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
    var mainSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml.cs"));
    var quickXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "ThemeQuickSwitchWindow.xaml"));
    var manifest = await File.ReadAllTextAsync(Path.Combine(appRoot, "app.manifest"));

    Ensure(mainXaml.Contains("x:Name=\"AdaptiveViewport\"", StringComparison.Ordinal) &&
           mainXaml.Contains("x:Name=\"AdaptiveScale\"", StringComparison.Ordinal) &&
           mainXaml.Contains("MinWidth=\"760\" MinHeight=\"420\"", StringComparison.Ordinal),
        "The main product surface must scale down instead of extending beyond a small work area.");
    Ensure(mainSource.Contains("FitWindowToWorkArea", StringComparison.Ordinal) &&
           mainSource.Contains("AdaptiveViewport_SizeChanged", StringComparison.Ordinal) &&
           mainSource.Contains("_quickSwitchWindow.Close();", StringComparison.Ordinal) &&
           mainSource.Contains("Key.F", StringComparison.Ordinal) &&
           mainSource.Contains("Key.I", StringComparison.Ordinal) &&
           mainSource.Contains("Key.F5", StringComparison.Ordinal),
        "Small-screen fitting and documented keyboard shortcuts must remain wired.");
    Ensure(mainXaml.Contains("x:Key=\"KeyboardFocusVisual\"", StringComparison.Ordinal) &&
           !mainXaml.Contains("FocusVisualStyle\" Value=\"{x:Null}", StringComparison.Ordinal) &&
           mainXaml.Contains("AutomationProperties.Name=\"首页横幅亮度\"", StringComparison.Ordinal) &&
           mainXaml.Contains("AutomationProperties.Name=\"聊天背景不透明度\"", StringComparison.Ordinal),
        "Keyboard focus and advanced image sliders require visible, descriptive accessibility metadata.");
    Ensure(quickXaml.Contains("AutomationProperties.Name=\"上一个可切换主题\"", StringComparison.Ordinal) &&
           quickXaml.Contains("AutomationProperties.Name=\"关闭主题浮窗\"", StringComparison.Ordinal) &&
           quickXaml.Contains("IsKeyboardFocused", StringComparison.Ordinal),
        "The icon-only quick bar controls must be named and visibly focusable.");
    Ensure(manifest.Contains("PerMonitorV2, PerMonitor", StringComparison.Ordinal),
        "The Windows application manifest must opt into per-monitor DPI scaling.");
}

static async Task Version12ProductWorkflowIsCompleteAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
    var xaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
    var source = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml.cs"));
    var dialogXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "ProductDialogWindow.xaml"));
    var dialogSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "ProductDialogWindow.xaml.cs"));
    var project = await File.ReadAllTextAsync(Path.Combine(appRoot, "Tessalume.App.csproj"));
    var readme = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "README.md"));

    foreach (var marker in new[]
             {
                 "x:Name=\"ThemeSearchBox\"",
                 "x:Name=\"AllThemesFilterButton\"",
                 "x:Name=\"LightThemesFilterButton\"",
                 "x:Name=\"DarkThemesFilterButton\"",
                 "x:Name=\"ThemeResultText\"",
                 "x:Name=\"ToastPanel\"",
                 "x:Name=\"AboutInfoPanel\"",
                 "x:Name=\"AboutLibrarySummaryText\"",
             })
    {
        Ensure(xaml.Contains(marker, StringComparison.Ordinal), $"The 1.2 interface is missing {marker}.");
    }

    Ensure(source.Contains("ApplyThemeLibraryFilter", StringComparison.Ordinal) &&
           source.Contains("ThemeSearchBox_FocusChanged", StringComparison.Ordinal) &&
           source.Contains("ShowProductConfirmation", StringComparison.Ordinal) &&
           source.Contains("ShowToast", StringComparison.Ordinal) &&
           !source.Contains("MessageBox.Show", StringComparison.Ordinal),
        "The main interface must use searchable filtering and unified in-product feedback.");
    Ensure(xaml.Contains("Property=\"Cursor\" Value=\"IBeam\"", StringComparison.Ordinal) &&
           xaml.Contains("GotKeyboardFocus=\"ThemeSearchBox_FocusChanged\"", StringComparison.Ordinal),
        "The search field must keep a clear text caret and hide its placeholder while focused.");
    Ensure(dialogXaml.Contains("DialogAccentBrush", StringComparison.Ordinal) &&
           dialogXaml.Contains("IsDefault=\"True\"", StringComparison.Ordinal) &&
           dialogXaml.Contains("IsCancel=\"True\"", StringComparison.Ordinal) &&
           dialogSource.Contains("CancelButton.IsDefault = true", StringComparison.Ordinal),
        "The product dialog must support consistent styling and keyboard-safe confirmation.");
    Ensure(project.Contains("<Version>1.2.0</Version>", StringComparison.Ordinal) &&
           readme.Contains("## Tessalume 1.2", StringComparison.Ordinal) &&
           readme.Contains("十套内置旗舰主题", StringComparison.Ordinal) &&
           File.Exists(Path.Combine(repositoryRoot, "CHANGELOG.md")),
        "Version metadata and release documentation must agree on Tessalume 1.2.");
}

static async Task PortableCreatorWorkspaceIsSelfContainedAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var appProject = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Tessalume.App.csproj"));
    var mainSource = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "MainWindow.xaml.cs"));
    var mainXaml = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "MainWindow.xaml"));
    var installerSource = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Infrastructure",
        "BuiltInAssetInstaller.cs"));
    var appSource = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "App.xaml.cs"));
    var skill = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        ".agents",
        "skills",
        "author-tessalume-theme",
        "SKILL.md"));
    var workspaceGuide = await File.ReadAllTextAsync(Path.Combine(
        repositoryRoot,
        "creator-workspace",
        "START_HERE.md"));

    Ensure(appProject.Contains("Tessalume.CreatorWorkspace/", StringComparison.Ordinal) &&
           appProject.Contains("author-tessalume-theme", StringComparison.Ordinal) &&
           appProject.Contains("theme-manifest-v2.schema.json", StringComparison.Ordinal) &&
           appProject.Contains("Compatibility\\*.css", StringComparison.Ordinal) &&
           installerSource.Contains("CompatibilityPrefix + \"theme-template-v1.css\"", StringComparison.Ordinal),
        "The EXE must embed the creator guide, Skill, schema, and shared Template 1.0 geometry.");
    Ensure(installerSource.Contains("CreateCreatorWorkspace", StringComparison.Ordinal) &&
           installerSource.Contains("CreatorWorkspacePrefix", StringComparison.Ordinal),
        "The app must be able to export the complete embedded creator workspace.");
    Ensure(appSource.Contains("--export-creator-workspace", StringComparison.Ordinal) &&
           appSource.Contains("BuiltInAssetInstaller.CreateCreatorWorkspace(destination)", StringComparison.Ordinal),
        "The published EXE must expose a testable creator-workspace export path.");
    Ensure(mainSource.Contains("PrepareCreatorWorkspace_Click", StringComparison.Ordinal) &&
           mainXaml.Contains("准备 Codex 创作工作区", StringComparison.Ordinal) &&
           mainXaml.Contains("请使用 $author-tessalume-theme", StringComparison.Ordinal) &&
           mainXaml.Contains("x:Name=\"CreatorPromptText\"", StringComparison.Ordinal) &&
           mainXaml.Contains("AutomationProperties.Name=\"复制提示词\"", StringComparison.Ordinal) &&
           mainXaml.Contains("FocusVisualStyle=\"{x:Null}\"", StringComparison.Ordinal) &&
           mainSource.Contains("Clipboard.SetText(CreatorPromptText.Text)", StringComparison.Ordinal) &&
           !mainSource.Contains("ShowProductMessage(\"复制", StringComparison.Ordinal),
        "The creator guide must show a larger complete prompt with one direct, non-modal copy action.");
    Ensure(skill.Contains("TESSALUME_CREATOR_WORKSPACE.md", StringComparison.Ordinal) &&
           skill.Contains("portable creator mode", StringComparison.Ordinal),
        "The authoring Skill must distinguish the portable workspace from the app repository.");
    Ensure(workspaceGuide.Contains("请为《鸣潮》的椿制作一套 Tessalume 主题", StringComparison.Ordinal) &&
           workspaceGuide.Contains("themes/<主题目录>", StringComparison.Ordinal),
        "The exported workspace must give a concrete one-sentence start and import handoff.");

    foreach (var relativePath in new[]
    {
        Path.Combine("creator-workspace", "AGENTS.md"),
        Path.Combine("creator-workspace", "TESSALUME_CREATOR_WORKSPACE.md"),
        Path.Combine("creator-workspace", "themes", "README.md"),
        Path.Combine(".agents", "skills", "author-tessalume-theme", "scripts", "scaffold_theme.py"),
        Path.Combine(".agents", "skills", "author-tessalume-theme", "scripts", "validate_theme_contract.py"),
        Path.Combine(".agents", "skills", "author-tessalume-theme", "scripts", "sync_template_geometry.py"),
    })
    {
        Ensure(File.Exists(Path.Combine(repositoryRoot, relativePath)),
            $"Creator workspace source is missing: {relativePath}");
    }
}

static async Task DiagnosticsRecoveryIsAvailableAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
    var xaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
    var mainSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml.cs"));
    var appSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "App.xaml.cs"));
    var installerSource = await File.ReadAllTextAsync(Path.Combine(
        appRoot,
        "Infrastructure",
        "BuiltInAssetInstaller.cs"));
    var logSource = await File.ReadAllTextAsync(Path.Combine(
        appRoot,
        "Infrastructure",
        "LocalLog.cs"));

    foreach (var marker in new[]
             {
                 "Content=\"打开日志目录\"",
                 "Content=\"恢复内置主题\"",
             })
    {
        Ensure(xaml.Contains(marker, StringComparison.Ordinal),
            $"The recovery surface is missing {marker}.");
    }

    Ensure(mainSource.Contains("RefreshDiagnosticsAsync", StringComparison.Ordinal) &&
           mainSource.Contains("RestoreBuiltInThemes_Click", StringComparison.Ordinal) &&
           !mainSource.Contains("CopyDiagnosticReport_Click", StringComparison.Ordinal) &&
           !xaml.Contains("复制诊断报告", StringComparison.Ordinal),
        "The diagnostics page must retain local status and recovery without clipboard report actions.");
    Ensure(appSource.Contains("LocalLog.Initialize(layout.DataDirectory)", StringComparison.Ordinal) &&
           appSource.IndexOf("LocalLog.Initialize(layout.DataDirectory)", StringComparison.Ordinal) <
           appSource.IndexOf("new MainWindow(layout)", StringComparison.Ordinal),
        "Local logging must be initialized before the main product surface starts.");
    Ensure(logSource.Contains("MaximumLogBytes", StringComparison.Ordinal) &&
           logSource.Contains("tessalume.previous.log", StringComparison.Ordinal) &&
           logSource.Contains("TakeLast", StringComparison.Ordinal),
        "Local logs must be bounded, rotated, and suitable for concise diagnostics.");
    Ensure(installerSource.Contains("RestoreDeletedThemes", StringComparison.Ordinal) &&
           installerSource.Contains("File.Delete(path)", StringComparison.Ordinal) &&
           installerSource.Contains("EnsureInstalled(layout)", StringComparison.Ordinal),
        "Built-in recovery must clear the deletion marker and reinstall embedded themes.");
}

static async Task LocalImporterCopiesPackageAsync()
{
    using var fixture = await ThemeFixture.CreateAsync();
    var library = Path.Combine(Path.GetTempPath(), $"tessalume-library-{Guid.NewGuid():N}");
    try
    {
        var package = await new ThemeImporter(new ThemePackageLoader()).ImportAsync(
            fixture.Root,
            library,
            overwrite: false);
        Ensure(package.Manifest.Id == "sample.theme", "Imported theme id did not match.");
        Ensure(File.Exists(Path.Combine(library, "sample.theme", "manifest.json")), "Imported manifest is missing.");
        Ensure(File.Exists(Path.Combine(library, "sample.theme", "assets", "hero.png")), "Imported asset is missing.");
    }
    finally
    {
        if (Directory.Exists(library))
        {
            Directory.Delete(library, recursive: true);
        }
    }
}

static async Task ZipThemeImportIsBoundedAsync()
{
    using var fixture = await ThemeFixture.CreateAsync();
    var token = Guid.NewGuid().ToString("N");
    var archivePath = Path.Combine(Path.GetTempPath(), $"tessalume-theme-{token}.zip");
    var maliciousPath = Path.Combine(Path.GetTempPath(), $"tessalume-malicious-{token}.zip");
    var library = Path.Combine(Path.GetTempPath(), $"tessalume-zip-library-{token}");
    string? extractedThemeDirectory = null;
    try
    {
        ZipFile.CreateFromDirectory(fixture.Root, archivePath);
        using (var extraction = await ThemeArchiveExtractor.ExtractAsync(archivePath))
        {
            extractedThemeDirectory = extraction.ThemeDirectory;
            Ensure(File.Exists(Path.Combine(extraction.ThemeDirectory, "manifest.json")),
                "The extracted ZIP theme manifest is missing.");
            var imported = await new ThemeImporter(new ThemePackageLoader()).ImportAsync(
                extraction.ThemeDirectory,
                library,
                overwrite: false);
            Ensure(imported.Manifest.Id == "sample.theme", "ZIP import changed the theme identity.");
        }
        Ensure(extractedThemeDirectory is not null && !Directory.Exists(extractedThemeDirectory),
            "Temporary ZIP extraction was not cleaned after import.");

        using (var archive = ZipFile.Open(maliciousPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escaped.txt");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("must not escape");
        }

        var traversalRejected = false;
        try
        {
            using var extraction = await ThemeArchiveExtractor.ExtractAsync(maliciousPath);
        }
        catch (InvalidDataException)
        {
            traversalRejected = true;
        }
        Ensure(traversalRejected, "ZIP path traversal was not rejected.");
    }
    finally
    {
        if (File.Exists(archivePath)) File.Delete(archivePath);
        if (File.Exists(maliciousPath)) File.Delete(maliciousPath);
        if (Directory.Exists(library)) Directory.Delete(library, recursive: true);
    }
}

static async Task BundledAdapterBuildsPayloadAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);
    var payload = await new ThemePayloadBuilder(new Dictionary<string, string>
    {
        [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            "theme-runtime-v2.js"),
    }).BuildAsync(package);
    Ensure(!payload.Contains("__DREAM_", StringComparison.Ordinal), "A dream placeholder remained in the payload.");
    Ensure(!payload.Contains("__TESSALUME_PAYLOAD_", StringComparison.Ordinal), "A Tessalume payload placeholder remained unresolved.");
    Ensure(package.Manifest.Config.TryGetValue("title", out var titleElement), "Theme title config is missing.");
    var title = titleElement.GetString();
    Ensure(!string.IsNullOrWhiteSpace(title), "Theme title config is empty.");
    var encodedTitle = JsonSerializer.Serialize(title).Trim('"');
    Ensure(package.IsAdvanced, "The representative theme should use the open advanced lifecycle.");
    Ensure(payload.Contains("registerTheme", StringComparison.Ordinal), "Advanced theme lifecycle is missing.");
    Ensure(payload.Contains(encodedTitle, StringComparison.Ordinal), "Theme config is missing.");
}

static async Task OpenAdvancedTemplateLoadsWithStableRevisionHashAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = (await new ThemePackageLoader().LoadAsync(
        Path.Combine(repositoryRoot, "examples"))).Package
        ?? throw new InvalidOperationException("Open advanced template could not be loaded.");
    var payload = await BuildPayloadAsync(repositoryRoot, package);
    var first = await ThemeFingerprintCalculator.CalculateAsync(package);
    var second = await ThemeFingerprintCalculator.CalculateAsync(package);
    var sharedTemplatePath = Path.Combine(
        repositoryRoot,
        "src",
        "Tessalume.App",
        "Compatibility",
        ThemePayloadBuilder.SharedTemplateStyleFileName);
    var effective = await ThemeFingerprintCalculator.CalculateEffectiveAsync(package, sharedTemplatePath);
    Ensure(package.IsAdvanced, "Advanced template must use the scripted lifecycle.");
    Ensure(package.Manifest.Id == "example.template-v1",
        "The root example package must be the Flagship Template 1.0 example.");
    Ensure(payload.Contains("registerTheme", StringComparison.Ordinal), "Advanced lifecycle is missing.");
    Ensure(first.Length == 64 && first == second, "Theme revision hash must be stable SHA-256.");
    Ensure(effective.Length == 64 && effective != first,
        "Shared themes must include the runtime template stylesheet in their effective revision hash.");
}

static async Task AdvancedImportKeepsScriptAndTracksChangesAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var library = Path.Combine(Path.GetTempPath(), $"tessalume-advanced-library-{Guid.NewGuid():N}");
    try
    {
        var imported = await new ThemeImporter(new ThemePackageLoader()).ImportAsync(
            Path.Combine(repositoryRoot, "examples"),
            library,
            overwrite: false);
        var scriptPath = imported.ScriptPath ?? throw new InvalidOperationException("Advanced script was not imported.");
        Ensure(File.Exists(scriptPath), "Advanced script was not imported.");
        var initialHash = await ThemeFingerprintCalculator.CalculateAsync(imported);

        await File.AppendAllTextAsync(scriptPath, "\n// fingerprint change");
        var changed = (await new ThemePackageLoader().LoadAsync(imported.RootDirectory)).Package
            ?? throw new InvalidOperationException("Changed advanced theme did not reload.");
        var changedHash = await ThemeFingerprintCalculator.CalculateAsync(changed);
        Ensure(!string.Equals(initialHash, changedHash, StringComparison.Ordinal),
            "Changing the imported script must update its runtime revision hash.");
    }
    finally
    {
        if (Directory.Exists(library)) Directory.Delete(library, recursive: true);
    }
}

static string FindRepositoryRoot()
{
    foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(startingPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException("Could not find the Tessalume repository root.");
}

static async Task<ThemePackage> LoadRepresentativePackageAsync(string repositoryRoot)
{
    var loader = new ThemePackageLoader();
    var themesRoot = Path.Combine(repositoryRoot, "themes");
    if (Directory.Exists(themesRoot))
    {
        var catalog = await new ThemeCatalog(loader).ScanAsync(themesRoot);
        var published = catalog.FirstOrDefault(item => item.Validation.IsValid && item.Package is not null);
        if (published?.Package is not null)
        {
            return published.Package;
        }
    }

    var templateRoot = Path.Combine(repositoryRoot, "examples");
    var template = await loader.LoadAsync(templateRoot);
    Ensure(template.Validation.IsValid, FormatIssues(template.Validation));
    return template.Package
        ?? throw new InvalidOperationException("No published theme or open theme template could be loaded.");
}

static async Task<int> ProbeRuntimeAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    if (targets.Count == 0)
    {
        Console.Error.WriteLine($"No Codex targets found on {port}.");
        return 2;
    }

    foreach (var target in targets.OrderBy(target =>
                 target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var result = await session.EvaluateAsync(
            "({ themeId: window.__TESSALUME_THEME_ID__ || null, installed: !!window.__TESSALUME_THEME_ID__, runtime: !!window.__TESSALUME_RUNTIME__, root: !!document.getElementById('tessalume-theme-root'), style: !!document.getElementById('tessalume-theme-style') || !!document.getElementById('codex-dream-skin-style'), chrome: !!document.getElementById('codex-dream-skin-chrome'), title: document.querySelector('.dream-brand b')?.textContent || document.querySelector('.example-theme-widget b')?.textContent || null, exampleMounted: document.documentElement.getAttribute('data-example-theme-mounted') })");
        Console.WriteLine(result);
    }

    return 0;
}

static async Task<int> RemoveRuntimeAsync(int port)
{
    await using var runtime = new ThemeRuntime(
        new LoopbackCdpDiscovery(),
        new ThemePayloadBuilder(new Dictionary<string, string>()));
    await runtime.RemoveAsync(port);
    Console.WriteLine("Theme removed.");
    return 0;
}

static async Task<int> ApplyRuntimeAsync(int port)
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);
    await using var runtime = new ThemeRuntime(
        new LoopbackCdpDiscovery(),
        new ThemePayloadBuilder(new Dictionary<string, string>
        {
            [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
                repositoryRoot,
                "src",
                "Tessalume.App",
                "Compatibility",
                "theme-runtime-v2.js"),
        }));
    await runtime.StartAsync(port, package);
    await runtime.StopAsync();
    Console.WriteLine("Theme applied.");
    return 0;
}

static async Task<int> ApplyPackageRuntimeAsync(int port, string packagePath)
{
    var repositoryRoot = FindRepositoryRoot();
    var package = (await new ThemePackageLoader().LoadAsync(packagePath)).Package
        ?? throw new InvalidOperationException("The requested theme package could not be loaded.");
    await using var runtime = new ThemeRuntime(
        new LoopbackCdpDiscovery(),
        new ThemePayloadBuilder(new Dictionary<string, string>
        {
            [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
                repositoryRoot,
                "src",
                "Tessalume.App",
                "Compatibility",
                "theme-runtime-v2.js"),
        }));
    await runtime.StartAsync(port, package);
    await runtime.StopAsync();
    Console.WriteLine($"Theme applied: {package.Manifest.Id}");
    return 0;
}

static async Task<string> BuildPayloadAsync(string repositoryRoot, ThemePackage package) =>
    await new ThemePayloadBuilder(new Dictionary<string, string>
    {
        [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            "theme-runtime-v2.js"),
    }).BuildAsync(package);

static async Task<int> ProbeThemeModesAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    foreach (var target in targets)
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var installed = await session.EvaluateAsync(
            "!!window.__TESSALUME_RUNTIME__ && document.documentElement.classList.contains('tessalume-theme-active')");
        if (installed.ValueKind != JsonValueKind.True)
        {
            continue;
        }

        var result = await session.EvaluateAsync(
            """
            (() => {
              const root = document.documentElement;
              const wasDark = root.classList.contains('electron-dark');
              const sample = () => ({
                colorScheme: getComputedStyle(root).colorScheme,
                textColor: getComputedStyle(document.body).color,
                composerBackground: getComputedStyle(document.querySelector('.composer-surface-chrome') || document.body).backgroundColor
              });
              root.classList.remove('electron-dark');
              const light = sample();
              root.classList.add('electron-dark');
              const dark = sample();
              root.classList.toggle('electron-dark', wasDark);
              return { light, dark, different: JSON.stringify(light) !== JSON.stringify(dark) };
            })()
            """);
        Console.WriteLine(result);
        return result.GetProperty("different").GetBoolean() ? 0 : 3;
    }

    Console.Error.WriteLine("No themed Codex target found.");
    return 2;
}

static async Task<int> ToggleColorSchemeAsync(int port)
{
    await using var runtime = new ThemeRuntime(
        new LoopbackCdpDiscovery(),
        new ThemePayloadBuilder(new Dictionary<string, string>()));
    var dark = await runtime.ToggleColorSchemeAsync(port);
    Console.WriteLine(dark ? "dark" : "light");
    return 0;
}

static async Task<int> ProbeAppearanceStateAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    foreach (var target in targets.OrderBy(target =>
                 target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var result = await session.EvaluateAsync(
            """
            (() => {
              const root = document.documentElement;
              const interesting = ([key]) => /theme|appearance|color|dark|light|scheme/i.test(key);
              const storage = store => Object.entries(store).filter(interesting);
              return {
                isMain: !!document.querySelector('main') &&
                  !root.classList.contains('compact-window') &&
                  !new URLSearchParams(location.search).has('initialRoute'),
                url: location.href,
                rootClass: root.className,
                rootAttributes: Object.fromEntries([...root.attributes].map(x => [x.name, x.value])),
                bodyAttributes: Object.fromEntries([...document.body.attributes].map(x => [x.name, x.value])),
                colorScheme: getComputedStyle(root).colorScheme,
                prefersDark: matchMedia('(prefers-color-scheme: dark)').matches,
                localStorage: storage(localStorage),
                sessionStorage: storage(sessionStorage),
                globalKeys: Object.keys(window).filter(key => /theme|appearance|color|dark|light|scheme/i.test(key)).slice(0, 100)
              };
            })()
            """);
        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("isMain", out var isMain) && isMain.GetBoolean())
        {
            Console.WriteLine(result);
            return 0;
        }
    }

    Console.Error.WriteLine("No main Codex target found.");
    return 2;
}

static async Task<int> ProbeAppearanceBundlesAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    foreach (var target in targets)
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var result = await session.EvaluateAsync(
            """
            (async () => {
              if (!document.querySelector('main')) return null;
              const urls = [...new Set([
                ...[...document.scripts].map(x => x.src),
                ...performance.getEntriesByType('resource').map(x => x.name)
              ].filter(url => /\.m?js(?:\?|$)/i.test(url)))];
              const needles = ['electron-dark', 'color-scheme', 'appearance', 'setTheme', 'themePreference'];
              const matches = [];
              for (const url of urls) {
                let text;
                try { text = await fetch(url).then(response => response.text()); } catch { continue; }
                for (const needle of needles) {
                  let offset = 0;
                  for (let count = 0; count < 4; count++) {
                    const index = text.indexOf(needle, offset);
                    if (index < 0) break;
                    matches.push({ url, needle, index, snippet: text.slice(Math.max(0,index-500),index+900) });
                    offset = index + needle.length;
                  }
                }
              }
              return matches.slice(0, 60);
            })()
            """);
        if (result.ValueKind == JsonValueKind.Array)
        {
            Console.WriteLine(result);
            return 0;
        }
    }

    Console.Error.WriteLine("No main Codex target found.");
    return 2;
}

static async Task<int> ProbeQueryClientsAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    foreach (var target in targets)
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var result = await session.EvaluateAsync(
            """
            (() => {
              if (!document.querySelector('main')) return null;
              const rootNode = document.querySelector('#root') || document.body;
              const fiberKey = Object.keys(rootNode).find(key => key.startsWith('__reactContainer$') || key.startsWith('__reactFiber$'));
              let root = fiberKey ? rootNode[fiberKey] : null;
              const seenFibers = new Set();
              const seenObjects = new WeakSet();
              const clients = [];
              const inspect = (value, path, depth = 0) => {
                if (!value || (typeof value !== 'object' && typeof value !== 'function') || seenObjects.has(value) || depth > 5) return;
                seenObjects.add(value);
                try {
                  if (typeof value.getQueryCache === 'function' && typeof value.invalidateQueries === 'function') {
                    const queries = value.getQueryCache().getAll().map(q => ({ hash: q.queryHash, key: q.queryKey, state: q.state?.status }));
                    clients.push({ path, queries: queries.filter(q => JSON.stringify(q).includes('settings')).slice(0, 20), total: queries.length });
                    return;
                  }
                } catch {}
                let entries;
                try { entries = Object.entries(value); } catch { return; }
                for (const [key, child] of entries.slice(0, 80)) {
                  if (/^(return|child|sibling|stateNode|alternate|_owner)$/.test(key)) continue;
                  inspect(child, `${path}.${key}`, depth + 1);
                }
              };
              const queue = root ? [root] : [];
              while (queue.length && seenFibers.size < 12000 && clients.length < 10) {
                const fiber = queue.shift();
                if (!fiber || seenFibers.has(fiber)) continue;
                seenFibers.add(fiber);
                inspect(fiber.memoizedProps, 'fiber.memoizedProps');
                inspect(fiber.memoizedState, 'fiber.memoizedState');
                inspect(fiber.dependencies, 'fiber.dependencies');
                if (fiber.child) queue.push(fiber.child);
                if (fiber.sibling) queue.push(fiber.sibling);
              }
              return { fiberKey, fibers: seenFibers.size, clients };
            })()
            """);
        if (result.ValueKind == JsonValueKind.Object)
        {
            Console.WriteLine(result);
            return 0;
        }
    }

    Console.Error.WriteLine("No main Codex target found.");
    return 2;
}

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string FormatIssues(ThemeValidationResult validation) =>
    string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));

file sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}

file sealed class ThemeFixture : IDisposable
{
    private ThemeFixture(string root) => Root = root;

    public string Root { get; }

    public static async Task<ThemeFixture> CreateAsync(
        string? root = null,
        string cssPath = "theme.css",
        string css = ":root { --accent: #ff79c6; }")
    {
        root ??= Path.Combine(Path.GetTempPath(), $"tessalume-theme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "assets"));

        if (!cssPath.StartsWith("..", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(Path.Combine(root, cssPath), css);
        }
        await File.WriteAllTextAsync(
            Path.Combine(root, "theme.js"),
            "registerTheme({ mount() {}, unmount() {} });");

        await File.WriteAllBytesAsync(Path.Combine(root, "assets", "hero.png"), [0x89, 0x50, 0x4e, 0x47]);
        var manifest = new
        {
            schemaVersion = 2,
            id = "sample.theme",
            name = "Sample Theme",
            version = "1.0.0",
            author = "Tests",
            engineVersion = 2,
            type = "advanced",
            capabilities = new { light = true, dark = true },
            entryPoints = new { css = cssPath, script = "theme.js" },
            assets = new { hero = "assets/hero.png" },
        };
        await File.WriteAllTextAsync(
            Path.Combine(root, ThemePackageLoader.ManifestFileName),
            JsonSerializer.Serialize(manifest));
        return new ThemeFixture(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
