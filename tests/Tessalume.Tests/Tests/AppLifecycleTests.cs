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
               migratedPreferences.ArtworkPresets.Count == 0 &&
               migratedPreferences.ExperiencePresets.Count == 0 &&
               migratedPreferences.ThemeLibrarySort == ThemeLibraryState.DefaultSort &&
               migratedPreferences.RecentThemeUsage.Count == 0 &&
               migratedPreferences.CreatorPromptDraft.WorkName == "鸣潮" &&
               migratedPreferences.CreatorPromptDraft.CharacterName == "椿" &&
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
               versionOnePreferences.ThemeVisualSettings["sample.theme"].Light.Hero.Zoom == 100 &&
               versionOnePreferences.ThemeVisualSettings["sample.theme"].Light.Hero.OffsetX == 0 &&
               versionOnePreferences.ThemeVisualSettings["sample.theme"].Light.Hero.Grayscale == 0 &&
               versionOnePreferences.ThemeVisualSettings["sample.theme"].Light.Hero.Blur == 0 &&
               versionOnePreferences.ThemeVisualSettings["sample.theme"].Dark.Chat.Opacity == 87 &&
               versionOnePreferences.RecentCreatorWorkspaces.Count == 0,
            "The 1.2 schema migration must preserve favorites, updates, and image adjustments while initializing workspace history.");

        const string versionTwoJson = """
            {
              "SchemaVersion": 2,
              "DarkMode": true,
              "OnboardingCompleted": true,
              "AutomaticUpdateChecks": true,
              "FavoriteThemeIds": ["advanced.theme"],
              "ThemeVisualSettings": {
                "advanced.theme": {
                  "Dark": {
                    "Sidebar": {
                      "Brightness": 92,
                      "Zoom": 128,
                      "OffsetX": -36,
                      "OffsetY": 18,
                      "Grayscale": 24,
                      "HueRotation": -42,
                      "Blur": 3.5
                    }
                  }
                }
              }
            }
            """;
        var versionTwoPreferences = UiPreferencesMigration.Deserialize(
            versionTwoJson,
            options,
            out var versionTwoMigrated);
        Ensure(versionTwoMigrated &&
               versionTwoPreferences.SchemaVersion == UiPreferences.CurrentSchemaVersion &&
               versionTwoPreferences.ThemeVisualSettings["advanced.theme"].Dark.Sidebar.Zoom == 128 &&
               versionTwoPreferences.ArtworkPresets.Count == 0,
            "Schema-two preferences must migrate to the current schema without losing advanced image adjustments.");

        var currentJson = JsonSerializer.Serialize(migratedPreferences with
        {
            ThemeVisualSettings = new Dictionary<string, ThemeVisualSettings>
            {
                ["advanced.theme"] = new ThemeVisualSettings
                {
                    Dark = new ThemeVisualModeSettings
                    {
                        Sidebar = new ThemeArtworkAdjustment
                        {
                            Zoom = 128,
                            OffsetX = -36,
                            OffsetY = 18,
                            Grayscale = 24,
                            HueRotation = -42,
                            Blur = 3.5,
                        },
                    },
                },
            },
            ArtworkPresets =
            [
                new ThemeArtworkPreset
                {
                    Name = "柔和背景",
                    Settings = new ThemeVisualModeSettings
                    {
                        Chat = new ThemeArtworkAdjustment
                        {
                            Brightness = 88,
                            Saturation = 72,
                            Blur = 2.5,
                        },
                    },
                },
            ],
            ExperiencePresets =
            [
                new ThemeExperiencePreset
                {
                    Name = "夜间创作",
                    ThemeId = "advanced.theme",
                    DarkMode = true,
                    Settings = new ThemeVisualSettings
                    {
                        Display = new ThemeDisplayPreferences
                        {
                            MotionIntensity = "reduced",
                            TextScale = "large",
                            Density = "spacious",
                        },
                    },
                },
            ],
            ThemeLibrarySort = ThemeLibraryState.RecentSort,
            RecentThemeUsage =
            [
                new ThemeUsageRecord
                {
                    ThemeId = "advanced.theme",
                    LastUsedAt = DateTimeOffset.Parse(
                        "2026-08-04T12:00:00+08:00",
                        System.Globalization.CultureInfo.InvariantCulture),
                    UseCount = 7,
                },
            ],
            CreatorPromptDraft = new CreatorPromptDraft
            {
                WorkName = "原神",
                CharacterName = "芙宁娜",
                VisualDirection = "蓝白歌剧舞台",
                UsesReferenceImages = true,
            },
        }, options);
        var currentPreferences = UiPreferencesMigration.Deserialize(currentJson, options, out var currentMigrated);
        var advancedAdjustment = currentPreferences.ThemeVisualSettings["advanced.theme"].Dark.Sidebar;
        Ensure(!currentMigrated &&
               currentPreferences.SchemaVersion == UiPreferences.CurrentSchemaVersion &&
               advancedAdjustment.Zoom == 128 &&
               advancedAdjustment.OffsetX == -36 &&
               advancedAdjustment.OffsetY == 18 &&
               advancedAdjustment.Grayscale == 24 &&
               advancedAdjustment.HueRotation == -42 &&
               advancedAdjustment.Blur == 3.5 &&
               currentPreferences.ArtworkPresets is [{ Name: "柔和背景" } preset] &&
               preset.Settings.Chat.Blur == 2.5 &&
               currentPreferences.ExperiencePresets is
               [{ Name: "夜间创作", ThemeId: "advanced.theme", DarkMode: true } experience] &&
               experience.Settings.Display is
               { MotionIntensity: "reduced", TextScale: "large", Density: "spacious" } &&
               currentPreferences.ThemeLibrarySort == ThemeLibraryState.RecentSort &&
               currentPreferences.RecentThemeUsage is [{ ThemeId: "advanced.theme", UseCount: 7 }] &&
               currentPreferences.CreatorPromptDraft is
               { WorkName: "原神", CharacterName: "芙宁娜", UsesReferenceImages: true },
            "Schema-four preferences must round-trip personalization and theme library state without another migration.");

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
            using (var migratedDocument = JsonDocument.Parse(File.ReadAllText(preferencesPath)))
            {
                Ensure(migratedDocument.RootElement.GetProperty("SchemaVersion").GetInt32() ==
                       UiPreferences.CurrentSchemaVersion,
                    "A successful migration must atomically persist the current schema immediately.");
            }

            File.Delete(snapshotPath);
            File.WriteAllText(preferencesPath, versionTwoJson);
            using (var store = new UiPreferencesStore(storeRoot))
            {
                _ = store.Load();
            }
            Ensure(File.Exists(snapshotPath) && File.ReadAllText(snapshotPath) == versionTwoJson,
                "The schema-two migration must preserve the original preferences JSON.");
            using (var migratedDocument = JsonDocument.Parse(File.ReadAllText(preferencesPath)))
            {
                Ensure(migratedDocument.RootElement.GetProperty("SchemaVersion").GetInt32() ==
                       UiPreferences.CurrentSchemaVersion,
                    "The schema-two migration must persist the current schema without waiting for another setting change.");
            }

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
        var aboutViewSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Features",
            "About",
            "AboutView.xaml.cs"));
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
        Ensure(mainWindowSource.Contains("AboutPage.SetStartupEnabled(enabled);", StringComparison.Ordinal) &&
               aboutViewSource.Contains("StartupCheckBox.IsChecked = enabled;", StringComparison.Ordinal),
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
        var helperRuntime = await File.ReadAllTextAsync(Path.Combine(appRoot, "Infrastructure", "UpdateHelperRuntime.cs"));
        var legacyAdapter = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Infrastructure",
            "Updates",
            "LegacyUpdateRecoveryAdapter.cs"));
        var preferences = await ReadUiPreferencesSourceAsync(appRoot);
        Ensure(xaml.Contains("x:Name=\"AutomaticUpdatesCheckBox\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Name=\"CheckForUpdatesButton\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Name=\"RollbackVersionButton\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Name=\"UpdateAvailableBadge\"", StringComparison.Ordinal) &&
               xaml.Contains("Click=\"UpdateAvailableBadge_Click\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Name=\"UpdateProgressBar\"", StringComparison.Ordinal),
            "The product must expose automatic checks, a manual check, an update badge, and download progress.");
        Ensure(preferences.Contains("AutomaticUpdateChecks { get; init; } = true", StringComparison.Ordinal) &&
               preferences.Contains("LastUpdateCheckAt", StringComparison.Ordinal) &&
               preferences.Contains("SemaphoreSlim _saveGate", StringComparison.Ordinal) &&
               preferences.Contains("preferences.FavoriteThemeIds ?? []", StringComparison.Ordinal) &&
               preferences.Contains("preferences.ThemeVisualSettings ??", StringComparison.Ordinal),
            "Preferences must retain update state, normalize older data, and serialize concurrent writes.");
        Ensure(mainSource.Contains("ScheduleAutomaticUpdateCheck", StringComparison.Ordinal) &&
               mainSource.Contains("_automaticUpdateCheckScheduled", StringComparison.Ordinal) &&
               mainSource.Contains("UpdateAvailableBadge.Visibility", StringComparison.Ordinal) &&
               mainSource.Contains("ConfirmAndInstallUpdateAsync", StringComparison.Ordinal) &&
               mainSource.Contains("if (_updateCheckInProgress || _availableUpdate is not", StringComparison.Ordinal) &&
               mainSource.Contains("HandleUpdateFailure(exception, showDialog: true)", StringComparison.Ordinal) &&
               !mainSource.Contains("DateTimeOffset.Now - checkedAt", StringComparison.Ordinal) &&
               mainSource.Contains("DownloadAndInstallUpdateAsync", StringComparison.Ordinal) &&
               mainSource.Contains("UpdateBootstrapper.StartHelper", StringComparison.Ordinal) &&
               mainSource.Contains("DescribeUpdateError", StringComparison.Ordinal) &&
               mainSource.Contains("无法连接 GitHub 更新服务", StringComparison.Ordinal),
            "Every enabled startup must check once, surface a non-blocking badge, and guard confirmed installation against concurrent or unhandled failures.");
        Ensure(appSource.Contains("UpdateBootstrapper.TryParseHelperArguments", StringComparison.Ordinal) &&
               helperRuntime.Contains("PortableUpdateInstaller.ApplyAndWriteResultAsync", StringComparison.Ordinal) &&
               bootstrapper.Contains("UseShellExecute = false", StringComparison.Ordinal) &&
               helperRuntime.Contains("ConfirmStartupHealthyAsync", StringComparison.Ordinal) &&
               helperRuntime.Contains("RestoreAfterFailedStartupAsync", StringComparison.Ordinal) &&
               !bootstrapper.Contains(
                   "Path.Combine(layout.RootDirectory, $\"{BrandInfo.ProductName}.exe.previous\")",
                   StringComparison.Ordinal),
            "A hidden standalone helper must health-check the new EXE, auto-restore failures, and retain the previous version.");
        var readResultAt = appSource.IndexOf("var startupUpdateResult = UpdateBootstrapper.ReadResult", StringComparison.Ordinal);
        var adaptLegacyAt = appSource.IndexOf("LegacyUpdateRecoveryAdapter.PrepareAsync", StringComparison.Ordinal);
        var cleanupAt = appSource.IndexOf("UpdateBootstrapper.CleanupStaleArtifactsAsync", StringComparison.Ordinal);
        var constructMainAt = appSource.IndexOf("var mainWindow = new MainWindow(layout)", StringComparison.Ordinal);
        var handoffAt = appSource.IndexOf("mainWindow.SetStartupUpdateResult(startupUpdateResult)", StringComparison.Ordinal);
        Ensure(readResultAt >= 0 && adaptLegacyAt > readResultAt && cleanupAt > adaptLegacyAt &&
               constructMainAt > cleanupAt && handoffAt > constructMainAt &&
               appSource.Contains("if (startupUpdateResult is null)", StringComparison.Ordinal) &&
               legacyAdapter.Contains("UpdateDataSnapshotStore", StringComparison.Ordinal) &&
               legacyAdapter.Contains("PortableUpdateInstaller.WriteResultAsync", StringComparison.Ordinal) &&
               mainSource.Contains("更新完成，但恢复点未建立", StringComparison.Ordinal) &&
               mainSource.Contains("没有建立可用的上一版本恢复点", StringComparison.Ordinal),
            "The rollback backup must remain available, legacy updater results must capture old schemas before migration, and failures must be reported honestly.");
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
