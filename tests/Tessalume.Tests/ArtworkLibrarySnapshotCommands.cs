using System.Reflection;
using System.Windows.Threading;
using Tessalume.App.Features.ArtworkLibrary.Application;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.ArtworkLibrary.Presentation;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

internal static partial class TestSuite
{
    static Task<int> RenderArtworkLibrarySnapshotsAsync(
        string lightSnapshotPath,
        string darkSnapshotPath,
        string compactSnapshotPath) =>
        RenderArtworkLibrarySnapshotsCoreAsync(new ArtworkLibrarySnapshotPaths(
            compactSnapshotPath, lightSnapshotPath, darkSnapshotPath));

    static Task<int> RenderArtworkLibraryFlowSnapshotsAsync(string outputDirectory) =>
        RenderArtworkLibrarySnapshotsCoreAsync(new ArtworkLibrarySnapshotPaths(
            Path.Combine(outputDirectory, "detail-sidebar-compact.png"),
            Path.Combine(outputDirectory, "detail-hero-light.png"),
            Path.Combine(outputDirectory, "detail-chat-dark.png"),
            Path.Combine(outputDirectory, "gallery-light.png"),
            Path.Combine(outputDirectory, "gallery-dark.png"),
            AlbumSize: new Size(1280, 1620)));

    static Task<int> RenderArtworkLibraryAlbumSnapshotsAsync(string outputDirectory) =>
        RenderArtworkLibrarySnapshotsCoreAsync(new ArtworkLibrarySnapshotPaths(
            Path.Combine(outputDirectory, "detail-sidebar-compact.png"),
            AlbumLight: Path.Combine(outputDirectory, "album-light.png"),
            AlbumDark: Path.Combine(outputDirectory, "album-dark.png"),
            CharactersLight: Path.Combine(outputDirectory, "characters-light.png"),
            CharactersDark: Path.Combine(outputDirectory, "characters-dark.png"),
            AlbumCompact: Path.Combine(outputDirectory, "album-compact.png")));

    static Task<int> RenderArtworkLibraryCardSnapshotsAsync(string outputDirectory) =>
        RenderArtworkLibrarySnapshotsCoreAsync(new ArtworkLibrarySnapshotPaths(
            CardLight: Path.Combine(outputDirectory, "card-detail-light.png"),
            MemoryDark: Path.Combine(outputDirectory, "memory-detail-dark.png")));

    static Task<int> RenderArtworkLibraryDeleteSnapshotsAsync(string outputDirectory) =>
        RenderArtworkLibrarySnapshotsCoreAsync(new ArtworkLibrarySnapshotPaths(
            DeleteLight: Path.Combine(outputDirectory, "import-delete-light.png"),
            DeleteDark: Path.Combine(outputDirectory, "import-delete-dark.png")));

    private sealed record ArtworkLibrarySnapshotPaths(
        string? Compact = null, string? Hero = null, string? Chat = null,
        string? AlbumLight = null, string? AlbumDark = null,
        string? CharactersLight = null, string? CharactersDark = null, Size? AlbumSize = null,
        string? AlbumCompact = null, string? CardLight = null, string? MemoryDark = null,
        string? DeleteLight = null, string? DeleteDark = null);

    private enum ArtworkLibrarySnapshotSurface { Characters, Album, Detail }

    private static Task<int> RenderArtworkLibrarySnapshotsCoreAsync(ArtworkLibrarySnapshotPaths paths)
    {
        var repositoryRoot = FindRepositoryRoot();
        var data = Path.Combine(ArtworkLibrarySnapshotDataRoot, $".snapshot-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(data);
        Exception? failure = null;
        using (var preferences = new UiPreferencesStore(data))
        {
            preferences.SaveAsync(new UiPreferences
            {
                OnboardingCompleted = true,
                AutomaticUpdateChecks = false,
                QuickSwitchVisible = false,
            }).GetAwaiter().GetResult();
        }

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MainWindow? window = null;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    window = new MainWindow(new PortableLayout(
                        repositoryRoot, Path.Combine(repositoryRoot, "themes"), data));
                    InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                    // Use current source assets and a selected, unapplied theme. No
                    // runtime application or user's portable settings are involved.
                    await AttachArtworkSnapshotThemeAsync(window, repositoryRoot);
                    await InvokeMainWindowTaskAsync(window, "ResolveThemeArtworkDefaultsAsync", CancellationToken.None);
                    var catalog = await new ThemeCatalog(new ThemePackageLoader())
                        .ScanAsync(Path.Combine(repositoryRoot, "themes"));
                    var packages = catalog.Where(item => item.Validation.IsValid && item.Package is not null)
                        .Select(item => item.Package!).ToArray();
                    var settings = GetArtworkLibrarySettings(window);
                    await using var service = new ArtworkLibraryService(data);
                    var view = window.ArtworkLibraryPage;
                    view.Configure(service, packages, settings,
                        "cartethyia.gale-tide-crown", ArtworkRegion.Hero, ArtworkColorMode.Light);
                    InvokeMainWindowMethod(window, "NavigateTo", AppRoute.ArtworkLibrary);
                    await view.RefreshAsync();
                    if (paths.CharactersLight is not null)
                        await RenderArtworkLibrarySurfaceAsync(window, false, new Size(1280, 820),
                            paths.CharactersLight, ArtworkLibrarySnapshotSurface.Characters);
                    if (paths.CharactersDark is not null)
                        await RenderArtworkLibrarySurfaceAsync(window, true, new Size(1280, 820),
                            paths.CharactersDark, ArtworkLibrarySnapshotSurface.Characters);
                    var character = ArtworkLibraryService.GetCharacter(packages.Single(package =>
                        package.Manifest.Id == "cartethyia.gale-tide-crown"));
                    await view.OpenCharacterAsync(character.Id);
                    await view.SelectBrowseRegionAsync(null);
                    var albumSize = paths.AlbumSize ?? new Size(1280, 820);
                    if (paths.AlbumLight is not null)
                        await RenderArtworkLibrarySurfaceAsync(window, false, albumSize,
                            paths.AlbumLight, ArtworkLibrarySnapshotSurface.Album);
                    if (paths.AlbumDark is not null)
                        await RenderArtworkLibrarySurfaceAsync(window, true, albumSize,
                            paths.AlbumDark, ArtworkLibrarySnapshotSurface.Album);
                    if (paths.AlbumCompact is not null)
                        await RenderArtworkLibrarySurfaceAsync(window, false, new Size(1080, 820),
                            paths.AlbumCompact, ArtworkLibrarySnapshotSurface.Album);
                    var library = await service.LoadAsync(packages, settings);
                    if (paths.Hero is not null)
                    {
                        var hero = library.Items.FirstOrDefault(item =>
                            item.SourceThemeId == "cartethyia.gale-tide-crown" &&
                            item.Regions.Contains(ArtworkRegion.Hero) && item.Modes.Contains(ArtworkColorMode.Light))
                            ?? throw new InvalidOperationException("The current source library is missing the light home artwork.");
                        await view.SelectItemAsync(hero);
                        await view.SelectTargetAsync("cartethyia.gale-tide-crown", ArtworkRegion.Hero, ArtworkColorMode.Light);
                        await RenderArtworkLibrarySurfaceAsync(window, false, new Size(1280, 820), paths.Hero);
                        view.BackToGallery();
                    }

                    if (paths.Chat is not null)
                    {
                        var chat = library.Items.FirstOrDefault(item =>
                            item.SourceThemeId == "cartethyia.gale-tide-crown" &&
                            item.Regions.Contains(ArtworkRegion.Chat) && item.Modes.Contains(ArtworkColorMode.Dark))
                            ?? throw new InvalidOperationException("The current source library is missing the dark chat artwork.");
                        await view.SelectItemAsync(chat);
                        await view.SelectTargetAsync("cartethyia.gale-tide-crown", ArtworkRegion.Chat, ArtworkColorMode.Dark);
                        await RenderArtworkLibrarySurfaceAsync(window, true, new Size(1280, 820), paths.Chat);
                        view.BackToGallery();
                    }
                    if (paths.Compact is not null)
                    {
                        var sidebar = library.Items.FirstOrDefault(item =>
                            item.SourceThemeId == "cartethyia.gale-tide-crown" &&
                            item.Regions.Contains(ArtworkRegion.Sidebar) && item.Modes.Contains(ArtworkColorMode.Light))
                            ?? throw new InvalidOperationException("The current source library is missing the light sidebar artwork.");
                        await view.SelectItemAsync(sidebar);
                        await view.SelectTargetAsync("cartethyia.gale-tide-crown", ArtworkRegion.Sidebar, ArtworkColorMode.Light);
                        await RenderArtworkLibrarySurfaceAsync(window, false, new Size(1080, 820), paths.Compact);
                        view.BackToGallery();
                    }
                    var cardPackage = packages.Single(package => package.Manifest.Id == "cartethyia.gale-tide-crown");
                    if (paths.CardLight is not null)
                        await RenderArtworkLibraryCardDetailAsync(window, library.Items, cardPackage,
                            ArtworkRegion.TaskRightPrimary, ArtworkColorMode.Light, paths.CardLight);
                    if (paths.MemoryDark is not null)
                        await RenderArtworkLibraryCardDetailAsync(window, library.Items, cardPackage,
                            ArtworkRegion.Memory, ArtworkColorMode.Dark, paths.MemoryDark);
                    if (paths.DeleteLight is not null || paths.DeleteDark is not null)
                        await RenderArtworkLibraryDeleteDetailsAsync(window, service, cardPackage, paths.DeleteLight, paths.DeleteDark);
                }
                catch (Exception exception)
                {
                    failure = exception is TargetInvocationException invocation
                        ? invocation.InnerException ?? invocation : exception;
                }
                finally
                {
                    if (window is not null) await window.DisposeAsync();
                    application.Shutdown();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        try
        {
            if (failure is not null)
            {
                Console.Error.WriteLine(failure);
                return Task.FromResult(1);
            }
            foreach (var path in new[] { paths.CharactersLight, paths.CharactersDark, paths.AlbumLight,
                         paths.AlbumDark, paths.AlbumCompact, paths.Hero, paths.Chat, paths.Compact,
                         paths.CardLight, paths.MemoryDark, paths.DeleteLight, paths.DeleteDark }.Where(path => path is not null))
                Console.WriteLine($"Artwork library snapshot: {Path.GetFullPath(path!)}");
            return Task.FromResult(0);
        }
        finally
        {
            DeleteArtworkLibraryQaDirectory(data);
        }
    }

    private static async Task RenderArtworkLibraryDeleteDetailsAsync(MainWindow window, ArtworkLibraryService service,
        ThemePackage package, string? lightPath, string? darkPath)
    {
        var view = window.ArtworkLibraryPage;
        var original = view.Items.Single(item => item.IsBuiltIn && item.SourceThemeId == package.Manifest.Id &&
            item.Regions.Contains(ArtworkRegion.Hero) && item.Modes.Contains(ArtworkColorMode.Light));
        await view.SelectItemAsync(original);
        Ensure(!original.CanDelete && view.DeleteButton.Visibility == Visibility.Collapsed,
            "A theme original must have no delete entry before a personal copy is imported.");
        view.BackToGallery();
        var character = ArtworkLibraryService.GetCharacter(package);
        var imported = await service.ImportAsync(original.AbsolutePath, character.Id, character.Name, ArtworkRegion.Hero, null);
        await view.RefreshAsync();
        foreach (var (path, mode) in new[] { (lightPath, ArtworkColorMode.Light), (darkPath, ArtworkColorMode.Dark) })
        {
            if (path is null) continue;
            await view.SelectItemAsync(view.Items.Single(item => item.Id == imported.Id));
            await view.SelectTargetAsync(package.Manifest.Id, ArtworkRegion.Hero, mode);
            Ensure(view.SelectedItem is { CanDelete: true, IsBuiltIn: false } &&
                   view.DeleteButton.Visibility == Visibility.Visible && view.DeleteButton.IsEnabled &&
                   view.SelectedRegion == ArtworkRegion.Hero && view.SelectedMode == mode &&
                   string.Equals(view.PreviewIdentityPath, imported.AbsolutePath, StringComparison.OrdinalIgnoreCase),
                "The delete-action snapshot must preview the actual isolated personal import with its deletion entry available.");
            await RenderArtworkLibrarySurfaceAsync(window, mode == ArtworkColorMode.Dark, new Size(1280, 820), path);
            view.BackToGallery();
        }
    }

    private static async Task RenderArtworkLibraryCardDetailAsync(MainWindow window,
        IReadOnlyList<ArtworkLibraryItem> items, ThemePackage package,
        ArtworkRegion region, ArtworkColorMode mode, string snapshotPath)
    {
        var view = window.ArtworkLibraryPage;
        var expectedPath = package.AssetPaths[ExpectedLibraryAssetKey(package, region, mode)];
        var original = items.Single(item => item.IsBuiltIn && item.SourceThemeId == package.Manifest.Id &&
            string.Equals(item.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase));
        await view.SelectItemAsync(original);
        await view.SelectTargetAsync(package.Manifest.Id, region, mode);
        Ensure(view.SelectedThemeId == package.Manifest.Id && view.SelectedRegion == region && view.SelectedMode == mode &&
               view.SelectedItem?.Id == original.Id && view.IsPreviewReady && view.IsCurrentTargetDefault &&
               view.CurrentTargetImageLabel == "主题原图" &&
               string.Equals(view.PreviewIdentityPath, expectedPath, StringComparison.OrdinalIgnoreCase),
            $"The {region}/{mode} snapshot must show the actual native card original and its default target state.");
        await RenderArtworkLibrarySurfaceAsync(window, mode == ArtworkColorMode.Dark,
            new Size(1280, 820), snapshotPath);
        view.BackToGallery();
    }

    private static async Task RenderArtworkLibrarySurfaceAsync(
        MainWindow window, bool dark, Size size, string snapshotPath,
        ArtworkLibrarySnapshotSurface surface = ArtworkLibrarySnapshotSurface.Detail)
    {
        SetStudioThemeForSnapshot(window, dark);
        ArrangeMainSurface(window, size);
        window.InfoScroll.ScrollToTop();
        CompletePageAnimation(window.InfoPage);
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (surface == ArtworkLibrarySnapshotSurface.Detail)
            await WaitForArtworkLibraryPreviewAsync(window.ArtworkLibraryPage);
        ArrangeMainSurface(window, size);
        var view = window.ArtworkLibraryPage;
        var expectedSurfaceVisible = surface switch
        {
            ArtworkLibrarySnapshotSurface.Detail =>
                view.IsPreviewReady && view.DetailPage.Visibility == Visibility.Visible &&
                view.GalleryPage.Visibility == Visibility.Collapsed,
            ArtworkLibrarySnapshotSurface.Characters =>
                view.GalleryPage.Visibility == Visibility.Visible && view.DetailPage.Visibility == Visibility.Collapsed &&
                view.CharactersPanel.Visibility == Visibility.Visible && view.AlbumPanel.Visibility == Visibility.Collapsed &&
                view.SelectedCharacterId is null && view.RenderedCharacters.Count > 0 && view.RenderedItems.Count == 0,
            ArtworkLibrarySnapshotSurface.Album =>
                view.GalleryPage.Visibility == Visibility.Visible && view.DetailPage.Visibility == Visibility.Collapsed &&
                view.CharactersPanel.Visibility == Visibility.Collapsed && view.AlbumPanel.Visibility == Visibility.Visible &&
                view.SelectedCharacterId is not null && view.RenderedItems.Count > 0 &&
                view.RenderedItems.All(item => item.CharacterId == view.SelectedCharacterId),
            _ => false,
        };
        Ensure(expectedSurfaceVisible &&
               window.ArtworkLibraryPage.ActualWidth > 400 &&
               window.ArtworkLibraryPage.Visibility == Visibility.Visible,
            $"The current-source {surface} surface must be exclusively visible and fully loaded.");
        SaveWindowContent(window, snapshotPath);
    }

    private static Dictionary<string, ThemeVisualSettings> GetArtworkLibrarySettings(MainWindow window) =>
        (Dictionary<string, ThemeVisualSettings>)(typeof(MainWindow).GetField(
            "_themeVisualSettings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window)
            ?? throw new MissingFieldException(nameof(MainWindow), "_themeVisualSettings"));

    private static async Task WaitForArtworkLibraryPreviewAsync(ArtworkLibraryView view)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!view.IsPreviewReady && DateTime.UtcNow < deadline)
        {
            await Task.Delay(40);
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        Ensure(view.IsPreviewReady, "The selected artwork library preview did not finish loading.");
        await Dispatcher.Yield(DispatcherPriority.Render);
    }

    private static void DeleteArtworkLibraryQaDirectory(string directory)
    {
        var target = Path.GetFullPath(directory);
        var parent = Path.GetDirectoryName(target);
        Ensure((string.Equals(parent, ArtworkLibraryQaTemporaryRoot, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parent, ArtworkLibrarySnapshotDataRoot, StringComparison.OrdinalIgnoreCase)) &&
               Path.GetFileName(target).StartsWith('.'),
            "QA cleanup must stay within its isolated E-drive artwork-library directory.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    private static string ArtworkLibraryQaTemporaryRoot => Path.GetFullPath(Path.Combine(
        Path.GetPathRoot(FindRepositoryRoot())!, "Tessalume-QA", "artwork-library-temp"));

    private static string ArtworkLibrarySnapshotDataRoot => Path.GetFullPath(Path.Combine(
        FindRepositoryRoot(), "artifacts", "qa", "artwork-library"));
}
