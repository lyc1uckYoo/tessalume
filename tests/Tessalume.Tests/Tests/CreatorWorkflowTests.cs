internal static partial class TestSuite
{
    static async Task PortableCreatorWorkspaceIsSelfContainedAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appProject = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Tessalume.App.csproj"));
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var mainSource = await ReadMainWindowSourceAsync(appRoot);
        var mainXaml = await ReadMainWindowXamlAsync(appRoot);
        var creatorSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Creator",
            "CreatorCenterView.xaml.cs"));
        var creatorViewModel = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Creator",
            "CreatorCenterViewModel.cs"));
        var creatorDevelopment = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Creator",
            "CreatorCenterViewModel.Development.cs"));
        var creatorWatcher = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Creator",
            "ThemeProjectWatcher.cs"));
        var creatorProvisioner = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Creator",
            "CreatorWorkspaceProvisioner.cs"));
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
        Ensure(mainSource.Contains("CreatorCenter.ActivateAsync", StringComparison.Ordinal) &&
               mainXaml.Contains("创建新工作区", StringComparison.Ordinal) &&
               mainXaml.Contains("请使用 $author-tessalume-theme", StringComparison.Ordinal) &&
               mainXaml.Contains("x:Name=\"CreatorPromptText\"", StringComparison.Ordinal) &&
               mainXaml.Contains("AutomationProperties.Name=\"复制创作提示词\"", StringComparison.Ordinal) &&
               mainXaml.Contains("FocusVisualStyle=\"{x:Null}\"", StringComparison.Ordinal) &&
               creatorSource.Contains("Clipboard.SetText(CreatorPromptText.Text)", StringComparison.Ordinal) &&
               !creatorSource.Contains("ShowMessage(\"复制", StringComparison.Ordinal),
            "The creator guide must show a larger complete prompt with one direct, non-modal copy action.");
        Ensure(mainXaml.Contains("CreatorCenterView", StringComparison.Ordinal) &&
               creatorViewModel.Contains("ThemeProjectScanner", StringComparison.Ordinal) &&
               creatorViewModel.Contains("ThemeArchiveWriter", StringComparison.Ordinal) &&
               creatorProvisioner.Contains("ResolveExistingWorkspace", StringComparison.Ordinal) &&
               mainSource.Contains("CreatorCenter?.Dispose()", StringComparison.Ordinal),
            "The creator center must own workspace scanning, health, export, and lifecycle outside MainWindow.");
        Ensure(mainXaml.Contains("本地开发会话", StringComparison.Ordinal) &&
               mainXaml.Contains("AutomationProperties.Name=\"校验通过后自动应用\"", StringComparison.Ordinal) &&
               creatorWatcher.Contains("FileSystemWatcher", StringComparison.Ordinal) &&
               creatorWatcher.Contains("WaitForStableFileAsync", StringComparison.Ordinal) &&
               creatorDevelopment.Contains("automatic: true", StringComparison.Ordinal) &&
               creatorDevelopment.Contains("StopProjectWatcher", StringComparison.Ordinal),
            "Creator live development must expose stable watching, explicit auto-apply, and reliable lifecycle cleanup.");
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

}
