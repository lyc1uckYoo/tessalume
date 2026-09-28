using System.Reflection;
using System.Windows.Controls;
using Tessalume.App.Features.ArtworkLibrary.Application;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.ArtworkLibrary.Presentation;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

internal static partial class TestSuite
{
    static async Task ArtworkLibraryDeletionProtectsOriginalAndPreparedIdentitiesAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var package = await CreateArtworkDeletionThemeAsync(root);
            var data = Path.Combine(root, "data");
            await using var service = new ArtworkLibraryService(data);
            var settings = new Dictionary<string, ThemeVisualSettings>();
            var original = (await service.LoadAsync([package], settings)).Items.Single();
            var forged = original with { IsBuiltIn = false };
            await EnsureArtworkDeletionRejectedAsync(() => service.DeleteImportedImageAsync(forged.Id));
            var preparedPath = await service.PrepareImageAsync(original);
            var recovered = (await service.LoadAsync([], settings)).Items.Single();
            Ensure(!recovered.IsBuiltIn && !recovered.CanDelete,
                "A recovered original must remain protected even after its theme is removed.");
            var protectedCatalog = await File.ReadAllBytesAsync(ArtworkDeletionCatalogPath(data));
            await EnsureArtworkDeletionRejectedAsync(() => service.DeleteImportedImageAsync(recovered.Id));
            Ensure((await File.ReadAllBytesAsync(ArtworkDeletionCatalogPath(data))).SequenceEqual(protectedCatalog),
                "Rejecting a protected source must not change its favorite, recipe, or prepared identity.");

            await service.LoadAsync([package], settings);
            var importedCopy = await service.ImportAsync(original.AbsolutePath, "personal", "我的图片", null, null);
            Ensure(importedCopy.CanDelete && importedCopy.Id != original.Id && importedCopy.StoredPath == preparedPath,
                "An explicitly imported copy may share original bytes while retaining a separate deletable personal identity.");
            await service.DeleteImportedImageAsync(importedCopy.Id);
            var remaining = await service.LoadAsync([package], settings);
            Ensure(remaining.Items.Any(item => item.Id == original.Id && item.IsBuiltIn && !item.CanDelete) &&
                   remaining.Items.All(item => item.Id != importedCopy.Id) &&
                   (await File.ReadAllBytesAsync(Path.Combine(data, preparedPath))).SequenceEqual(OnePixelPng) &&
                   (await File.ReadAllBytesAsync(original.AbsolutePath)).SequenceEqual(OnePixelPng),
                "Deleting a personal copy must preserve the built-in card, prepared bytes, and source original.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static async Task ArtworkLibraryDeletedImportsStayHiddenAndReimportCleanlyAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var source = Path.Combine(root, "removed.png");
            var retainedSource = Path.Combine(root, "retained.png");
            await File.WriteAllBytesAsync(source, OnePixelPng);
            await File.WriteAllBytesAsync(retainedSource, TwoPixelPng);
            var settings = new Dictionary<string, ThemeVisualSettings>();
            await using var service = new ArtworkLibraryService(root);
            await service.LoadAsync([], settings);
            var removed = await service.ImportAsync(source, "xin", "心", ArtworkRegion.Memory, ArtworkColorMode.Dark);
            var retained = await service.ImportAsync(retainedSource, "xin", "心", ArtworkRegion.Hero, ArtworkColorMode.Light);
            await service.SetFavoriteAsync(removed.Id, true);
            await service.SetFavoriteAsync(retained.Id, true);
            await service.SaveCompositionAsync(removed.Id, "xin.fixture", ArtworkRegion.Memory, ArtworkColorMode.Dark,
                new ThemeArtworkAdjustment { Brightness = 137 });
            await service.SaveCompositionAsync(retained.Id, "xin.fixture", ArtworkRegion.Hero, ArtworkColorMode.Light,
                new ThemeArtworkAdjustment { Brightness = 149 });
            settings["xin.fixture"] = new() { Dark = new() { Memory = new() { CustomImagePath = removed.StoredPath } } };
            var settingsBefore = JsonSerializer.Serialize(settings);
            var preferencesPath = Path.Combine(root, "ui-settings.json");
            await File.WriteAllTextAsync(preferencesPath, settingsBefore);
            await service.DeleteImportedImageAsync(removed.Id);
            using (var catalog = JsonDocument.Parse(await File.ReadAllTextAsync(ArtworkDeletionCatalogPath(root))))
            {
                var document = catalog.RootElement;
                Ensure(document.GetProperty("images").EnumerateArray().All(image => image.GetProperty("id").GetString() != removed.Id) &&
                       document.GetProperty("favorites").EnumerateArray().All(value => value.GetString() != removed.Id) &&
                       document.GetProperty("compositions").EnumerateArray().All(recipe => recipe.GetProperty("itemId").GetString() != removed.Id) &&
                       document.GetProperty("hiddenImagePaths").EnumerateArray().Any(value => value.GetString() == removed.StoredPath),
                    "Deleting an import must clear only its visible metadata and record the portable hidden path.");
            }
            Ensure((await service.LoadAsync([], settings)).Items.All(item => item.Id != removed.Id),
                "An in-process refresh must not rediscover soft-deleted bytes as a new import.");
            await using var restarted = new ArtworkLibraryService(root);
            var reloaded = await restarted.LoadAsync([], settings);
            Ensure(reloaded.Items.Count == 1 && reloaded.Items.Single().Id == retained.Id && reloaded.Items.Single().IsFavorite &&
                   restarted.ResolveInitialAdjustment(reloaded.Items.Single(), "xin.fixture", ArtworkRegion.Hero, ArtworkColorMode.Light).Brightness == 149,
                "Restart must keep the deleted import hidden and retain another image's favorite and recipe.");
            Ensure(JsonSerializer.Serialize(settings) == settingsBefore && await File.ReadAllTextAsync(preferencesPath) == settingsBefore &&
                   (await File.ReadAllBytesAsync(removed.AbsolutePath)).SequenceEqual(OnePixelPng),
                "Removing a picture from the library must preserve current theme references and their original bytes.");
            var reimported = await restarted.ImportAsync(source, "xin", "心", ArtworkRegion.TaskLeft, ArtworkColorMode.Light);
            var visibleAgain = await restarted.LoadAsync([], settings);
            Ensure(reimported.Id == removed.Id && visibleAgain.Items.Count == 2 && !reimported.IsFavorite &&
                   restarted.ResolveInitialAdjustment(reimported, "xin.fixture", ArtworkRegion.Memory, ArtworkColorMode.Dark).Brightness == 100,
                "Explicit reimport must restore visibility without resurrecting deleted favorites or composition recipes.");
            using var finalCatalog = JsonDocument.Parse(await File.ReadAllTextAsync(ArtworkDeletionCatalogPath(root)));
            Ensure(finalCatalog.RootElement.GetProperty("hiddenImagePaths").EnumerateArray()
                    .All(value => value.GetString() != reimported.StoredPath),
                "Reimport must remove the matching hidden marker instead of relying only on a transient UI card.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static async Task ArtworkLibraryDeletionFailuresPreserveWritableAndReadOnlyCatalogsAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var source = Path.Combine(root, "personal.png");
            await File.WriteAllBytesAsync(source, OnePixelPng);
            await using var service = new ArtworkLibraryService(root);
            await service.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
            var image = await service.ImportAsync(source, "personal", "我的图片", null, null);
            await service.SetFavoriteAsync(image.Id, true);
            var catalogPath = ArtworkDeletionCatalogPath(root);
            var bytesBefore = await File.ReadAllBytesAsync(catalogPath);
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                var rejected = false;
                try { await service.DeleteImportedImageAsync(image.Id, canceled.Token); }
                catch (OperationCanceledException) { rejected = true; }
                Ensure(rejected, "A canceled deletion must not enter the catalog transaction.");
            }
            var writeRejected = false;
            using (new FileStream(catalogPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { await service.DeleteImportedImageAsync(image.Id); }
                catch (IOException) { writeRejected = true; }
                catch (UnauthorizedAccessException) { writeRejected = true; }
            }
            Ensure(writeRejected && (await File.ReadAllBytesAsync(catalogPath)).SequenceEqual(bytesBefore) &&
                   !Directory.EnumerateFiles(Path.GetDirectoryName(catalogPath)!, "*.tmp").Any() && File.Exists(image.AbsolutePath),
                "A failed atomic catalog write must preserve its previous bytes and retained original, with no temporary file left behind.");
            // Without refreshing from disk, another mutation must still see the
            // same live item and must not persist a leaked deletion marker.
            await service.SetFavoriteAsync(image.Id, false);
            using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath)))
                Ensure(document.RootElement.GetProperty("images").GetArrayLength() == 1 &&
                       document.RootElement.GetProperty("hiddenImagePaths").GetArrayLength() == 0,
                    "A rejected write must roll back both the in-memory item list and the pending hidden-path state.");

            const string unsupported = "{\"schemaVersion\":99}";
            await File.WriteAllTextAsync(catalogPath, unsupported);
            var protectedSnapshot = await service.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
            Ensure(protectedSnapshot.Diagnostics.Count > 0, "The unsupported catalog must enter read-only protection.");
            await EnsureArtworkDeletionRejectedAsync(() => service.DeleteImportedImageAsync(image.Id));
            Ensure(await File.ReadAllTextAsync(catalogPath) == unsupported && File.Exists(image.AbsolutePath),
                "Deletion must not overwrite an unsupported catalog or remove any original bytes.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static async Task ArtworkLibraryDeletedImportsSurviveBackupRoundTripAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var data = Path.Combine(root, "data");
            var themes = Path.Combine(root, "themes");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(themes);
            await File.WriteAllTextAsync(Path.Combine(data, "ui-settings.json"), "{}");
            var source = Path.Combine(root, "personal.png");
            await File.WriteAllBytesAsync(source, OnePixelPng);
            await using var service = new ArtworkLibraryService(data);
            await service.LoadAsync([], new Dictionary<string, ThemeVisualSettings>());
            var image = await service.ImportAsync(source, "personal", "我的图片", null, null);
            await service.DeleteImportedImageAsync(image.Id);
            var expectedCatalog = await File.ReadAllBytesAsync(ArtworkDeletionCatalogPath(data));
            var backup = new PortableBackupService(root, data, themes);
            var archivePath = Path.Combine(root, "deleted-library.zip");
            await backup.CreateAsync(archivePath);
            using (var archive = ZipFile.OpenRead(archivePath))
                Ensure(archive.GetEntry("data/personalization/library.json") is not null &&
                       archive.GetEntry("data/" + image.StoredPath) is not null,
                    "A backup must include both the hidden-path catalog and retained bytes used by existing settings.");
            await service.ImportAsync(source, "personal", "我的图片", null, null);
            await backup.RestoreAsync(archivePath);
            await using var restarted = new ArtworkLibraryService(data);
            Ensure((await restarted.LoadAsync([], new Dictionary<string, ThemeVisualSettings>())).Items.Count == 0 &&
                   (await File.ReadAllBytesAsync(ArtworkDeletionCatalogPath(data))).SequenceEqual(expectedCatalog) &&
                   (await File.ReadAllBytesAsync(image.AbsolutePath)).SequenceEqual(OnePixelPng),
                "Restoring a backup must restore deletion visibility without breaking retained image references.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static Task ArtworkLibraryDeletionConfirmationAndNavigationPreserveAppliedStateAsync() =>
        RunArtworkLibraryExperienceAsync(async fixture =>
        {
            var view = fixture.Window.ArtworkLibraryPage;
            var baseline = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
            await view.SelectItemAsync(fixture.FirstImage);
            await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Memory, ArtworkColorMode.Dark);
            await InvokeMainWindowTaskAsync(fixture.Window, "ApplyArtworkLibraryAsync",
                new ArtworkLibraryApplyEventArgs(fixture.ThemeId, ArtworkRegion.Memory, ArtworkColorMode.Dark,
                    fixture.FirstImage, view.DraftAdjustment));
            var undoField = typeof(MainWindow).GetField("_artworkLibraryUndo", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var undoBefore = undoField.GetValue(fixture.Window);
            var settingsBefore = JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window));
            var preferencesBefore = await File.ReadAllBytesAsync(fixture.PreferencesPath);
            await view.SelectItemAsync(view.Items.First(item => item.IsBuiltIn));
            Ensure(view.DeleteButton.Visibility == Visibility.Collapsed && !view.SelectedItem!.CanDelete,
                "Theme originals must not expose the personal-image delete action.");
            view.BackToGallery();
            view.SearchBox.Text = "portrait";
            await view.SelectBrowseRegionAsync(ArtworkRegion.Hero);
            var character = view.SelectedCharacterId;
            await view.SelectItemAsync(view.Items.First(item => item.Id == fixture.FirstImage.Id));
            Ensure(view.DeleteButton.Visibility == Visibility.Visible && view.SelectedItem!.CanDelete,
                "An imported image must expose a clear delete action in its detail.");
            var catalogBefore = await File.ReadAllBytesAsync(ArtworkDeletionCatalogPath(fixture.DataDirectory));
            var selectedItem = view.SelectedItem!;
            var selectedId = selectedItem.Id;
            var previewBefore = view.PreviewIdentityPath;
            var confirmations = 0;
            fixture.Window.ArtworkLibraryDeleteConfirmation = _ => { confirmations++; return Task.FromResult(false); };
            await fixture.Window.DeleteArtworkLibraryItemAsync(selectedItem);
            Ensure(confirmations == 1 && view.SelectedItem?.Id == selectedId && view.PreviewIdentityPath == previewBefore &&
                   view.DetailPage.Visibility == Visibility.Visible &&
                   (await File.ReadAllBytesAsync(ArtworkDeletionCatalogPath(fixture.DataDirectory))).SequenceEqual(catalogBefore),
                "Canceling confirmation must preserve the selected detail, preview, and catalog exactly.");
            fixture.Window.ArtworkLibraryDeleteConfirmation = _ => { confirmations++; return Task.FromResult(true); };
            view.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForArtworkLibraryConditionAsync(() => confirmations == 2 && view.TargetControls.IsEnabled &&
                view.DetailPage.Visibility == Visibility.Collapsed &&
                view.Items.All(item => item.Id != selectedId), "Confirmed deletion did not return to the refreshed album.");
            Ensure(view.AlbumPanel.Visibility == Visibility.Visible && view.SelectedCharacterId == character &&
                   view.SearchBox.Text == "portrait" && view.BrowseRegion == ArtworkRegion.Hero && view.SelectedItem is null &&
                   JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window)) == settingsBefore &&
                   (await File.ReadAllBytesAsync(fixture.PreferencesPath)).SequenceEqual(preferencesBefore) &&
                   ReferenceEquals(undoField.GetValue(fixture.Window), undoBefore) && File.Exists(fixture.FirstImage.AbsolutePath),
                "Confirmed deletion must preserve browse context, active image settings, undo history, and referenced bytes.");
            await InvokeMainWindowTaskAsync(fixture.Window, "UndoArtworkLibraryAsync");
            Ensure(ThemeVisualSettingsSemanticComparer.AdjustmentEquals(
                    GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId].Dark.Memory, baseline.Dark.Memory) &&
                   view.Items.All(item => item.Id != selectedId),
                "The earlier apply undo must remain usable without resurrecting the deleted library card.");
        });

    private static string ArtworkDeletionCatalogPath(string dataDirectory) =>
        Path.Combine(dataDirectory, "personalization", "library.json");

    private static async Task EnsureArtworkDeletionRejectedAsync(Func<Task> operation)
    {
        var rejected = false;
        try { await operation(); }
        catch (InvalidDataException) { rejected = true; }
        catch (InvalidOperationException) { rejected = true; }
        Ensure(rejected, "The backend must reject deletion of a protected or unavailable image identity.");
    }

    private static async Task<ThemePackage> CreateArtworkDeletionThemeAsync(string root)
    {
        var directory = Path.Combine(root, "theme");
        Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, "hero-light.png");
        await File.WriteAllBytesAsync(image, OnePixelPng);
        return new ThemePackage(directory, Path.Combine(directory, "manifest.json"),
            new ThemeManifest { Id = "qa.deletion", Name = "删除保护测试", Capabilities = new() { Light = true, Dark = true } },
            null, null, new Dictionary<string, string> { ["hero-light"] = image }, image, null);
    }
}
