using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessalume.App.Features.Pets;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Pets;

internal static partial class TestSuite
{
    static async Task PetDevelopmentProjectsValidateAndStayOutOfReleaseAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceProjectRoot = Path.Combine(
            repositoryRoot,
            "pet-projects",
            PetApplicationService.BuiltInPetId);
        var publishedRoot = Path.Combine(
            repositoryRoot,
            "pets",
            PetApplicationService.BuiltInPetId);
        Ensure(File.Exists(Path.Combine(sourceProjectRoot, "pet-project.json")) &&
               File.Exists(Path.Combine(sourceProjectRoot, "tools", "build_smooth_pet.py")) &&
               Directory.Exists(Path.Combine(sourceProjectRoot, "assets", "keyframes")) &&
               !File.Exists(Path.Combine(sourceProjectRoot, "catalog.json")),
            "The migrated pet project must retain editable sources without masquerading as a published package.");
        Ensure(ComputePetDevelopmentSha256(Path.Combine(sourceProjectRoot, "spritesheet.webp")) ==
               ComputePetDevelopmentSha256(Path.Combine(publishedRoot, "spritesheet.webp")),
            "The migrated Flying Snowfluff project must begin from the currently published atlas.");

        var root = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-pet-development-contract-{Guid.NewGuid():N}");
        try
        {
            var projectRoot = await CreatePetDevelopmentFixtureAsync(root);
            var manifestPath = Path.Combine(
                projectRoot,
                PetDevelopmentProjectContract.ManifestFileName);
            var originalManifest = await File.ReadAllTextAsync(manifestPath);
            var loader = new PetDevelopmentProjectLoader();
            var valid = await loader.LoadAsync(projectRoot);
            Ensure(valid.Validation.IsValid && valid.Project is not null &&
                   valid.Project.Manifest.Id == PetApplicationService.BuiltInPetId &&
                   valid.Project.Manifest.Previews.Count == 11 &&
                   valid.Project.PreviewFiles.Count() == 11 &&
                   valid.Project.PreviewFiles.Select(preview => preview.Metadata.ActionKey)
                       .SequenceEqual(PetDevelopmentProjectContract.RequiredPreviewActionKeys) &&
                   valid.Project.PreviewFiles.All(preview =>
                       preview.GifInfo.FrameCount == preview.Metadata.ExpectedFrameCount &&
                       Path.GetFullPath(preview.FullPath).StartsWith(
                           Path.GetFullPath(valid.Project.PreviewOutputDirectory) + Path.DirectorySeparatorChar,
                           StringComparison.OrdinalIgnoreCase)),
                "A valid development project must expose all 11 bounded GIF previews from its own candidate output.");

            await MutatePetDevelopmentManifestAsync(
                manifestPath,
                originalManifest,
                rootNode => rootNode["previews"]!.AsArray()[0]!["path"] = "../escape.gif");
            var traversal = await loader.LoadAsync(projectRoot);
            Ensure(!traversal.Validation.IsValid &&
                   traversal.Validation.Issues.Any(issue => issue.Code == "project.preview.path.invalid"),
                "Development preview paths must reject traversal.");

            await MutatePetDevelopmentManifestAsync(
                manifestPath,
                originalManifest,
                rootNode => rootNode["previews"]!.AsArray()[0]!["path"] =
                    "https://example.invalid/idle.gif");
            var remote = await loader.LoadAsync(projectRoot);
            Ensure(!remote.Validation.IsValid &&
                   remote.Validation.Issues.Any(issue => issue.Code == "project.preview.path.invalid"),
                "Development previews must reject remote resources.");

            await MutatePetDevelopmentManifestAsync(
                manifestPath,
                originalManifest,
                rootNode => rootNode["previews"]!.AsArray()[0]!["expectedFrameCount"] = 7);
            var wrongFrames = await loader.LoadAsync(projectRoot);
            Ensure(!wrongFrames.Validation.IsValid &&
                   wrongFrames.Validation.Issues.Any(issue => issue.Code == "project.preview.frames.mismatch"),
                "Development previews must reject a GIF whose actual frame count differs from the project contract.");

            await File.WriteAllTextAsync(manifestPath, originalManifest);
            var missingPreview = Path.Combine(
                projectRoot,
                "build",
                "final-motion-candidate",
                "previews",
                "00-idle.gif");
            File.Delete(missingPreview);
            var missing = await loader.LoadAsync(projectRoot);
            Ensure(!missing.Validation.IsValid &&
                   missing.Validation.Issues.Any(issue => issue.Code == "project.preview.unavailable"),
                "A missing candidate GIF must make the development project visibly invalid.");

            var appProject = await File.ReadAllTextAsync(Path.Combine(
                repositoryRoot,
                "src",
                "Tessalume.App",
                "Tessalume.App.csproj"));
            var buildScript = await File.ReadAllTextAsync(Path.Combine(
                repositoryRoot,
                "一键构建EXE.ps1"));
            var ignoreFile = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, ".gitignore"));
            Ensure(!appProject.Contains("pet-projects", StringComparison.OrdinalIgnoreCase) &&
                   !buildScript.Contains("pet-projects", StringComparison.OrdinalIgnoreCase) &&
                   ignoreFile.Contains("/pet-projects/*/build/", StringComparison.Ordinal) &&
                   ignoreFile.Contains("/pet-projects/*/tmp/", StringComparison.Ordinal),
                "Development sources and generated candidates must stay outside embedded and portable release assets.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task PetGallerySeparatesOfficialAndLiveDevelopmentEntriesAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-pet-gallery-service-{Guid.NewGuid():N}");
        var themesDirectory = Path.Combine(root, "themes");
        var dataDirectory = Path.Combine(root, "data");
        var projectsRoot = Path.Combine(root, "pet-projects");
        Directory.CreateDirectory(themesDirectory);
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var projectRoot = await CreatePetDevelopmentFixtureAsync(projectsRoot);
            var layout = new PortableLayout(root, themesDirectory, dataDirectory);
            var galleryOptions = new PetGalleryServiceOptions(
                layout.PetsDirectory,
                projectsRoot);
            using var gallery = new PetGalleryService(layout, galleryOptions);
            var snapshot = await gallery.ScanAsync();
            var matching = snapshot.Entries
                .Where(entry => entry.PetId == PetApplicationService.BuiltInPetId)
                .ToArray();
            var official = matching.Single(entry => !entry.IsDevelopment);
            var development = matching.Single(entry => entry.IsDevelopment);
            Ensure(matching.Length == 2 &&
                   official.IsValid && official.Package is not null &&
                   official.DevelopmentProject is null &&
                   official.SourceBadge == "官方宠物" &&
                   development.IsValid && development.Package is null &&
                   development.DevelopmentProject is not null &&
                   development.SourceBadge == "开发预览" &&
                   development.PreviewFrames.Count == 11 &&
                   development.PreviewFrames.All(frame => !string.IsNullOrWhiteSpace(frame.Revision)) &&
                   snapshot.DevelopmentEntries.Count == 1 &&
                   snapshot.OfficialEntries.Count == 1,
                "The gallery must keep an official release and its editable development project as two explicit cards.");

            var petOptions = new PetApplicationServiceOptions(
                Path.Combine(root, "isolated-codex-pets"),
                Path.Combine(root, "pet-backups"),
                Path.Combine(dataDirectory, "pet-center-state.v1.json"));
            using (var petService = new PetApplicationService(layout, petOptions))
            {
                petService.SelectEntry(development);
                var state = await petService.RefreshAsync();
                Ensure(state.Status == PetCenterStatus.DevelopmentPreview &&
                       state.StatusTitle == "实时监看中" &&
                       state.IsDevelopmentPreview &&
                       !state.ShowPrimaryAction &&
                       !state.ShowInstallationManagement &&
                       state.PreviewFrames.Count == 11 &&
                       state.InstallLocation == projectRoot &&
                       state.LocationLabel == "项目位置",
                    "Opening a development card must yield a generic live-preview detail without installer controls.");
                InvalidOperationException? installFailure = null;
                try
                {
                    _ = await petService.InstallAsync(PetInstallIntent.Install);
                }
                catch (InvalidOperationException exception)
                {
                    installFailure = exception;
                }
                Ensure(installFailure?.Message.Contains(
                           "开发预览不会连接正式宠物安装器",
                           StringComparison.Ordinal) == true &&
                       (!Directory.Exists(petOptions.CodexPetsRoot) ||
                        !Directory.EnumerateFileSystemEntries(
                            petOptions.CodexPetsRoot,
                            "*",
                            SearchOption.AllDirectories).Any()),
                    "A development preview must refuse installation before writing any Codex Pets content.");
            }

            var idlePath = Path.Combine(
                projectRoot,
                "build",
                "final-motion-candidate",
                "previews",
                "00-idle.gif");
            var idleBytes = await File.ReadAllBytesAsync(idlePath);
            var originalRevision = development.PreviewFrames.Single(frame => frame.Key == "idle").Revision;
            await File.WriteAllBytesAsync(idlePath, [0x47, 0x49, 0x46]);
            var unstableSnapshot = await gallery.ScanAsync();
            var retained = unstableSnapshot.DevelopmentEntries.Single();
            Ensure(retained.UsesLastGoodPreview && retained.CanOpen &&
                   retained.PreviewFrames.Single(frame => frame.Key == "idle").Revision == originalRevision,
                "A partially written candidate must retain the last complete preview instead of flashing a broken card.");

            await File.WriteAllBytesAsync(idlePath, idleBytes);
            File.SetLastWriteTimeUtc(idlePath, DateTime.UtcNow.AddMinutes(2));
            var refreshedSnapshot = await gallery.ScanAsync();
            var refreshed = refreshedSnapshot.DevelopmentEntries.Single();
            Ensure(refreshed.IsValid && !refreshed.UsesLastGoodPreview &&
                   refreshed.PreviewFrames.Single(frame => frame.Key == "idle").Revision != originalRevision,
                "A stable regenerated candidate must publish a new revision so the active player reloads the same action path.");

            var watcherSignals = 0;
            var watcherSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            gallery.DevelopmentProjectsChanged += (_, _) =>
            {
                Interlocked.Increment(ref watcherSignals);
                watcherSignal.TrySetResult();
            };
            gallery.SetWatching(true);
            await Task.Delay(120);
            var versionPath = Path.Combine(projectRoot, "VERSION");
            var versionText = await File.ReadAllTextAsync(versionPath);
            await File.WriteAllTextAsync(versionPath, versionText + Environment.NewLine);
            var signaled = await Task.WhenAny(watcherSignal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Ensure(ReferenceEquals(signaled, watcherSignal.Task),
                "The active gallery must debounce a local Codex project update into a live-refresh signal.");
            gallery.SetWatching(false);
            await Task.Delay(800);
            var stoppedCount = Volatile.Read(ref watcherSignals);
            await File.WriteAllTextAsync(versionPath, versionText);
            await Task.Delay(900);
            Ensure(Volatile.Read(ref watcherSignals) == stoppedCount,
                "Leaving the pet route must stop the development-project watcher.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task PetGalleryViewRoutesCardsToGenericAnimatedDetailAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-pet-gallery-view-{Guid.NewGuid():N}");
        var themesDirectory = Path.Combine(root, "themes");
        var dataDirectory = Path.Combine(root, "data");
        var projectsRoot = Path.Combine(root, "pet-projects");
        Directory.CreateDirectory(themesDirectory);
        Directory.CreateDirectory(dataDirectory);
        try
        {
            _ = await CreatePetDevelopmentFixtureAsync(projectsRoot);
            var layout = new PortableLayout(root, themesDirectory, dataDirectory);
            var galleryOptions = new PetGalleryServiceOptions(layout.PetsDirectory, projectsRoot);
            PetGallerySnapshot snapshot;
            PetCenterPresentationState developmentState;
            using (var gallery = new PetGalleryService(layout, galleryOptions))
            {
                snapshot = await gallery.ScanAsync();
            }
            var development = snapshot.DevelopmentEntries.Single();
            var petOptions = new PetApplicationServiceOptions(
                Path.Combine(root, "isolated-codex-pets"),
                Path.Combine(root, "pet-backups"),
                Path.Combine(dataDirectory, "pet-center-state.v1.json"));
            using (var petService = new PetApplicationService(layout, petOptions))
            {
                petService.SelectEntry(development);
                developmentState = await petService.RefreshAsync();
            }

            Exception? failure = null;
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                MainWindow? window = null;
                PetCenterView? view = null;
                ScrollViewer? host = null;
                try
                {
                    window = new MainWindow(layout, petOptions, galleryOptions);
                    InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                    view = window.PetCenterPage;
                    var pageParent = LogicalTreeHelper.GetParent(view) as Panel
                        ?? throw new InvalidOperationException("The pet page must have a detachable shell host.");
                    pageParent.Children.Remove(view);
                    view.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri(
                            "pack://application:,,,/Tessalume;component/Styles/MainWindowResources.xaml",
                            UriKind.Absolute),
                    });
                    host = new ScrollViewer
                    {
                        Content = view,
                        Width = 1120,
                        Height = 760,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Background = Brushes.Transparent,
                    };
                    PetGalleryEntry? requested = null;
                    bool backRequested = false;
                    typeof(PetCenterView).GetField(
                            "PetRequested",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic)
                        ?.SetValue(view, null);
                    typeof(PetCenterView).GetField(
                            "BackToGalleryRequested",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic)
                        ?.SetValue(view, null);
                    view.PetRequested += (_, entry) => requested = entry;
                    view.BackToGalleryRequested += (_, _) => backRequested = true;
                    view.RenderGallery(snapshot);
                    ArrangePetCenter(host, 1120, 760);
                    Ensure(view.IsShowingGallery &&
                           view.GalleryPanel.GalleryItems.Items.Count == 2 &&
                           view.GalleryPanel.GallerySection.Visibility == Visibility.Visible &&
                           view.GalleryPanel.EmptyState.Visibility == Visibility.Collapsed,
                        "The gallery page must show official and development cards before entering a detail.");

                    view.GalleryPanel.DevelopmentFilterButton.RaiseEvent(
                        new RoutedEventArgs(ButtonBase.ClickEvent));
                    Ensure(view.GalleryPanel.GalleryItems.Items.Count == 1,
                        "The development filter must isolate live project cards.");
                    view.GalleryPanel.SearchBox.Text = "不存在的宠物";
                    Ensure(view.GalleryPanel.GalleryItems.Items.Count == 0 &&
                           view.GalleryPanel.EmptyState.Visibility == Visibility.Visible,
                        "Gallery search must expose a clear empty state.");
                    view.GalleryPanel.SearchBox.Text = string.Empty;
                    view.GalleryPanel.AllFilterButton.RaiseEvent(
                        new RoutedEventArgs(ButtonBase.ClickEvent));
                    ArrangePetCenter(host, 1120, 760);
                    var cardClick = typeof(PetGalleryView).GetMethod(
                        "PetCard_Click",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(nameof(PetGalleryView), "PetCard_Click");
                    cardClick.Invoke(
                        view.GalleryPanel,
                        [new Button { Tag = development }, new RoutedEventArgs(ButtonBase.ClickEvent)]);
                    Ensure(ReferenceEquals(requested, development),
                        "Selecting a development card must route the exact gallery entry to the generic detail page.");

                    view.Render(developmentState);
                    ArrangePetCenter(host, 1120, 760);
                    view.PreviewPlayer.SetActive(true);
                    AwaitWithDispatcher(view.PreviewPlayer.WaitForCurrentLoadAsync());
                    var firstIndex = view.PreviewPlayer.CurrentFrameIndex;
                    var firstSource = view.PetPreviewImage.Source;
                    for (var index = 0; index < 12 &&
                         view.PreviewPlayer.CurrentFrameIndex == firstIndex; index++)
                    {
                        AwaitWithDispatcher(Task.Delay(120));
                    }
                    Ensure(!view.IsShowingGallery &&
                           developmentState.PreviewFrames.Count == 11 &&
                           view.PrimaryActionGroup.Visibility == Visibility.Collapsed &&
                           view.DetailPageSubtitleText.Text.Contains("项目状态", StringComparison.Ordinal) &&
                           view.RestoreBackupButton.Visibility == Visibility.Collapsed &&
                           view.UninstallButton.Visibility == Visibility.Collapsed &&
                           view.RefreshButton.Visibility == Visibility.Visible &&
                           Equals(view.RefreshButton.Content, "↻  立即刷新") &&
                           view.PreviewPlayer.CurrentKey == "idle" &&
                           view.PreviewPlayer.DecodedFrameCount == 6 &&
                           view.PreviewPlayer.CurrentFrameIndex != firstIndex &&
                           !ReferenceEquals(firstSource, view.PetPreviewImage.Source) &&
                           view.PetPreviewImage.Source is BitmapSource,
                        "A development detail must play the selected candidate GIF while keeping formal installation controls absent.");

                    view.BackToGalleryButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Ensure(backRequested && view.IsShowingGallery &&
                           !view.PreviewPlayer.IsAnimating &&
                           view.PreviewPlayer.DecodedFrameCount == 0,
                        "Returning to the gallery must release live GIF frames and restore the card view.");
                    host.Content = null;
                }
                catch (Exception exception)
                {
                    failure = exception is System.Reflection.TargetInvocationException invocation
                        ? invocation.InnerException ?? invocation
                        : exception;
                }
                finally
                {
                    view?.Dispose();
                    if (window is not null)
                    {
                        AwaitWithDispatcher(window.DisposeAsync().AsTask());
                    }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is not null)
            {
                throw new InvalidOperationException(
                    "The pet gallery card, generic detail, and live preview UI contract failed.",
                    failure);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreatePetDevelopmentFixtureAsync(string parentRoot)
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceProjectRoot = Path.Combine(
            repositoryRoot,
            "pet-projects",
            PetApplicationService.BuiltInPetId);
        var publishedRoot = Path.Combine(
            repositoryRoot,
            "pets",
            PetApplicationService.BuiltInPetId);
        var targetRoot = Path.Combine(parentRoot, PetApplicationService.BuiltInPetId);
        Directory.CreateDirectory(targetRoot);
        foreach (var fileName in new[]
                 {
                     PetDevelopmentProjectContract.ManifestFileName,
                     PetPackageContract.ManifestFileName,
                     "spritesheet.webp",
                     "VERSION",
                 })
        {
            File.Copy(
                Path.Combine(sourceProjectRoot, fileName),
                Path.Combine(targetRoot, fileName),
                overwrite: true);
        }

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            targetRoot,
            PetDevelopmentProjectContract.ManifestFileName)));
        var outputRelativePath = manifest.RootElement
            .GetProperty("previewOutputDirectory")
            .GetString()!;
        var outputRoot = Path.Combine(targetRoot, outputRelativePath.Replace('/', Path.DirectorySeparatorChar));
        foreach (var preview in manifest.RootElement.GetProperty("previews").EnumerateArray())
        {
            var relativePath = preview.GetProperty("path").GetString()!;
            var platformPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.Combine(outputRoot, platformPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var source = Path.Combine(publishedRoot, platformPath);
            if (!File.Exists(source))
            {
                source = Path.Combine(publishedRoot, "previews", Path.GetFileName(platformPath));
            }
            File.Copy(source, destination, overwrite: true);
        }
        return targetRoot;
    }

    private static async Task MutatePetDevelopmentManifestAsync(
        string manifestPath,
        string originalManifest,
        Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(originalManifest)?.AsObject()
            ?? throw new InvalidDataException("Unable to parse the pet development fixture.");
        mutate(node);
        await File.WriteAllTextAsync(
            manifestPath,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ComputePetDevelopmentSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
