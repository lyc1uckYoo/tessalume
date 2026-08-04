internal static partial class TestSuite
{
    static Task CreatorWorkspaceHistoryIsNormalizedAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-workspace-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, CreatorWorkspaceStore.MaximumRecentWorkspaces + 4)
            .Select(index => new CreatorWorkspaceRecord
            {
                DirectoryPath = Path.Combine(root, $"project-{index}"),
                DisplayName = $"Project {index}",
                LastOpenedAt = now.AddMinutes(-index),
            })
            .Concat(
            [
                new CreatorWorkspaceRecord
                {
                    DirectoryPath = Path.Combine(root, "project-0", "."),
                    DisplayName = "Older duplicate",
                    LastOpenedAt = now.AddDays(-1),
                },
                new CreatorWorkspaceRecord { DirectoryPath = "   " },
            ]);

        var store = new CreatorWorkspaceStore(entries);
        Ensure(store.Entries.Count == CreatorWorkspaceStore.MaximumRecentWorkspaces,
            "Creator workspace history must stay bounded.");
        Ensure(store.Entries.Count(entry => entry.DirectoryPath.EndsWith("project-0", StringComparison.OrdinalIgnoreCase)) == 1 &&
               store.Entries[0].DisplayName == "Project 0",
            "Creator workspace history must deduplicate normalized paths and keep the newest entry.");

        var touchedPath = Path.Combine(root, "new-project");
        store.Touch(touchedPath, now.AddMinutes(1));
        Ensure(store.Entries[0].DirectoryPath == Path.GetFullPath(touchedPath) &&
               store.Entries[0].DisplayName == "new-project",
            "Touching a workspace must promote it and derive a useful display name.");
        Ensure(store.Remove(Path.Combine(touchedPath, ".")) &&
               store.Entries.All(entry => !entry.DirectoryPath.Equals(touchedPath, StringComparison.OrdinalIgnoreCase)),
            "Workspace removal must use normalized path identity.");

        var preferences = UiPreferencesMigration.PrepareForSave(new UiPreferences
        {
            RecentCreatorWorkspaces = store.Snapshot(),
        });
        var serialized = JsonSerializer.Serialize(preferences);
        var reloaded = UiPreferencesMigration.Deserialize(
            serialized,
            new JsonSerializerOptions(),
            out var migrated);
        Ensure(!migrated &&
               reloaded.SchemaVersion == UiPreferences.CurrentSchemaVersion &&
               reloaded.RecentCreatorWorkspaces.Select(entry => entry.DirectoryPath)
                   .SequenceEqual(store.Entries.Select(entry => entry.DirectoryPath)),
            "Schema-two preferences must round-trip normalized creator workspace history.");

        return Task.CompletedTask;
    }

    static async Task CreatorProjectScannerProducesStructuredHealthAsync()
    {
        var scanner = new ThemeProjectScanner(new ThemePackageLoader());
        using var fixture = await CreatorThemeFixture.CreateAsync();
        var template = await scanner.ScanProjectAsync(fixture.Root);
        Ensure(template.Health.CanExport,
            $"A complete Template 1.0 project should pass creator health: {FormatCreatorIssues(template.Health)}");
        Ensure(template.ThemeId == "fixture.creator-theme" && template.AssetCount == 11,
            "Creator project metadata must expose theme identity and the eleven standard asset slots.");
        Ensure(Enum.GetValues<ThemeProjectHealthGroup>()
                .Where(group => group != ThemeProjectHealthGroup.Workspace)
                .All(group => template.Health.Checks.Any(check => check.Group == group)),
            "Creator health must include every project report group.");

        var starter = await scanner.ScanProjectAsync(Path.Combine(FindRepositoryRoot(), "examples"));
        Ensure(!starter.Health.CanExport &&
               starter.Health.Checks.Any(check => check.Code == "creator.draft.unresolved"),
            "The starter template must not be mistaken for a publishable finished theme.");

        var publishedRoot = Path.Combine(FindRepositoryRoot(), "themes");
        foreach (var directory in Directory.EnumerateDirectories(publishedRoot))
        {
            var published = await scanner.ScanProjectAsync(directory);
            Ensure(published.Health.CanExport,
                $"Published theme {Path.GetFileName(directory)} failed creator health: " +
                FormatCreatorIssues(published.Health));
        }

        var invalidRoot = Path.Combine(Path.GetTempPath(), $"tessalume-invalid-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(invalidRoot);
        try
        {
            var invalid = await scanner.ScanProjectAsync(invalidRoot);
            Ensure(!invalid.Health.CanExport &&
                   invalid.Health.Checks.Any(check => check.Code == "manifest.missing"),
                "A project without a manifest must remain visible with a structured blocking issue.");
        }
        finally
        {
            Directory.Delete(invalidRoot, recursive: true);
        }

        var missingWorkspace = await scanner.ScanWorkspaceAsync(Path.Combine(
            Path.GetTempPath(),
            $"tessalume-moved-workspace-{Guid.NewGuid():N}"));
        Ensure(!missingWorkspace.Exists &&
               missingWorkspace.Health.Checks.Any(check => check.Code == "workspace.directory.missing"),
            "Moved workspaces must return a recoverable health result instead of throwing.");
    }

    static async Task CreatorCenterOrchestratesWorkspaceProjectsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-creator-center-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "workspace");
        var project = Path.Combine(workspace, "themes", "fixture.creator-theme");
        Directory.CreateDirectory(Path.Combine(workspace, "themes"));
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "TESSALUME_CREATOR_WORKSPACE.md"),
            "creator workspace");
        try
        {
            using var fixture = await CreatorThemeFixture.CreateAsync(project);
            var store = new CreatorWorkspaceStore();
            var saveCount = 0;
            using var viewModel = new CreatorCenterViewModel(
                store,
                () =>
                {
                    saveCount++;
                    return Task.CompletedTask;
                });

            await viewModel.AddWorkspaceAsync(workspace);
            Ensure(saveCount == 1 &&
                   viewModel.Workspaces.Count == 1 &&
                   viewModel.SelectedWorkspace is not null,
                "Adding a creator workspace must persist it and make it current.");
            Ensure(viewModel.Projects.Count == 1 &&
                   viewModel.SelectedProject?.ThemeId == "fixture.creator-theme" &&
                   viewModel.SelectedProject.CanExport &&
                   viewModel.HealthGroups.Count == 8,
                "The creator center must discover projects and expose the complete grouped health report.");
            Ensure(CreatorWorkspaceProvisioner.ResolveExistingWorkspace(project) == workspace,
                "Opening a theme project folder must resolve back to its creator workspace.");

            await viewModel.RemoveSelectedWorkspaceAsync();
            Ensure(saveCount == 2 &&
                   viewModel.Workspaces.Count == 0 &&
                   viewModel.Projects.Count == 0 &&
                   viewModel.SelectedWorkspace is null,
                "Removing a recent workspace must keep files intact while clearing creator-center state.");
            Ensure(Directory.Exists(project),
                "Removing a workspace record must never delete the project directory.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task CreatorWatcherDebouncesStableChangesAndReleasesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-watcher-{Guid.NewGuid():N}");
        var assets = Path.Combine(root, "assets");
        var cssPath = Path.Combine(root, "skin.css");
        var imagePath = Path.Combine(assets, "hero.png");
        Directory.CreateDirectory(assets);
        await File.WriteAllTextAsync(cssPath, ":root {}\n");
        await File.WriteAllBytesAsync(imagePath, [0x01]);
        var batches = new System.Collections.Concurrent.ConcurrentQueue<ThemeProjectChangeBatch>();
        using var signal = new SemaphoreSlim(0);

        try
        {
            using (var watcher = new ThemeProjectWatcher(
                       root,
                       [cssPath, imagePath],
                       debounceDelay: TimeSpan.FromMilliseconds(120),
                       stabilityInterval: TimeSpan.FromMilliseconds(45),
                       stabilityTimeout: TimeSpan.FromSeconds(3)))
            {
                watcher.Changed += (_, batch) =>
                {
                    batches.Enqueue(batch);
                    signal.Release();
                };
                watcher.Start();

                for (var index = 0; index < 6; index++)
                {
                    await File.AppendAllTextAsync(cssPath, $".save-{index} {{}}\n");
                    await Task.Delay(25);
                }
                Ensure(await signal.WaitAsync(TimeSpan.FromSeconds(4)),
                    "Continuous editor saves must eventually produce a watcher batch.");
                await Task.Delay(320);
                Ensure(batches.Count == 1 && batches.TryPeek(out var saveBatch) &&
                       saveBatch.ChangedPaths.Contains(cssPath, StringComparer.OrdinalIgnoreCase),
                    "Continuous saves must be debounced into one stable change batch.");

                var replacement = Path.Combine(root, "skin.next.tmp");
                await File.WriteAllTextAsync(replacement, ":root { --replacement: 1; }");
                File.Move(replacement, cssPath, overwrite: true);
                Ensure(await signal.WaitAsync(TimeSpan.FromSeconds(4)),
                    "Atomic rename replacement must be detected.");
                Ensure(batches.Last().ChangedPaths.Contains(cssPath, StringComparer.OrdinalIgnoreCase),
                    "The replacement batch must identify the final declared CSS path.");

                await using (var stream = new FileStream(
                                 imagePath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.ReadWrite | FileShare.Delete))
                {
                    for (var index = 0; index < 8; index++)
                    {
                        await stream.WriteAsync(new byte[2048]);
                        await stream.FlushAsync();
                        await Task.Delay(55);
                    }
                }
                Ensure(await signal.WaitAsync(TimeSpan.FromSeconds(4)),
                    "A slowly generated image must be reported after its final write.");
                Ensure(new FileInfo(imagePath).Length == 16 * 1024 &&
                       batches.Last().ChangedPaths.Contains(imagePath, StringComparer.OrdinalIgnoreCase),
                    "The watcher must wait for the complete declared asset instead of reading a partial image.");

                Directory.Delete(root, recursive: true);
                Ensure(await signal.WaitAsync(TimeSpan.FromSeconds(4)),
                    "Deleting the selected project directory must wake the watcher.");
                Ensure(!batches.Last().ProjectExists,
                    "Directory deletion must be surfaced as a recoverable project state.");
            }

            var releaseRoot = Path.Combine(Path.GetTempPath(), $"tessalume-watcher-release-{Guid.NewGuid():N}");
            Directory.CreateDirectory(releaseRoot);
            var releaseFile = Path.Combine(releaseRoot, "skin.css");
            await File.WriteAllTextAsync(releaseFile, ":root {}");
            var callbacksAfterDispose = 0;
            var releaseWatcher = new ThemeProjectWatcher(
                releaseRoot,
                [releaseFile],
                debounceDelay: TimeSpan.FromMilliseconds(80),
                stabilityInterval: TimeSpan.FromMilliseconds(30));
            releaseWatcher.Changed += (_, _) => Interlocked.Increment(ref callbacksAfterDispose);
            releaseWatcher.Start();
            releaseWatcher.Dispose();
            await File.AppendAllTextAsync(releaseFile, "\n.after-dispose {}");
            await Task.Delay(300);
            Ensure(callbacksAfterDispose == 0,
                "Disposing a project watcher must cancel pending work and release all callbacks.");
            Directory.Delete(releaseRoot, recursive: true);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task CreatorCenterAutoAppliesOnlyHealthyStableProjectsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-auto-apply-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "workspace");
        var project = Path.Combine(workspace, "themes", "fixture.creator-theme");
        Directory.CreateDirectory(Path.Combine(workspace, "themes"));
        try
        {
            using var fixture = await CreatorThemeFixture.CreateAsync(project);
            var applyCount = 0;
            var automaticFlags = new List<bool>();
            var runtime = new CreatorRuntimeBridge(
                (_, automatic, _) =>
                {
                    Interlocked.Increment(ref applyCount);
                    lock (automaticFlags) automaticFlags.Add(automatic);
                    return Task.FromResult(new CreatorRuntimeActionResult(
                        true,
                        new CreatorRuntimeStatus(true, 9340, false),
                        "applied"));
                },
                _ => Task.FromResult(new CreatorRuntimeStatus(true, 9340, false)),
                _ => Task.FromResult(new CreatorRuntimeStatus(true, 9340, true)));
            using var viewModel = new CreatorCenterViewModel(
                new CreatorWorkspaceStore(),
                () => Task.CompletedTask,
                runtime);
            await viewModel.AddWorkspaceAsync(workspace);
            Ensure(!viewModel.AutoApplyEnabled && viewModel.IsWatching,
                "Creator auto-apply must default to off while stable file monitoring starts automatically.");

            viewModel.AutoApplyEnabled = true;
            var cssPath = Path.Combine(project, "skin.css");
            for (var index = 0; index < 4; index++)
            {
                await File.AppendAllTextAsync(cssPath, $"\n.valid-{index} {{ color: black; }}");
                await Task.Delay(35);
            }
            await WaitForConditionAsync(() => Volatile.Read(ref applyCount) == 1, TimeSpan.FromSeconds(6));
            Ensure(applyCount == 1 && automaticFlags.SequenceEqual([true]) &&
                   viewModel.SelectedProject?.CanExport == true,
                "A burst of healthy saves must trigger exactly one automatic revalidation and apply.");

            await File.AppendAllTextAsync(cssPath, "\n.broken {");
            await WaitForConditionAsync(
                () => viewModel.SelectedProject?.CanExport == false,
                TimeSpan.FromSeconds(6));
            await Task.Delay(350);
            Ensure(applyCount == 1 && viewModel.LastAppliedText.Contains("跳过", StringComparison.Ordinal),
                "Automatic apply must be skipped when the refreshed project has a blocking error.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Timed out waiting for an asynchronous creator workflow condition.");
    }

    static async Task ThemeArchiveExportIsDeterministicAndRoundTripsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-export-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source-theme");
        var firstArchive = Path.Combine(root, "first.zip");
        var secondArchive = Path.Combine(root, "second.zip");
        Directory.CreateDirectory(root);
        try
        {
            using var fixture = await CreatorThemeFixture.CreateAsync(source);
            Directory.CreateDirectory(Path.Combine(source, ".sources"));
            await File.WriteAllTextAsync(Path.Combine(source, ".sources", "design.psd"), "must not ship");
            await File.WriteAllTextAsync(Path.Combine(source, "creator-notes.md"), "must not ship");

            var loader = new ThemePackageLoader();
            var writer = new ThemeArchiveWriter(loader);
            var first = await writer.ExportAsync(source, firstArchive);
            var second = await writer.ExportAsync(source, secondArchive);
            Ensure(first.FileCount == second.FileCount && first.Sha256 == second.Sha256,
                "Unchanged source must produce a deterministic theme archive.");
            Ensure(first.Sha256.Length == 64 && first.RevisionHash.Length == 64,
                "Theme export must report SHA-256 and source revision hashes.");

            using (var archive = ZipFile.OpenRead(firstArchive))
            {
                var names = archive.Entries.Select(entry => entry.FullName).ToArray();
                Ensure(names.All(name => name.StartsWith("fixture.creator-theme/", StringComparison.Ordinal)),
                    "The exported archive must contain exactly one theme root.");
                Ensure(names.All(name => !name.Contains(".sources", StringComparison.OrdinalIgnoreCase) &&
                                         !name.EndsWith("creator-notes.md", StringComparison.OrdinalIgnoreCase)),
                    "Design sources and undeclared notes must not enter the share archive.");
            }

            using (var extraction = await ThemeArchiveExtractor.ExtractAsync(firstArchive))
            {
                var reloaded = (await loader.LoadAsync(extraction.ThemeDirectory)).Package
                    ?? throw new InvalidOperationException("The exported archive did not reload.");
                var actualRevision = await ThemeFingerprintCalculator.CalculateAsync(reloaded);
                Ensure(actualRevision == first.RevisionHash,
                    "The exported archive must preserve the exact runtime revision hash.");
            }

            var beforeReplace = first.Sha256;
            var replaced = await writer.ExportAsync(source, firstArchive);
            Ensure(replaced.Sha256 == beforeReplace,
                "Replacing an existing archive must preserve deterministic output.");

            await File.AppendAllTextAsync(Path.Combine(source, "skin.css"), "\n.unclosed {");
            var preservedBytes = await File.ReadAllBytesAsync(firstArchive);
            var rejected = false;
            try
            {
                await writer.ExportAsync(source, firstArchive);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Ensure(rejected && preservedBytes.SequenceEqual(await File.ReadAllBytesAsync(firstArchive)),
                "A failed export must not damage the previously completed archive.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string FormatCreatorIssues(ThemeProjectHealthReport health) =>
        string.Join(
            "; ",
            health.Checks
                .Where(check => check.Severity != ThemeProjectHealthSeverity.Passed)
                .Select(check => $"{check.Code}: {check.Message}"));

}
