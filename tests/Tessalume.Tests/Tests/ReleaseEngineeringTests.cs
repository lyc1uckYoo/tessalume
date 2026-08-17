internal static partial class TestSuite
{
    static async Task BuildScriptLaunchesPublishedExecutableAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "一键构建EXE.ps1"));
        Ensure(source.Contains("[switch]$NoLaunch", StringComparison.Ordinal) &&
               source.Contains("[switch]$FullValidation", StringComparison.Ordinal) &&
               source.Contains("'--build'", StringComparison.Ordinal) &&
               source.Contains("'--full'", StringComparison.Ordinal) &&
               source.Contains("if (-not $NoLaunch)", StringComparison.Ordinal) &&
               source.Contains("Start-Process -FilePath $finalExe -WorkingDirectory $output", StringComparison.Ordinal),
            "The one-click build must separate daily and release validation, launch by default, and retain an explicit opt-out.");
    }

    static async Task ReleaseReadinessAssetsAreDocumentedAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var buildScript = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "一键构建EXE.ps1"));
        var releaseCandidateScript = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "tools",
            "Test-ReleaseCandidate.ps1"));
        var readme = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "README.md"));
        var securityPath = Path.Combine(repositoryRoot, "SECURITY.md");
        var licensePath = Path.Combine(repositoryRoot, "LICENSE");
        var changelogPath = Path.Combine(repositoryRoot, "CHANGELOG.md");
        var issueTemplatePath = Path.Combine(repositoryRoot, ".github", "ISSUE_TEMPLATE", "bug-report.yml");
        var license = await File.ReadAllTextAsync(licensePath);
        var changelog = await File.ReadAllTextAsync(changelogPath);
        var security = await File.ReadAllTextAsync(securityPath);
        var publicScreenshots = Path.Combine(repositoryRoot, ".github", "assets", "screenshots");

        Ensure(buildScript.Contains("Get-FileHash -LiteralPath $finalExe -Algorithm SHA256", StringComparison.Ordinal) &&
               buildScript.Contains("SHA256SUMS.txt", StringComparison.Ordinal) &&
               releaseCandidateScript.Contains("Complete release build failed", StringComparison.Ordinal),
            "The release build must create a checksum and propagate complete-build failures.");
        Ensure(File.Exists(securityPath) && File.Exists(issueTemplatePath) && File.Exists(changelogPath) &&
               changelog.Contains("## 2.0.0", StringComparison.Ordinal) &&
               license.Contains("MIT License", StringComparison.Ordinal) &&
               license.Contains("Permission is hereby granted", StringComparison.Ordinal),
            "Public testing requires an MIT license, security guidance, a structured bug form, and a public changelog.");
        Ensure(readme.Contains("issues/new?template=bug-report.yml", StringComparison.Ordinal) &&
               readme.Contains("Microsoft Defender SmartScreen", StringComparison.Ordinal) &&
               readme.Contains("SHA256SUMS.txt", StringComparison.Ordinal) &&
               readme.Contains("把 Codex Desktop 变成属于你的主题工作空间", StringComparison.Ordinal) &&
               readme.Contains("一个软件，完成整套主题体验", StringComparison.Ordinal) &&
               readme.Contains("个性化不再需要手改 CSS", StringComparison.Ordinal) &&
               readme.Contains("让 Codex 帮你制作自己的皮肤", StringComparison.Ordinal) &&
               readme.Contains("更新不会重置你的主题和设置", StringComparison.Ordinal) &&
               readme.Contains("tessalume-personalization-light.png", StringComparison.Ordinal) &&
               readme.Contains("tessalume-creator.png", StringComparison.Ordinal) &&
               !readme.Contains("## Tessalume 1.4.1", StringComparison.Ordinal) &&
               readme.Split('\n').Length <= 160 &&
               readme.Contains("[MIT License](LICENSE)", StringComparison.Ordinal) &&
               security.Contains("最新的 `2.0.x`", StringComparison.Ordinal) &&
               security.Contains("备份 ZIP", StringComparison.Ordinal),
            "The public README must stay product-focused while exposing download, safety, feedback, and licensing.");
        foreach (var screenshot in new[]
                 {
                     "tessalume-light.png",
                     "tessalume-dark.png",
                     "tessalume-personalization-light.png",
                     "tessalume-personalization-dark.png",
                     "tessalume-creator.png",
                 })
        {
            Ensure(File.Exists(Path.Combine(publicScreenshots, screenshot)),
                $"The 2.0 public product screenshot is missing: {screenshot}.");
        }
    }

    static async Task GitHubAutomationSeparatesApplicationAndCompatibilityReleasesAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflows = Path.Combine(repositoryRoot, ".github", "workflows");
        var ci = await File.ReadAllTextAsync(Path.Combine(workflows, "ci.yml"));
        var release = await File.ReadAllTextAsync(Path.Combine(workflows, "release.yml"));
        var compatibility = await File.ReadAllTextAsync(Path.Combine(
            workflows,
            "compatibility-release.yml"));
        var packScript = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "tools",
            "New-CompatibilityPack.ps1"));
        var compatibilityReadme = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            "README.md"));
        var notesScript = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "tools",
            "Get-ReleaseNotes.ps1"));

        Ensure(ci.Contains("一键构建EXE.ps1", StringComparison.Ordinal) &&
               ci.Contains("-NoLaunch", StringComparison.Ordinal) &&
               ci.Contains("-FullValidation", StringComparison.Ordinal) &&
               ci.Contains("actions/upload-artifact@v4", StringComparison.Ordinal),
            "CI must execute the same complete release build used locally and retain its verified artifacts.");
        Ensure(release.Contains("tags:", StringComparison.Ordinal) &&
               release.Contains("'v*.*.*'", StringComparison.Ordinal) &&
               release.Contains("-FullValidation", StringComparison.Ordinal) &&
               release.Contains("does not match project version", StringComparison.Ordinal) &&
               release.Contains("Get-ReleaseNotes.ps1", StringComparison.Ordinal) &&
               release.Contains("gh release create", StringComparison.Ordinal) &&
               release.Contains("--latest", StringComparison.Ordinal),
            "Application tags must validate the project version and changelog before publishing a latest GitHub Release.");
        Ensure(compatibility.Contains("'compat-v*.*.*'", StringComparison.Ordinal) &&
               compatibility.Contains("New-CompatibilityPack.ps1", StringComparison.Ordinal) &&
               compatibility.Contains("--latest=false", StringComparison.Ordinal) &&
               compatibility.Contains("Tessalume-Compatibility.zip", StringComparison.Ordinal),
            "Compatibility tags must publish only the bounded small package and must never replace the latest app release.");
        Ensure(packScript.Contains("minimumAppVersion", StringComparison.Ordinal) &&
               packScript.Contains("Tessalume.App.csproj", StringComparison.Ordinal) &&
               packScript.Contains("does not match source profileVersion", StringComparison.Ordinal) &&
               compatibilityReadme.Contains("New-CompatibilityPack.ps1 -Version 3.0.4", StringComparison.Ordinal) &&
               notesScript.Contains("CHANGELOG.md does not contain", StringComparison.Ordinal),
            "Release scripts must derive compatibility requirements from source, reject version drift, and reject missing release notes.");
    }

    static async Task CompatibilityPackBuildIsDeterministicAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "tools", "New-CompatibilityPack.ps1");
        var testRoot = Path.Combine(Path.GetTempPath(), $"tessalume-compat-pack-{Guid.NewGuid():N}");
        var firstOutput = Path.Combine(testRoot, "first");
        var secondOutput = Path.Combine(testRoot, "second");
        var mismatchOutput = Path.Combine(testRoot, "mismatch");

        try
        {
            var mismatch = await RunPackBuildAsync(mismatchOutput, "3.0.2");
            Ensure(mismatch.ExitCode != 0 &&
                   mismatch.Output.Contains(
                       "does not match source profileVersion '3.0.4'",
                       StringComparison.Ordinal) &&
                   !File.Exists(Path.Combine(
                       mismatchOutput,
                       "Tessalume-Compatibility.zip")),
                "The compatibility builder must clearly reject a version that differs from the source profileVersion.");
            await BuildPackAsync(firstOutput);
            await BuildPackAsync(secondOutput);

            var firstArchive = Path.Combine(firstOutput, "Tessalume-Compatibility.zip");
            var secondArchive = Path.Combine(secondOutput, "Tessalume-Compatibility.zip");
            var firstHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(firstArchive)));
            var secondHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(secondArchive)));
            Ensure(firstHash == secondHash,
                "Identical compatibility sources must produce a reproducible ZIP and SHA-256.");

            using var archive = System.IO.Compression.ZipFile.OpenRead(firstArchive);
            var expectedEntries = new[]
            {
                "compatibility-pack.json",
                "compatibility-profile-v3.json",
                "theme-runtime-v2.js",
            };
            Ensure(archive.Entries.Select(entry => entry.FullName).SequenceEqual(expectedEntries) &&
                   archive.Entries.All(entry =>
                       entry.LastWriteTime.Year == 2000 &&
                       entry.LastWriteTime.Month == 1 &&
                       entry.LastWriteTime.Day == 1 &&
                       entry.LastWriteTime.Hour == 0 &&
                       entry.LastWriteTime.Minute == 0 &&
                       entry.LastWriteTime.Second == 0),
                "The compatibility ZIP must retain its fixed contract order and timestamp.");
            using var profileStream = archive.GetEntry("compatibility-profile-v3.json")!.Open();
            using var manifestStream = archive.GetEntry("compatibility-pack.json")!.Open();
            using var profileDocument = JsonDocument.Parse(profileStream);
            using var manifestDocument = JsonDocument.Parse(manifestStream);
            Ensure(profileDocument.RootElement.GetProperty("profileVersion").GetString() == "3.0.4" &&
                   manifestDocument.RootElement.GetProperty("packVersion").GetString() == "3.0.4" &&
                   manifestDocument.RootElement.GetProperty("runtimeContractVersion").GetInt32() == 4,
                "The compatibility archive must preserve the source profile version and runtime contract without rewriting them.");
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }

        async Task BuildPackAsync(string outputDirectory)
        {
            var result = await RunPackBuildAsync(outputDirectory, "3.0.4");
            Ensure(result.ExitCode == 0,
                $"Compatibility pack build failed. {result.Output}".Trim());
        }

        async Task<(int ExitCode, string Output)> RunPackBuildAsync(
            string outputDirectory,
            string version)
        {
            var powerShell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            var startInfo = new System.Diagnostics.ProcessStartInfo(powerShell)
            {
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[]
                     {
                         "-NoProfile",
                         "-NonInteractive",
                         "-ExecutionPolicy",
                         "Bypass",
                         "-File",
                         scriptPath,
                         "-Version",
                         version,
                         "-MinimumAppVersion",
                         "2.0.0",
                         "-OutputDirectory",
                         outputDirectory,
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }
            startInfo.Environment["PSModulePath"] = string.Empty;

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the compatibility pack builder.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;
            return (process.ExitCode, $"{output} {error}".Trim());
        }
    }



    static async Task SourceLayoutKeepsFeatureBoundariesAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var expectedMainWindowParts = new[]
        {
            "MainWindow.Navigation.cs",
            "MainWindow.QuickSwitch.cs",
            "MainWindow.ThemeRuntime.cs",
            "MainWindow.Presentation.cs",
            "MainWindow.ThemeLibrary.cs",
            "MainWindow.ThemeLibraryExperience.cs",
            "MainWindow.Settings.cs",
            "MainWindow.Creator.cs",
            "MainWindow.CreatorAcceptance.cs",
            "MainWindow.Updates.cs",
            "MainWindow.Diagnostics.cs",
            "MainWindow.Backup.cs",
        };
        foreach (var fileName in expectedMainWindowParts)
        {
            Ensure(File.Exists(Path.Combine(appRoot, fileName)),
                $"The MainWindow feature boundary is missing {fileName}.");
        }

        var mainRoot = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml.cs"));
        foreach (var featureDeclaration in new[]
                 {
                     "private async Task ReloadThemesAsync",
                     "private async Task CheckForUpdatesAsync",
                     "private async Task<bool> ApplyThemeAsync",
                     "private void ShowProductMessage",
                 })
        {
            Ensure(!mainRoot.Contains(featureDeclaration, StringComparison.Ordinal),
                $"MainWindow.xaml.cs must not absorb the {featureDeclaration} feature again.");
        }

        var quickRoot = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "ThemeQuickSwitchWindow.xaml.cs"));
        Ensure(!quickRoot.Contains("RefreshUsageAsync", StringComparison.Ordinal) &&
               !quickRoot.Contains("ApplyRelativeAsync", StringComparison.Ordinal),
            "The quick-switch root must retain only state, construction, refresh, and shell theming.");
        Ensure(File.Exists(Path.Combine(appRoot, "Styles", "MainWindowResources.xaml")),
            "MainWindow styles must remain in their dedicated resource dictionary.");
        foreach (var fileName in new[]
                 {
                     "CreatorCenterView.xaml",
                     "CreatorCenterView.xaml.cs",
                 })
        {
            Ensure(File.Exists(Path.Combine(appRoot, "Creator", fileName)),
                $"The Creator Center boundary is missing {fileName}.");
        }
        var creatorShell = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.Creator.cs"));
        Ensure(!creatorShell.Contains("OpenFolderDialog", StringComparison.Ordinal) &&
               !creatorShell.Contains("ThemeProjectScanner", StringComparison.Ordinal) &&
               !creatorShell.Contains("ThemeArchiveWriter", StringComparison.Ordinal),
            "MainWindow.Creator must remain a navigation shell instead of reclaiming project logic.");

        var creatorRoot = Path.Combine(appRoot, "Creator");
        foreach (var relativePath in new[]
                 {
                     Path.Combine("Application", "Contracts", "ICreatorProjectInspectionService.cs"),
                     Path.Combine("Application", "Contracts", "ICreatorProjectExportService.cs"),
                     Path.Combine("Application", "Contracts", "ICreatorRuntimeGateway.cs"),
                     Path.Combine("Application", "Contracts", "ICreatorAcceptanceService.cs"),
                     Path.Combine("Application", "Services", "CreatorWorkflowEvaluator.cs"),
                     Path.Combine("Application", "Services", "CreatorAcceptanceService.cs"),
                     Path.Combine("Application", "Prompting", "CreatorPromptComposer.cs"),
                     Path.Combine("Application", "Prompting", "CreatorRepairPromptComposer.cs"),
                     Path.Combine("Domain", "CreatorWorkflow.cs"),
                     Path.Combine("Domain", "CreatorAcceptance.cs"),
                     Path.Combine("Infrastructure", "Persistence", "CreatorWorkspaceStore.cs"),
                     Path.Combine("Infrastructure", "Runtime", "CreatorRuntimeBridge.cs"),
                     Path.Combine("Infrastructure", "Watching", "ThemeProjectWatcher.cs"),
                     Path.Combine("Infrastructure", "Workspaces", "CreatorWorkspaceProvisioner.cs"),
                     Path.Combine("Presentation", "Navigation", "CreatorCenterRoute.cs"),
                     Path.Combine("Presentation", "Pages", "CreatorWorkspacePage.xaml"),
                     Path.Combine("Presentation", "Pages", "CreatorWorkflowPage.xaml"),
                     Path.Combine("Presentation", "Pages", "CreatorInspectionPage.xaml"),
                     Path.Combine("Presentation", "Pages", "CreatorAcceptancePage.xaml"),
                     Path.Combine("Presentation", "Pages", "CreatorReleasePage.xaml"),
                     Path.Combine("Presentation", "ViewModels", "CreatorCenterViewModel.Workspaces.cs"),
                     Path.Combine("Presentation", "ViewModels", "CreatorCenterViewModel.cs"),
                     Path.Combine("Presentation", "ViewModels", "CreatorProjectViewModels.cs"),
                     Path.Combine("Presentation", "ViewModels", "CreatorCenterViewModel.ProjectInspection.cs"),
                     Path.Combine("Presentation", "ViewModels", "CreatorCenterViewModel.RuntimeSession.cs"),
                     Path.Combine("Presentation", "ViewModels", "CreatorCenterViewModel.Acceptance.cs"),
                 })
        {
            Ensure(File.Exists(Path.Combine(creatorRoot, relativePath)),
                $"The Creator Center modular boundary is missing {relativePath}.");
        }

        var compatibilityRoot = Path.Combine(appRoot, "Compatibility");
        var runtimeRoot = Path.Combine(compatibilityRoot, "Runtime");
        var runtimeFragments = new (string FileName, int MaximumLines)[]
        {
            ("00-bootstrap.js", 400),
            ("05-artwork-composition.js", 400),
            ("06-artwork-settings.js", 400),
            ("10-page-recognition.js", 400),
            ("15-display-preferences.js", 400),
            ("20-adaptive-layout.js", 400),
            ("30-surface-decoration.js", 400),
            ("40-cleanup-recovery.js", 400),
        };
        Ensure(!File.Exists(Path.Combine(compatibilityRoot, "theme-runtime-v2.js")) &&
               File.Exists(Path.Combine(runtimeRoot, "runtime-bundle.json")),
            "Compatibility runtime source must be modular and assembled only at build/install boundaries.");
        foreach (var (fragment, maximumLines) in runtimeFragments)
        {
            var fragmentPath = Path.Combine(runtimeRoot, fragment);
            Ensure(File.Exists(fragmentPath), $"Compatibility runtime fragment is missing: {fragment}.");
            var lines = await File.ReadAllLinesAsync(fragmentPath);
            Ensure(lines.Length <= maximumLines &&
                   lines.FirstOrDefault()?.Contains("TESSALUME_RUNTIME_FRAGMENT", StringComparison.Ordinal) == true,
                $"Compatibility runtime fragment must remain focused and self-identifying: {fragment}.");
        }
        var composedRuntime = CompatibilityRuntimeComposer.ComposeSource(compatibilityRoot);
        Ensure(composedRuntime.Contains("mountCanonicalTheme", StringComparison.Ordinal) &&
               composedRuntime.Contains("syncDisplayPreferences", StringComparison.Ordinal) &&
               composedRuntime.Contains("syncAdaptiveVisibility", StringComparison.Ordinal) &&
               composedRuntime.Contains("decorateSharedSurfaces", StringComparison.Ordinal) &&
               composedRuntime.TrimEnd().EndsWith("})()", StringComparison.Ordinal),
            "The modular compatibility runtime must compose into the complete contract-v4 payload.");
        var visualSettingsDefinition = composedRuntime.IndexOf(
            "const setVisualSettings = async",
            StringComparison.Ordinal);
        var placementSyncDefinition = composedRuntime.IndexOf(
            "const synchronizeVisualPlacements = async",
            StringComparison.Ordinal);
        var initialVisualSettingsApply = composedRuntime.IndexOf(
            "await setVisualSettings(stagedVisualSettings",
            StringComparison.Ordinal);
        Ensure(visualSettingsDefinition >= 0 &&
               placementSyncDefinition >= 0 &&
               initialVisualSettingsApply > visualSettingsDefinition &&
               initialVisualSettingsApply > placementSyncDefinition,
            "The composed runtime must define absolute composition helpers before first-use initialization.");
        foreach (var fileName in new[] { "ThemeRuntimeAcceptanceProbe.cs", "ThemeRuntimeAcceptanceSnapshot.cs" })
        {
            var sourcePath = Path.Combine(repositoryRoot, "src", "Tessalume.Core", "Runtime", fileName);
            Ensure(File.Exists(sourcePath) && (await File.ReadAllLinesAsync(sourcePath)).Length <= 240,
                $"Runtime acceptance must stay in its focused service boundary: {fileName}.");
        }
        var acceptanceProbeSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.Core",
            "Runtime",
            "ThemeRuntimeAcceptanceProbe.cs"));
        Ensure(acceptanceProbeSource.Contains("'reduced'", StringComparison.Ordinal) &&
               !acceptanceProbeSource.Contains("'compact'", StringComparison.Ordinal),
            "Runtime acceptance must use the same full/reduced/minimal layout vocabulary as the compatibility engine.");

        foreach (var sourcePath in Directory.EnumerateFiles(
                     Path.Combine(creatorRoot, "Presentation"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var lineCount = (await File.ReadAllLinesAsync(sourcePath)).Length;
            Ensure(lineCount <= 320,
                $"Creator presentation source must stay focused: {Path.GetFileName(sourcePath)} has {lineCount} lines.");
        }

        var scannerRoot = Path.Combine(repositoryRoot, "src", "Tessalume.Core", "Creator");
        foreach (var (relativePath, maximumLines) in new[]
                 {
                     ("ThemeProjectScanner.cs", 320),
                     (Path.Combine("Inspection", "ThemeProjectScanner.Workspaces.cs"), 320),
                     (Path.Combine("Inspection", "ThemeProjectScanner.Contracts.cs"), 500),
                     (Path.Combine("Inspection", "ThemeProjectScanner.Mapping.cs"), 320),
                 })
        {
            var sourcePath = Path.Combine(scannerRoot, relativePath);
            Ensure(File.Exists(sourcePath), $"The scanner boundary is missing {relativePath}.");
            var lineCount = (await File.ReadAllLinesAsync(sourcePath)).Length;
            Ensure(lineCount <= maximumLines,
                $"Scanner source must stay focused: {relativePath} has {lineCount} lines.");
        }
        Ensure(!File.Exists(Path.Combine(appRoot, "DiagnosticsWindow.xaml")) &&
               !File.Exists(Path.Combine(appRoot, "DiagnosticsWindow.xaml.cs")),
            "The obsolete duplicate diagnostics window must not return.");
        var aboutFeatureRoot = Path.Combine(appRoot, "Features", "About");
        foreach (var fileName in new[]
                 {
                     "AboutView.xaml",
                     "AboutView.xaml.cs",
                     "AboutState.cs",
                     "AboutDataService.cs",
                     "AboutUpdateService.cs",
                 })
        {
            Ensure(File.Exists(Path.Combine(aboutFeatureRoot, fileName)),
                $"The About feature slice is missing {fileName}.");
        }
        var diagnosticsFeatureRoot = Path.Combine(appRoot, "Features", "Diagnostics");
        foreach (var fileName in new[]
                 {
                     "DiagnosticsView.xaml",
                     "DiagnosticsView.xaml.cs",
                     "DiagnosticsSnapshot.cs",
                     "DiagnosticsInspectionService.cs",
                     "CompatibilityHealthService.cs",
                 })
        {
            Ensure(File.Exists(Path.Combine(diagnosticsFeatureRoot, fileName)),
                $"The diagnostics feature slice is missing {fileName}.");
        }
        var personalizationFeatureRoot = Path.Combine(appRoot, "Features", "Personalization");
        foreach (var fileName in new[]
                 {
                     "PersonalImageStore.cs",
                     "DisplayPreferencesView.xaml",
                     "DisplayPreferencesView.xaml.cs",
                     "DisplaySettingsView.xaml",
                     "DisplaySettingsView.xaml.cs",
                 })
        {
            Ensure(File.Exists(Path.Combine(personalizationFeatureRoot, fileName)),
                $"The personalization feature slice is missing {fileName}.");
        }
        var personalizationShellRoot = Path.Combine(appRoot, "Shell", "Personalization");
        foreach (var (fileName, maximumLines) in new[]
                 {
                     ("MainWindow.PersonalizationNavigation.cs", 220),
                     ("MainWindow.PersonalizationPreview.cs", 220),
                     ("MainWindow.PersonalizationPresentation.cs", 220),
                     ("MainWindow.DisplayPreferences.cs", 220),
                     ("MainWindow.ArtworkWorkbench.cs", 360),
                     ("MainWindow.ArtworkWorkbenchDialogs.cs", 240),
                 })
        {
            var path = Path.Combine(personalizationShellRoot, fileName);
            Ensure(File.Exists(path) && (await File.ReadAllLinesAsync(path)).Length < maximumLines,
                $"The personalization shell boundary is missing or oversized: {fileName}.");
        }
        var artworkWorkbenchRoot = Path.Combine(personalizationFeatureRoot, "ArtworkWorkbench");
        foreach (var relativePath in new[]
                 {
                     Path.Combine("Domain", "ArtworkTypes.cs"),
                     Path.Combine("Domain", "ArtworkSettingsAccessor.cs"),
                     Path.Combine("Domain", "ArtworkSettingsReducer.cs"),
                     Path.Combine("Application", "ArtworkPlacementMapper.cs"),
                     Path.Combine("Application", "ArtworkSurfaceMetricsProbeGate.cs"),
                     Path.Combine("Application", "ArtworkHistoryService.cs"),
                     Path.Combine("Application", "ArtworkWorkbenchSession.cs"),
                     Path.Combine("Infrastructure", "ArtworkImageSourceResolver.cs"),
                     Path.Combine("Infrastructure", "ArtworkThemeDefaultsStore.cs"),
                     Path.Combine("Infrastructure", "ArtworkPreviewImageCache.cs"),
                     Path.Combine("Infrastructure", "ArtworkWorkbenchFileDialogs.cs"),
                     Path.Combine("Presentation", "ArtworkCanvasControl.xaml"),
                     Path.Combine("Presentation", "ArtworkInspectorView.xaml"),
                     Path.Combine("Presentation", "ArtworkWorkbenchView.xaml"),
                 })
        {
            Ensure(File.Exists(Path.Combine(artworkWorkbenchRoot, relativePath)),
                $"Artwork Workbench 3.0 modular boundary is missing {relativePath}.");
        }
        foreach (var sourcePath in Directory.EnumerateFiles(
                     artworkWorkbenchRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var lineCount = (await File.ReadAllLinesAsync(sourcePath)).Length;
            Ensure(lineCount <= 560,
                $"Artwork Workbench source must stay focused: {Path.GetFileName(sourcePath)} has {lineCount} lines.");
        }
        var artworkShell = await File.ReadAllTextAsync(Path.Combine(
            personalizationShellRoot,
            "MainWindow.ArtworkWorkbench.cs"));
        Ensure(!artworkShell.Contains("ArtworkCanvasCssMapper", StringComparison.Ordinal) &&
               !artworkShell.Contains("ArtworkHistoryService", StringComparison.Ordinal) &&
               !artworkShell.Contains("ArtworkPreviewImageCache", StringComparison.Ordinal),
            "The MainWindow artwork shell must only coordinate dialogs, persistence, and runtime application.");
        Ensure(File.Exists(Path.Combine(
                   repositoryRoot,
                   "src",
                   "Tessalume.Core",
                   "Backup",
                   "PortableBackupService.cs")),
            "Compatibility presentation and portable backup algorithms must remain in dedicated feature boundaries.");
        foreach (var (relativePath, maximumLines) in new[]
                 {
                     (Path.Combine("src", "Tessalume.App", "Infrastructure", "UpdateBootstrapper.cs"), 450),
                     (Path.Combine("src", "Tessalume.App", "Infrastructure", "UpdateHelperRuntime.cs"), 450),
                     (Path.Combine("src", "Tessalume.Core", "Runtime", "ThemeRuntime.cs"), 650),
                     (Path.Combine("src", "Tessalume.Core", "Runtime", "ThemeRuntime.Payload.cs"), 200),
                     (Path.Combine("src", "Tessalume.Core", "Backup", "PortableBackupService.cs"), 800),
                     (Path.Combine("src", "Tessalume.Core", "Backup", "PortableBackupService.Internals.cs"), 200),
                 })
        {
            var path = Path.Combine(repositoryRoot, relativePath);
            Ensure(File.Exists(path) && (await File.ReadAllLinesAsync(path)).Length <= maximumLines,
                $"Infrastructure source must stay focused: {relativePath}.");
        }

        var mainXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
        var diagnosticsView = await File.ReadAllTextAsync(Path.Combine(
            diagnosticsFeatureRoot,
            "DiagnosticsView.xaml"));
        var aboutView = await File.ReadAllTextAsync(Path.Combine(aboutFeatureRoot, "AboutView.xaml"));
        var updateShell = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.Updates.cs"));
        var recoveryShellPath = Path.Combine(
            appRoot,
            "Shell",
            "About",
            "MainWindow.AboutUpdateRecovery.cs");
        Ensure(mainXaml.Contains("<about:AboutView", StringComparison.Ordinal) &&
               !mainXaml.Contains("x:Name=\"LibrarySummaryText\"", StringComparison.Ordinal) &&
               aboutView.Contains("x:Name=\"LibrarySummaryText\"", StringComparison.Ordinal) &&
               mainXaml.Contains("<diagnostics:DiagnosticsView", StringComparison.Ordinal) &&
               !mainXaml.Contains("x:Name=\"DiagnosticHealthTitleText\"", StringComparison.Ordinal) &&
               diagnosticsView.Contains("x:Name=\"DiagnosticHealthTitleText\"", StringComparison.Ordinal) &&
               mainXaml.Split('\n').Length < 950,
            "MainWindow must compose About and Diagnostics as features instead of reclaiming their controls.");
        Ensure(File.Exists(recoveryShellPath) &&
               updateShell.Split('\n').Length < 350 &&
               !updateShell.Contains("UpdateRollbackStore", StringComparison.Ordinal) &&
               !updateShell.Contains("ReleaseUpdateClient", StringComparison.Ordinal) &&
               !updateShell.Contains("CompatibilityUpdateClient", StringComparison.Ordinal),
            "About update orchestration must keep recovery and low-level clients outside the main update file.");

        var testRoot = Path.Combine(repositoryRoot, "tests", "Tessalume.Tests");
        var program = await File.ReadAllTextAsync(Path.Combine(testRoot, "Program.cs"));
        var project = await File.ReadAllTextAsync(Path.Combine(testRoot, "Tessalume.Tests.csproj"));
        Ensure(string.Equals(program.Trim(), "return await TestSuite.RunAsync(args);", StringComparison.Ordinal),
            "The test entry point must remain separate from product tests and runtime probes.");
        Ensure(project.Contains("Tessalume.App.csproj", StringComparison.Ordinal) &&
               !project.Contains("UiPreferences.cs", StringComparison.Ordinal),
            "Product tests must reference the App project instead of linking product source files.");
    }

}
