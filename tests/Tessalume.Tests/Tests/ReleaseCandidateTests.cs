internal static partial class TestSuite
{
    static async Task Version20IsolatedCreatorToRecoveryFlowAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-13-e2e-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "workspace");
        var project = Path.Combine(workspace, "themes", "fixture.creator-theme");
        var library = Path.Combine(root, "themes");
        var data = Path.Combine(root, "data");
        var share = Path.Combine(root, "share", "fixture.creator-theme.zip");
        Directory.CreateDirectory(Path.Combine(workspace, "themes"));
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "TESSALUME_CREATOR_WORKSPACE.md"),
            "Tessalume creator workspace");
        try
        {
            using var fixture = await CreatorThemeFixture.CreateAsync(project);
            const string versionOnePreferences = """
                {
                  "SchemaVersion": 1,
                  "DarkMode": true,
                  "OnboardingCompleted": true,
                  "AutomaticUpdateChecks": false,
                  "FavoriteThemeIds": ["fixture.creator-theme"],
                  "ThemeVisualSettings": {
                    "fixture.creator-theme": {
                      "Dark": { "Chat": { "Brightness": 84, "Opacity": 92 } }
                    }
                  }
                }
                """;
            var settingsPath = Path.Combine(data, "ui-settings.json");
            await File.WriteAllTextAsync(settingsPath, versionOnePreferences);
            using (var preferencesStore = new UiPreferencesStore(data))
            {
                var migrated = preferencesStore.Load();
                var workspaces = new CreatorWorkspaceStore();
                workspaces.Touch(workspace, DateTimeOffset.UtcNow);
                await preferencesStore.SaveAsync(migrated with
                {
                    RecentCreatorWorkspaces = workspaces.Snapshot(),
                });
            }

            Ensure(File.Exists(Path.Combine(
                       data,
                       "backups",
                       "latest-before-preferences-migration.json")),
                "The isolated 1.2 upgrade must preserve its original preferences before migration.");

            var loader = new ThemePackageLoader();
            var workspaceHealth = await new ThemeProjectScanner(loader).ScanWorkspaceAsync(workspace);
            Ensure(workspaceHealth.Exists &&
                   workspaceHealth.Projects.Count == 1 &&
                   workspaceHealth.Projects[0].Health.CanExport,
                "A generated theme must be discovered and pass the structured creator report.");

            var export = await new ThemeArchiveWriter(loader).ExportAsync(project, share);
            Ensure(File.Exists(share) && export.FileCount >= 14 && export.Sha256.Length == 64,
                "A healthy creator project must produce a verified share archive and SHA-256.");

            using (var extraction = await ThemeArchiveExtractor.ExtractAsync(share))
            {
                var imported = await new ThemeImporter(loader).ImportAsync(
                    extraction.ThemeDirectory,
                    library,
                    overwrite: false);
                var importedRevision = await ThemeFingerprintCalculator.CalculateAsync(imported);
                Ensure(importedRevision == export.RevisionHash,
                    "The exported archive must re-import into a clean library without changing its revision.");
            }

            var backupPath = Path.Combine(root, "release-candidate-backup.zip");
            var backupService = new PortableBackupService(root, data, library);
            var backup = await backupService.CreateAsync(
                backupPath,
                new PortableBackupOptions { IncludeImportedThemes = true });
            Ensure(backup.Summary.ImportedThemes.Count == 1 &&
                   backup.Summary.ImportedThemes[0].ThemeId == "fixture.creator-theme",
                "The release-candidate backup must include the explicitly selected imported theme.");

            await File.WriteAllTextAsync(settingsPath, "mutated");
            await File.WriteAllTextAsync(
                Path.Combine(library, "fixture.creator-theme", "skin.css"),
                "mutated");
            var restore = await backupService.RestoreAsync(backupPath);
            Ensure(File.Exists(restore.AutomaticSnapshotPath),
                "Restoring the release-candidate backup must preserve the immediately previous state.");

            UiPreferences restoredPreferences;
            using (var preferencesStore = new UiPreferencesStore(data))
            {
                restoredPreferences = preferencesStore.Load();
            }
            var restoredTheme = await loader.LoadAsync(Path.Combine(library, "fixture.creator-theme"));
            Ensure(restoredPreferences.SchemaVersion == UiPreferences.CurrentSchemaVersion &&
                   restoredPreferences.DarkMode &&
                   !restoredPreferences.AutomaticUpdateChecks &&
                   restoredPreferences.FavoriteThemeIds.Contains("fixture.creator-theme") &&
                   restoredPreferences.ThemeVisualOverrides["fixture.creator-theme"].Dark?.Chat?.Brightness == 84 &&
                   restoredPreferences.RecentCreatorWorkspaces.Count == 1 &&
                   restoredTheme.Validation.IsValid,
                "The 1.2 → 1.3 → export → import → backup → restore flow must preserve every user setting and a valid theme.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
