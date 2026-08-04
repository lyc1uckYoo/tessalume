internal static partial class TestSuite
{
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
        var changelogPath = Path.Combine(repositoryRoot, "CHANGELOG.md");
        var issueTemplatePath = Path.Combine(repositoryRoot, ".github", "ISSUE_TEMPLATE", "bug-report.yml");
        var license = await File.ReadAllTextAsync(licensePath);
        var changelog = await File.ReadAllTextAsync(changelogPath);
        var security = await File.ReadAllTextAsync(securityPath);

        Ensure(buildScript.Contains("Get-FileHash -LiteralPath $finalExe -Algorithm SHA256", StringComparison.Ordinal) &&
               buildScript.Contains("SHA256SUMS.txt", StringComparison.Ordinal),
            "The release build must create a SHA-256 manifest beside the executable.");
        Ensure(File.Exists(securityPath) && File.Exists(issueTemplatePath) && File.Exists(changelogPath) &&
               changelog.Contains("## 1.3.0", StringComparison.Ordinal) &&
               license.Contains("MIT License", StringComparison.Ordinal) &&
               license.Contains("Permission is hereby granted", StringComparison.Ordinal),
            "Public testing requires an MIT license, security guidance, a structured bug form, and a public changelog.");
        Ensure(readme.Contains("issues/new?template=bug-report.yml", StringComparison.Ordinal) &&
               readme.Contains("Microsoft Defender SmartScreen", StringComparison.Ordinal) &&
               readme.Contains("SHA256SUMS.txt", StringComparison.Ordinal) &&
               readme.Contains("从 1.2.x 升级到 1.3.0", StringComparison.Ordinal) &&
               readme.Contains("项目化主题创作", StringComparison.Ordinal) &&
               readme.Contains("便携备份与恢复", StringComparison.Ordinal) &&
               readme.Contains("从 1.1 升级到 1.2", StringComparison.Ordinal) &&
               readme.Contains("[MIT License](LICENSE)", StringComparison.Ordinal) &&
               security.Contains("最新的 `1.3.x`", StringComparison.Ordinal) &&
               security.Contains("备份 ZIP", StringComparison.Ordinal),
            "The download guide must expose feedback, signature status, checksum verification, and licensing.");
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
            "MainWindow.ArtworkEditor.cs",
            "MainWindow.Creator.cs",
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
                     "CreatorCenterViewModel.cs",
                     "CreatorWorkspaceProvisioner.cs",
                     "CreatorWorkspaceStore.cs",
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
        Ensure(!File.Exists(Path.Combine(appRoot, "DiagnosticsWindow.xaml")) &&
               !File.Exists(Path.Combine(appRoot, "DiagnosticsWindow.xaml.cs")),
            "The obsolete duplicate diagnostics window must not return.");
        Ensure(File.Exists(Path.Combine(
                   appRoot,
                   "Diagnostics",
                   "CompatibilityHealthService.cs")) &&
               File.Exists(Path.Combine(
                   repositoryRoot,
                   "src",
                   "Tessalume.Core",
                   "Backup",
                   "PortableBackupService.cs")),
            "Compatibility presentation and portable backup algorithms must remain in dedicated feature boundaries.");

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
