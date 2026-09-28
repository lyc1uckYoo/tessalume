using Tessalume.App.Features.ArtworkLibrary.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal static partial class TestSuite
{
    static async Task ArtworkLibraryIndexesOriginalsAndMigratesWithoutChangingSettingsAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var package = (await new ThemePackageLoader().LoadAsync(Path.Combine(
                FindRepositoryRoot(), "themes", "xin.moonfox-sovereign"))).Package!;
            var imagePath = Path.Combine(root, "personalization", "images", "old-personal.png");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            await File.WriteAllBytesAsync(imagePath, OnePixelPng);
            var settings = new Dictionary<string, ThemeVisualSettings>
            {
                [package.Manifest.Id] = new()
                {
                    Dark = new() { Chat = new() { CustomImagePath = "personalization/images/old-personal.png", Brightness = 117 } },
                },
            };
            var before = JsonSerializer.Serialize(settings);
            var settingsPath = Path.Combine(root, "ui-settings.json");
            await File.WriteAllTextAsync(settingsPath, before);
            await using var service = new ArtworkLibraryService(root);
            var snapshot = await service.LoadAsync([package], settings);
            Ensure(snapshot.Items.Count(item => item.IsBuiltIn) == 11 && snapshot.Items.Count == 12,
                "Gallery must discover all eleven character originals and the existing personal picture.");
            var migrated = snapshot.Items.Single(item => !item.IsBuiltIn);
            Ensure(migrated.CharacterId == "xin" && migrated.CharacterName == "心" &&
                   migrated.Usages.Single().Region == ArtworkRegion.Chat && migrated.Usages.Single().Mode == ArtworkColorMode.Dark,
                "Migration must infer the current character and derive the actual target usage.");
            Ensure(JsonSerializer.Serialize(settings) == before && await File.ReadAllTextAsync(settingsPath) == before,
                "Catalog discovery and migration must not write or mutate visual settings.");
            var defaults = await new ArtworkThemeDefaultsStore().LoadAsync(package);
            var expected = ThemeArtworkSettingsResolver.Resolve(defaults.Defaults, null).Settings.Light.Hero;
            var hero = snapshot.Items.Single(item => item.IsBuiltIn && item.Regions.Contains(ArtworkRegion.Hero) && item.Modes.Contains(ArtworkColorMode.Light));
            var recommended = service.ResolveInitialAdjustment(hero, package.Manifest.Id, ArtworkRegion.Hero, ArtworkColorMode.Light);
            Ensure(recommended.CompositionMode == ThemeArtworkCompositionMode.Custom && recommended.CustomImagePath is null &&
                   recommended.Placement == expected.Placement && recommended.Brightness == expected.Brightness,
                "Original recommendation must preserve published placement and effects without retaining a wrong image reference.");
            Ensure(!Directory.EnumerateFiles(Path.GetDirectoryName(imagePath)!).Any(path => path != imagePath),
                "Indexing and preview must not copy theme originals into personal storage.");
            var prepared = await service.PrepareImageAsync(hero);
            Ensure((await File.ReadAllBytesAsync(Path.Combine(root, prepared))).SequenceEqual(await File.ReadAllBytesAsync(hero.AbsolutePath)),
                "Applying a built-in original must preserve its exact source bytes.");
            var appliedSettings = ArtworkSettingsAccessor.SetAdjustment(settings[package.Manifest.Id], ArtworkColorMode.Light,
                ArtworkRegion.Hero, recommended with { CustomImagePath = prepared });
            var reloaded = await service.LoadAsync([package], new Dictionary<string, ThemeVisualSettings> { [package.Manifest.Id] = appliedSettings });
            Ensure(reloaded.Items.Count == 12 && reloaded.Items.Single(item => item.Id == hero.Id).Usages.Any(usage => usage.Region == ArtworkRegion.Hero),
                "Prepared built-in originals must keep their identity and not reappear as extra migrated personal items.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static async Task ArtworkLibraryPersistsFavoritesAndIsolatesCompositionTargetsAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var source = Path.Combine(root, "portrait.png");
            await File.WriteAllBytesAsync(source, TwoPixelPng);
            await using var service = new ArtworkLibraryService(root);
            await service.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
            var image = await service.ImportAsync(source, "xin", "心", ArtworkRegion.Hero, ArtworkColorMode.Light);
            var duplicate = await service.ImportAsync(source, "xin", "心", ArtworkRegion.Chat, ArtworkColorMode.Dark);
            Ensure(image.Id == duplicate.Id && Directory.GetFiles(Path.Combine(root, "personalization", "images")).Length == 1,
                "Reimporting the same image must reuse the original and merge supported targets.");
            await service.SetFavoriteAsync(image.Id, true);
            var chosen = new ThemeArtworkAdjustment { Brightness = 139, Opacity = 83, CustomImagePath = "outside-secret.png", ThemeAssetKey = "wrong-image" };
            await service.SaveCompositionAsync(image.Id, "xin.theme", ArtworkRegion.Chat, ArtworkColorMode.Dark, chosen);
            await using var restarted = new ArtworkLibraryService(root);
            var snapshot = await restarted.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
            var loaded = snapshot.Items.Single();
            Ensure(loaded.IsFavorite && loaded.PixelWidth == 2 && loaded.Regions.Contains(ArtworkRegion.Chat),
                "Favorite, dimensions and merged import destinations must survive a fresh service load.");
            var restored = restarted.ResolveInitialAdjustment(loaded, "xin.theme", ArtworkRegion.Chat, ArtworkColorMode.Dark);
            Ensure(restored.Brightness == 139 && restored.Opacity == 83 && restored.CustomImagePath is null && restored.ThemeAssetKey is null,
                "Saved compositions must restore visual parameters without importing external image references.");
            foreach (var target in new[] { ("xin.theme", ArtworkRegion.Chat, ArtworkColorMode.Light),
                         ("xin.theme", ArtworkRegion.Hero, ArtworkColorMode.Dark), ("other.theme", ArtworkRegion.Chat, ArtworkColorMode.Dark) })
                Ensure(restarted.ResolveInitialAdjustment(loaded, target.Item1, target.Item2, target.Item3).Brightness == 100,
                    "A saved composition must not leak into another theme, region or color mode.");
            var catalog = Path.Combine(root, "personalization", "library.json");
            var before = await File.ReadAllTextAsync(catalog);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try { await restarted.SetFavoriteAsync(image.Id, false, canceled.Token); }
            catch (OperationCanceledException) { }
            Ensure(await File.ReadAllTextAsync(catalog) == before &&
                   !Directory.EnumerateFiles(Path.GetDirectoryName(catalog)!, "*.tmp").Any(),
                "Canceled catalog writes must preserve the previous complete document and leave no temporary file.");
            File.Delete(loaded.AbsolutePath);
            var missingRejected = false;
            try { await restarted.PrepareImageAsync(loaded); }
            catch (FileNotFoundException) { missingRejected = true; }
            Ensure(missingRejected, "Apply must revalidate a picture removed after the gallery was loaded.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static async Task ArtworkLibraryRejectsUnsafeMetadataAndPreservesUnsupportedCatalogAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var directory = Path.Combine(root, "personalization");
            Directory.CreateDirectory(directory);
            var catalog = Path.Combine(directory, "library.json");
            await File.WriteAllTextAsync(catalog, """
                {"schemaVersion":1,"images":[{"id":"escape","name":"escape","storedPath":"personalization/images/../../outside.png"}],"favorites":[],"compositions":[],"preparedImages":{}}
                """);
            await using var service = new ArtworkLibraryService(root);
            var snapshot = await service.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
            Ensure(snapshot.Items.Count == 0 && snapshot.Diagnostics.Count > 0, "Traversal metadata must not expose outside files.");
            foreach (var invalid in new[] { "{broken", "{\"schemaVersion\":99}" })
            {
                await File.WriteAllTextAsync(catalog, invalid);
                await using var guarded = new ArtworkLibraryService(root);
                var read = await guarded.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
                Ensure(read.Diagnostics.Count > 0 && await File.ReadAllTextAsync(catalog) == invalid,
                    "Corrupt or future indexes must be preserved, with a visible diagnostic.");
                var rejected = false;
                try { await guarded.ImportAsync("missing.png", "xin", "心", ArtworkRegion.Chat, ArtworkColorMode.Dark); }
                catch (InvalidDataException) { rejected = true; }
                Ensure(rejected && await File.ReadAllTextAsync(catalog) == invalid,
                    "Mutation must not overwrite an unreadable or unsupported catalog.");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static async Task ArtworkLibraryBackupRoundTripsAndLegacyRestorePreservesNewCatalogAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var data = Path.Combine(root, "data");
            var themes = Path.Combine(root, "themes");
            var images = Path.Combine(data, "personalization", "images");
            Directory.CreateDirectory(images);
            Directory.CreateDirectory(themes);
            await File.WriteAllTextAsync(Path.Combine(data, "ui-settings.json"), "{}");
            await File.WriteAllBytesAsync(Path.Combine(images, "old.png"), OnePixelPng);
            var service = new PortableBackupService(root, data, themes);
            var legacy = Path.Combine(root, "legacy.zip");
            await service.CreateAsync(legacy);
            await using var library = new ArtworkLibraryService(data);
            var snapshot = await library.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
            await library.SetFavoriteAsync(snapshot.Items.Single().Id, true);
            var source = Path.Combine(root, "new.png");
            await File.WriteAllBytesAsync(source, TwoPixelPng);
            var imported = await library.ImportAsync(source, "xin", "心", ArtworkRegion.Sidebar, ArtworkColorMode.Dark);
            var catalog = Path.Combine(data, "personalization", "library.json");
            var expected = await File.ReadAllTextAsync(catalog);
            await service.RestoreAsync(legacy);
            Ensure(await File.ReadAllTextAsync(catalog) == expected && File.Exists(imported.AbsolutePath),
                "Restoring a pre-library backup must preserve newer originals, classifications and favorites.");
            var backup = Path.Combine(root, "library.zip");
            await service.CreateAsync(backup);
            using (var archive = ZipFile.OpenRead(backup))
                Ensure(archive.GetEntry("data/personalization/library.json") is not null,
                    "Portable backup must include the versioned picture catalog.");
            await File.WriteAllTextAsync(catalog, "{broken");
            File.Delete(imported.AbsolutePath);
            await service.RestoreAsync(backup);
            Ensure(await File.ReadAllTextAsync(catalog) == expected &&
                   (await File.ReadAllBytesAsync(imported.AbsolutePath)).SequenceEqual(TwoPixelPng),
                "A new backup must restore the catalog and exact original bytes together.");
            await VerifyEmptyArtworkLibraryBackupRestoreAsync();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task VerifyEmptyArtworkLibraryBackupRestoreAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var data = Path.Combine(root, "data");
            var themes = Path.Combine(root, "themes");
            var personalization = Path.Combine(data, "personalization");
            var images = Path.Combine(personalization, "images");
            var catalog = Path.Combine(personalization, "library.json");
            Directory.CreateDirectory(personalization);
            Directory.CreateDirectory(themes);
            const string emptyCatalog = "{\"schemaVersion\":1,\"images\":[]}";
            await File.WriteAllTextAsync(catalog, emptyCatalog);
            await File.WriteAllTextAsync(Path.Combine(data, "ui-settings.json"), "{}");
            var backupService = new PortableBackupService(root, data, themes);
            var emptyBackup = Path.Combine(root, "empty-library.zip");
            await backupService.CreateAsync(emptyBackup);
            using (var archive = ZipFile.OpenRead(emptyBackup))
                Ensure(archive.GetEntry("data/personalization/library.json") is not null &&
                       !archive.Entries.Any(entry => entry.FullName.StartsWith("data/personalization/images/", StringComparison.Ordinal)),
                    "An empty-gallery backup must explicitly contain the catalog without image or directory placeholder entries.");

            var source = Path.Combine(root, "later-import.png");
            await File.WriteAllBytesAsync(source, TwoPixelPng);
            await using var library = new ArtworkLibraryService(data);
            await library.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
            var imported = await library.ImportAsync(source, "xin", "心", ArtworkRegion.Memory, ArtworkColorMode.Dark);
            await library.SetFavoriteAsync(imported.Id, true);
            var sibling = Path.Combine(personalization, "unrelated-preference.txt");
            await File.WriteAllTextAsync(sibling, "preserve sibling data");

            var restored = await backupService.RestoreAsync(emptyBackup);
            Ensure(await File.ReadAllTextAsync(catalog) == emptyCatalog &&
                   Directory.Exists(images) && !Directory.EnumerateFileSystemEntries(images).Any() &&
                   await File.ReadAllTextAsync(sibling) == "preserve sibling data" &&
                   (await File.ReadAllBytesAsync(source)).SequenceEqual(TwoPixelPng),
                "Restoring an explicit empty gallery must replace the personal image store while preserving sibling data and external originals.");
            await using var reopened = new ArtworkLibraryService(data);
            Ensure((await reopened.LoadAsync([], new Dictionary<string, ThemeVisualSettings>())).Items.Count == 0,
                "A later gallery load must not rediscover images added after an empty-gallery backup.");
            using var recovery = ZipFile.OpenRead(restored.AutomaticSnapshotPath);
            var recoveryEntry = recovery.GetEntry("data/" + imported.StoredPath);
            Ensure(recoveryEntry is not null && recovery.GetEntry("data/personalization/library.json") is not null,
                "The automatic pre-restore snapshot must preserve the newer catalog and picture for recovery.");
            using var recoveredBytes = new MemoryStream();
            await using (var entryStream = recoveryEntry!.Open()) await entryStream.CopyToAsync(recoveredBytes);
            Ensure(recoveredBytes.ToArray().SequenceEqual(TwoPixelPng),
                "The pre-restore snapshot must retain the exact imported image bytes.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static async Task ArtworkLibraryPreservesPreparedPicturesAcrossThemeRemovalAndUpdatesAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var themeDirectory = Path.Combine(root, "theme");
            Directory.CreateDirectory(themeDirectory);
            var original = Path.Combine(themeDirectory, "hero-light.png");
            await File.WriteAllBytesAsync(original, OnePixelPng);
            var package = new ThemePackage(themeDirectory, Path.Combine(themeDirectory, "manifest.json"),
                new ThemeManifest { Id = "xin.fixture", Name = "心 · 测试" }, null, null,
                new Dictionary<string, string> { ["hero-light"] = original }, original, null);
            await using var library = new ArtworkLibraryService(Path.Combine(root, "data"));
            var settings = new Dictionary<string, ThemeVisualSettings>();
            var first = (await library.LoadAsync([package], settings)).Items.Single();
            var stored = await library.PrepareImageAsync(first);
            await library.SetFavoriteAsync(first.Id, true);
            await library.SaveCompositionAsync(first.Id, package.Manifest.Id, ArtworkRegion.Hero, ArtworkColorMode.Light,
                new ThemeArtworkAdjustment { Brightness = 147 });
            var catalog = Path.Combine(root, "data", "personalization", "library.json");
            var catalogBefore = await File.ReadAllTextAsync(catalog);
            var stable = await library.LoadAsync([package], settings);
            Ensure(stable.Items.Single().Id == first.Id && await File.ReadAllTextAsync(catalog) == catalogBefore,
                "Refreshing unchanged sources must reuse durable byte fingerprints without rewriting the catalog.");

            var removed = (await library.LoadAsync([], settings)).Items.Single();
            Ensure(removed.Id == first.Id && !removed.IsBuiltIn && removed.IsFavorite && removed.CharacterName == "心" &&
                   library.ResolveInitialAdjustment(removed, package.Manifest.Id, ArtworkRegion.Hero, ArtworkColorMode.Light).Brightness == 147,
                "Removing a theme must recover its selected original as a personal picture with the same favorite and recipe identity.");
            var reinstalled = await library.LoadAsync([package], settings);
            Ensure(reinstalled.Items.Count == 1 && reinstalled.Items.Single().IsBuiltIn,
                "Reinstalling the same original must not create duplicate recovered and built-in cards.");

            await File.WriteAllBytesAsync(original, TwoPixelPng);
            File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddSeconds(2));
            var updated = await library.LoadAsync([package], settings);
            var replacement = updated.Items.Single(item => item.IsBuiltIn);
            Ensure(updated.Items.Count == 2 && replacement.Id != first.Id && !replacement.IsFavorite &&
                   library.ResolveInitialAdjustment(replacement, package.Manifest.Id, ArtworkRegion.Hero, ArtworkColorMode.Light).Brightness == 100 &&
                   (await File.ReadAllBytesAsync(Path.Combine(root, "data", stored))).SequenceEqual(OnePixelPng),
                "Changing an asset under the same theme/key must not apply its predecessor's recipe or destroy the selected original.");
            Ensure(updated.Items.Single(item => !item.IsBuiltIn).IsFavorite,
                "The old original's favorite must remain attached to the retained bytes.");

            await File.WriteAllBytesAsync(original, OnePixelPng);
            var changedAfterPreviewRejected = false;
            try { await library.PrepareImageAsync(replacement); }
            catch (IOException) { changedAfterPreviewRejected = true; }
            Ensure(changedAfterPreviewRejected,
                "An original that changes after preview must be rejected before applying under the stale picture identity.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static async Task ArtworkLibraryGeneralImportsKeepIndependentClassificationAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var explicitSource = Path.Combine(root, "dark-chat.png");
            var generalSource = Path.Combine(root, "general.png");
            await File.WriteAllBytesAsync(explicitSource, OnePixelPng);
            await File.WriteAllBytesAsync(generalSource, TwoPixelPng);
            await using var library = new ArtworkLibraryService(root);
            await library.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
            var explicitImage = await library.ImportAsync(explicitSource, "xin", "心", ArtworkRegion.Chat, ArtworkColorMode.Dark);
            var generalImage = await library.ImportAsync(generalSource, "personal", "我的图片", null, null);
            var duplicate = await library.ImportAsync(explicitSource, "personal", "我的图片", null, null);
            Ensure(duplicate.Id == explicitImage.Id && duplicate.CharacterId == "xin" &&
                   duplicate.Regions.SequenceEqual([ArtworkRegion.Chat]) && duplicate.Modes.SequenceEqual([ArtworkColorMode.Dark]),
                "Unclassified reimport must retain an existing picture's explicit character, region and color classification.");
            Ensure(generalImage.CharacterId == "personal" &&
                   generalImage.Regions.Order().SequenceEqual(Enum.GetValues<ArtworkRegion>().Order()) &&
                   generalImage.Modes.Order().SequenceEqual(Enum.GetValues<ArtworkColorMode>().Order()),
                "A general import must stay available across regions and modes instead of inheriting the previous target.");
            var catalogPath = Path.Combine(root, "personalization", "library.json");
            using (var catalog = JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath)))
            {
                var stored = catalog.RootElement.GetProperty("images").EnumerateArray()
                    .Single(image => image.GetProperty("id").GetString() == generalImage.Id);
                Ensure(stored.GetProperty("regions").GetArrayLength() == 0 && stored.GetProperty("modes").GetArrayLength() == 0,
                    "Unselected import metadata must remain empty on disk, without implicit Hero, Chat, Light or Dark tags.");
            }
            await library.ImportAsync(generalSource, "personal", "我的图片", ArtworkRegion.Sidebar, null);
            await using var reopened = new ArtworkLibraryService(root);
            var reloaded = (await reopened.LoadAsync([], new Dictionary<string, ThemeVisualSettings>())).Items
                .Single(image => image.Id == generalImage.Id);
            Ensure(reloaded.Regions.SequenceEqual([ArtworkRegion.Sidebar]) &&
                   reloaded.Modes.Order().SequenceEqual(Enum.GetValues<ArtworkColorMode>().Order()),
                "An explicit region filter may classify a picture without assigning an unrelated color mode.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string ArtworkLibraryTestDirectory()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "artifacts", "tmp", "artwork-library-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
