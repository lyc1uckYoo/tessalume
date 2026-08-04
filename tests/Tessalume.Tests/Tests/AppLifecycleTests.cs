internal static partial class TestSuite
{
    static Task UiPreferencesMigrateFromUnversionedSchemaAsync()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        const string legacyJson = """
            {
              "DarkMode": true,
              "OnboardingCompleted": true,
              "AutomaticUpdateChecks": false,
              "FavoriteThemeIds": null,
              "ThemeVisualSettings": null
            }
            """;

        var migratedPreferences = UiPreferencesMigration.Deserialize(legacyJson, options, out var migrated);
        Ensure(migrated, "Unversioned UI preferences must run through the version-zero migration.");
        Ensure(migratedPreferences.SchemaVersion == UiPreferences.CurrentSchemaVersion,
            "Migrated UI preferences must use the current schema version.");
        Ensure(migratedPreferences.DarkMode && migratedPreferences.OnboardingCompleted,
            "The UI preferences migration must preserve existing values.");
        Ensure(!migratedPreferences.AutomaticUpdateChecks,
            "The UI preferences migration must preserve explicit opt-out values.");
        Ensure(migratedPreferences.FavoriteThemeIds.Count == 0 &&
               migratedPreferences.ThemeVisualSettings.Count == 0 &&
               migratedPreferences.RecentCreatorWorkspaces.Count == 0,
            "The UI preferences migration must normalize legacy null collections.");

        const string versionOneJson = """
            {
              "SchemaVersion": 1,
              "DarkMode": false,
              "OnboardingCompleted": true,
              "AutomaticUpdateChecks": false,
              "LastUpdateCheckAt": "2026-08-03T10:30:00+08:00",
              "FavoriteThemeIds": ["sample.theme"],
              "ThemeVisualSettings": {
                "sample.theme": {
                  "Light": {
                    "Hero": { "Brightness": 91, "Contrast": 103, "Saturation": 88, "Opacity": 96 }
                  },
                  "Dark": {
                    "Chat": { "Brightness": 82, "Contrast": 110, "Saturation": 94, "Opacity": 87 }
                  }
                }
              }
            }
            """;
        var versionOnePreferences = UiPreferencesMigration.Deserialize(
            versionOneJson,
            options,
            out var versionOneMigrated);
        Ensure(versionOneMigrated &&
               versionOnePreferences.SchemaVersion == UiPreferences.CurrentSchemaVersion,
            "Schema-one UI preferences must migrate to the current schema.");
        Ensure(versionOnePreferences.FavoriteThemeIds.SequenceEqual(["sample.theme"]) &&
               !versionOnePreferences.AutomaticUpdateChecks &&
               versionOnePreferences.LastUpdateCheckAt is not null &&
               versionOnePreferences.ThemeVisualSettings["sample.theme"].Light.Hero.Brightness == 91 &&
               versionOnePreferences.ThemeVisualSettings["sample.theme"].Dark.Chat.Opacity == 87 &&
               versionOnePreferences.RecentCreatorWorkspaces.Count == 0,
            "The 1.2 schema migration must preserve favorites, updates, and image adjustments while initializing workspace history.");

        var currentJson = JsonSerializer.Serialize(migratedPreferences, options);
        var currentPreferences = UiPreferencesMigration.Deserialize(currentJson, options, out var currentMigrated);
        Ensure(!currentMigrated && currentPreferences.SchemaVersion == UiPreferences.CurrentSchemaVersion,
            "Current UI preferences must load without another migration.");

        var futureJson = currentJson.Replace(
            $"\"SchemaVersion\": {UiPreferences.CurrentSchemaVersion}",
            $"\"SchemaVersion\": {UiPreferences.CurrentSchemaVersion + 1}",
            StringComparison.Ordinal);
        try
        {
            _ = UiPreferencesMigration.Deserialize(futureJson, options, out _);
            throw new InvalidOperationException("A future UI preferences schema was accepted.");
        }
        catch (JsonException)
        {
        }

        var storeRoot = Path.Combine(Path.GetTempPath(), $"tessalume-preferences-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storeRoot);
        try
        {
            var preferencesPath = Path.Combine(storeRoot, "ui-settings.json");
            File.WriteAllText(preferencesPath, legacyJson);
            using (var store = new UiPreferencesStore(storeRoot))
            {
                _ = store.Load();
            }
            var snapshotPath = Path.Combine(
                storeRoot,
                "backups",
                "latest-before-preferences-migration.json");
            Ensure(File.Exists(snapshotPath) && File.ReadAllText(snapshotPath) == legacyJson,
                "Preferences migration must preserve the latest original JSON before normalization.");

            File.Delete(snapshotPath);
            File.WriteAllText(preferencesPath, currentJson);
            using (var store = new UiPreferencesStore(storeRoot))
            {
                _ = store.Load();
            }
            Ensure(!File.Exists(snapshotPath),
                "Current-schema preferences must not overwrite the migration recovery snapshot.");
        }
        finally
        {
            Directory.Delete(storeRoot, recursive: true);
        }

        return Task.CompletedTask;
    }


    static async Task DeferredMainUiReplaysEngineStateAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = await ReadMainWindowSourceAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App"));
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

    static async Task MainWindowDisposalIsIdempotentAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = await ReadMainWindowSourceAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App"));
        Ensure(source.Contains("Interlocked.Exchange(ref _disposeStarted, 1)", StringComparison.Ordinal),
            "MainWindow disposal must guard against simultaneous close and explicit cleanup.");
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
        var mainWindowSource = await ReadMainWindowSourceAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App"));
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


    static async Task AutomaticUpdateWorkflowIsConnectedAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var xaml = await ReadMainWindowXamlAsync(appRoot);
        var mainSource = await ReadMainWindowSourceAsync(appRoot);
        var appSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "App.xaml.cs"));
        var bootstrapper = await File.ReadAllTextAsync(Path.Combine(appRoot, "Infrastructure", "UpdateBootstrapper.cs"));
        var preferences = await ReadUiPreferencesSourceAsync(appRoot);
        Ensure(xaml.Contains("x:Name=\"AutomaticUpdatesCheckBox\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Name=\"CheckForUpdatesButton\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Name=\"UpdateProgressBar\"", StringComparison.Ordinal),
            "Settings must expose automatic checks, a manual check, and download progress.");
        Ensure(preferences.Contains("AutomaticUpdateChecks { get; init; } = true", StringComparison.Ordinal) &&
               preferences.Contains("LastUpdateCheckAt", StringComparison.Ordinal) &&
               preferences.Contains("SemaphoreSlim _saveGate", StringComparison.Ordinal) &&
               preferences.Contains("preferences.FavoriteThemeIds ?? []", StringComparison.Ordinal) &&
               preferences.Contains("preferences.ThemeVisualSettings ??", StringComparison.Ordinal),
            "Preferences must retain update state, normalize older data, and serialize concurrent writes.");
        Ensure(mainSource.Contains("ScheduleAutomaticUpdateCheck", StringComparison.Ordinal) &&
               mainSource.Contains("DownloadAndInstallUpdateAsync", StringComparison.Ordinal) &&
               mainSource.Contains("UpdateBootstrapper.StartHelper", StringComparison.Ordinal) &&
               mainSource.Contains("DescribeUpdateError", StringComparison.Ordinal) &&
               mainSource.Contains("无法连接 GitHub 更新服务", StringComparison.Ordinal),
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
        var source = await ReadMainWindowSourceAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App"));
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

}
