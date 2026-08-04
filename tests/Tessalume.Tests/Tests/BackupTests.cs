internal static partial class TestSuite
{
    static async Task PortableBackupRoundTripsUserDataAndImportedThemesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-backup-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "data");
        var themes = Path.Combine(root, "themes");
        var userTheme = Path.Combine(themes, "sample.theme");
        var builtInTheme = Path.Combine(themes, "builtin.theme");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(themes);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(data, "ui-settings.json"), "{\"value\":\"original\"}");
            await File.WriteAllTextAsync(Path.Combine(data, "state.json"), "{\"enabled\":true}");
            await File.WriteAllTextAsync(Path.Combine(data, "deleted-built-in-themes.txt"), "builtin.removed");
            Directory.CreateDirectory(Path.Combine(data, "logs"));
            await File.WriteAllTextAsync(Path.Combine(data, "logs", "tessalume.log"), "must not ship");

            using var userFixture = await ThemeFixture.CreateAsync(userTheme);
            Directory.CreateDirectory(builtInTheme);
            await File.WriteAllTextAsync(
                Path.Combine(builtInTheme, "manifest.json"),
                "{\"id\":\"builtin.theme\",\"name\":\"Built In\",\"version\":\"1.0\"}");
            await File.WriteAllTextAsync(Path.Combine(builtInTheme, "built-in.txt"), "embedded");

            var service = new PortableBackupService(
                root,
                data,
                themes,
                new HashSet<string>(["builtin.theme"], StringComparer.OrdinalIgnoreCase));
            var dataOnlyPath = Path.Combine(root, "data-only.zip");
            var dataOnly = await service.CreateAsync(dataOnlyPath);
            Ensure(!dataOnly.Summary.IncludesImportedThemes &&
                   dataOnly.Summary.ImportedThemes.Count == 0 &&
                   dataOnly.Summary.DataFileCount == 3,
                "Default backups must include user settings and runtime state without themes.");
            using (var archive = ZipFile.OpenRead(dataOnlyPath))
            {
                Ensure(archive.Entries.All(entry =>
                           !entry.FullName.Contains("logs", StringComparison.OrdinalIgnoreCase)),
                    "Logs and diagnostics must not enter a user-data backup.");
            }

            var fullPath = Path.Combine(root, "full.zip");
            var full = await service.CreateAsync(
                fullPath,
                new PortableBackupOptions { IncludeImportedThemes = true });
            Ensure(full.Summary.ImportedThemes.Count == 1 &&
                   full.Summary.ImportedThemes[0].ThemeId == "sample.theme" &&
                   full.Summary.TotalFileCount > full.Summary.DataFileCount,
                "An opt-in full backup must include imported themes while excluding built-in themes.");
            var inspected = await PortableBackupService.InspectAsync(fullPath);
            Ensure(inspected.SchemaVersion == full.Summary.SchemaVersion &&
                   inspected.CreatedAt == full.Summary.CreatedAt &&
                   inspected.TotalFileCount == full.Summary.TotalFileCount &&
                   inspected.TotalBytes == full.Summary.TotalBytes &&
                   inspected.ImportedThemes.Select(theme => theme.ThemeId)
                       .SequenceEqual(full.Summary.ImportedThemes.Select(theme => theme.ThemeId)),
                "A completed backup must pass an independent manifest and hash inspection.");

            await File.WriteAllTextAsync(Path.Combine(data, "ui-settings.json"), "{\"value\":\"mutated\"}");
            await File.WriteAllTextAsync(Path.Combine(data, "state.json"), "{\"enabled\":false}");
            await File.WriteAllTextAsync(Path.Combine(userTheme, "theme.css"), "mutated");
            await File.WriteAllTextAsync(Path.Combine(builtInTheme, "built-in.txt"), "keep-current-built-in");
            var restore = await service.RestoreAsync(fullPath);
            Ensure(await File.ReadAllTextAsync(Path.Combine(data, "ui-settings.json")) == "{\"value\":\"original\"}" &&
                   await File.ReadAllTextAsync(Path.Combine(data, "state.json")) == "{\"enabled\":true}" &&
                   (await File.ReadAllTextAsync(Path.Combine(userTheme, "theme.css"))).Contains("--accent", StringComparison.Ordinal) &&
                   await File.ReadAllTextAsync(Path.Combine(builtInTheme, "built-in.txt")) == "keep-current-built-in",
                "Restore must replace backed-up user data and imported themes without touching built-in themes.");
            Ensure(File.Exists(restore.AutomaticSnapshotPath) &&
                   (await PortableBackupService.InspectAsync(restore.AutomaticSnapshotPath)).IncludesImportedThemes,
                "Restore must preserve the immediately previous state as a validated automatic snapshot.");

            var unsafeArchivePath = Path.Combine(root, "contains-built-in.zip");
            await new PortableBackupService(root, data, themes).CreateAsync(
                unsafeArchivePath,
                new PortableBackupOptions { IncludeImportedThemes = true });
            var builtInRejected = false;
            try
            {
                await service.RestoreAsync(unsafeArchivePath);
            }
            catch (InvalidDataException)
            {
                builtInRejected = true;
            }
            Ensure(builtInRejected &&
                   await File.ReadAllTextAsync(Path.Combine(builtInTheme, "built-in.txt")) ==
                       "keep-current-built-in",
                "Restore policy must reject any archive that could replace an embedded theme.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task PortableBackupRejectsCorruptionCancellationAndRollsBackAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-backup-safety-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "data");
        var themes = Path.Combine(root, "themes");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(themes);
        var uiPath = Path.Combine(data, "ui-settings.json");
        var statePath = Path.Combine(data, "state.json");
        var deletedPath = Path.Combine(data, "deleted-built-in-themes.txt");
        try
        {
            await File.WriteAllTextAsync(uiPath, "backup-ui");
            await File.WriteAllTextAsync(statePath, "backup-state");
            await File.WriteAllTextAsync(deletedPath, "backup-deleted");
            var service = new PortableBackupService(root, data, themes);
            var backupPath = Path.Combine(root, "safe.zip");
            await service.CreateAsync(backupPath);

            var traversalPath = Path.Combine(root, "traversal.zip");
            using (var archive = ZipFile.Open(traversalPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escape.txt");
                await using var stream = entry.Open();
                await stream.WriteAsync("escape"u8.ToArray());
            }
            var traversalRejected = false;
            try
            {
                await PortableBackupService.InspectAsync(traversalPath);
            }
            catch (InvalidDataException)
            {
                traversalRejected = true;
            }
            Ensure(traversalRejected && !File.Exists(Path.Combine(root, "escape.txt")),
                "Backup inspection must reject traversal before extracting any content.");

            var corruptPath = Path.Combine(root, "corrupt.zip");
            File.Copy(backupPath, corruptPath);
            using (var archive = ZipFile.Open(corruptPath, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("data/ui-settings.json")!;
                entry.Delete();
                entry = archive.CreateEntry("data/ui-settings.json");
                await using var stream = entry.Open();
                await stream.WriteAsync("tampered"u8.ToArray());
            }
            var corruptionRejected = false;
            try
            {
                await PortableBackupService.InspectAsync(corruptPath);
            }
            catch (InvalidDataException)
            {
                corruptionRejected = true;
            }
            Ensure(corruptionRejected,
                "Backup inspection must verify every declared file hash.");

            var preservedDestination = Path.Combine(root, "preserved.zip");
            await File.WriteAllTextAsync(preservedDestination, "preserve-me");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var cancelled = false;
                try
                {
                    await service.CreateAsync(preservedDestination, cancellationToken: cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                Ensure(cancelled && await File.ReadAllTextAsync(preservedDestination) == "preserve-me",
                    "A cancelled backup must preserve the previous destination archive.");
            }

            await File.WriteAllTextAsync(uiPath, "current-ui");
            File.Delete(statePath);
            Directory.CreateDirectory(statePath);
            await File.WriteAllTextAsync(deletedPath, "current-deleted");
            var restoreFailed = false;
            try
            {
                await service.RestoreAsync(backupPath);
            }
            catch (IOException)
            {
                restoreFailed = true;
            }
            Ensure(restoreFailed &&
                   await File.ReadAllTextAsync(uiPath) == "current-ui" &&
                   await File.ReadAllTextAsync(deletedPath) == "current-deleted" &&
                   Directory.Exists(statePath),
                "A mid-transaction restore failure must roll back every earlier replacement.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
