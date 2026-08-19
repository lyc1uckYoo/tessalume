using System.Text.Json.Nodes;
using Tessalume.App.Features.Pets;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Pets;

internal static partial class TestSuite
{
    static async Task PublishedPetSourcesStaySeparateAndPackagesValidateAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectRoot = Path.Combine(repositoryRoot, "pet-projects");
        var packagesRoot = Path.Combine(repositoryRoot, "pets");
        var appProject = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Tessalume.App.csproj"));
        var buildScript = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "一键构建EXE.ps1"));

        Ensure(Directory.Exists(Path.Combine(projectRoot, "flying-snowfluff")) &&
               Directory.Exists(Path.Combine(projectRoot, "phoebe-jiubi")) &&
               !appProject.Contains("pet-projects", StringComparison.OrdinalIgnoreCase) &&
               !buildScript.Contains("pet-projects", StringComparison.OrdinalIgnoreCase),
            "Editable pet projects must stay outside executable and portable release resources.");

        var loader = new PetPackageLoader();
        var flying = await loader.LoadAsync(Path.Combine(packagesRoot, "flying-snowfluff"));
        var phoebe = await loader.LoadAsync(Path.Combine(packagesRoot, "phoebe-jiubi"));
        Ensure(flying.Validation.IsValid && flying.Package is not null &&
               flying.Package.Manifest.Id == "flying-snowfluff" &&
               flying.Package.Manifest.SpritesheetPath == "spritesheet.webp" &&
               flying.Package.SpritesheetInfo.Encoding == "VP8L" &&
               flying.Package.PreviewFiles.Count() == 11,
            "Flying Snowfluff must remain a complete published v2 WebP pet package.");
        Ensure(phoebe.Validation.IsValid && phoebe.Package is not null &&
               phoebe.Package.Manifest.Id == "phoebe-jiubi" &&
               phoebe.Package.Manifest.DisplayName == "菲比啾比" &&
               phoebe.Package.Manifest.SpritesheetPath == "spritesheet.png" &&
               phoebe.Package.SpritesheetInfo.Encoding == "APNG" &&
               phoebe.Package.SpritesheetInfo is { Width: 1536, Height: 2288, HasAlpha: true } &&
               phoebe.Package.PreviewFiles.Count() == 11,
            "Phoebe Jiubi must publish as a complete v2 APNG package with all product previews.");

        var phoebeProjectRoot = Path.Combine(projectRoot, "phoebe-jiubi");
        var phoebeProjectManifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            phoebeProjectRoot,
            PetDevelopmentProjectContract.ManifestFileName)))?.AsObject();
        Ensure(phoebeProjectManifest is not null &&
               File.Exists(Path.Combine(phoebeProjectRoot, "spritesheet.png")) &&
               phoebeProjectManifest["previewOutputDirectory"]?.GetValue<string>() ==
                   "build/final-motion-candidate" &&
               phoebeProjectManifest["protocol"]?["usedFrameCount"]?.GetValue<int>() == 74,
            "The tracked Phoebe Jiubi source project must keep its editable atlas and reproducible v2 contract without depending on ignored build output.");
    }

    static async Task PetGalleryUsesOneRefreshablePackageLibraryAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-pet-library-refresh-{Guid.NewGuid():N}");
        var themesDirectory = Path.Combine(root, "themes");
        var dataDirectory = Path.Combine(root, "data");
        Directory.CreateDirectory(themesDirectory);
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var layout = new PortableLayout(root, themesDirectory, dataDirectory);
            PetCopyDirectory(
                Path.Combine(repositoryRoot, "pets", "flying-snowfluff"),
                Path.Combine(layout.PetsDirectory, "flying-snowfluff"));
            PetCopyDirectory(
                Path.Combine(repositoryRoot, "pets", "phoebe-jiubi"),
                Path.Combine(layout.PetsDirectory, "phoebe-jiubi"));

            var galleryOptions = new PetGalleryServiceOptions(layout.PetsDirectory);
            using var gallery = new PetGalleryService(layout, galleryOptions);
            var snapshot = await gallery.ScanAsync();
            Ensure(snapshot.Entries.Count == 2 &&
                   snapshot.Entries.All(entry =>
                       entry.IsValid &&
                       entry.Package is not null &&
                       entry.SourceBadge == "正式宠物" &&
                       entry.PreviewFrames.Count == 11) &&
                   snapshot.Entries.Any(entry => entry.PetId == "flying-snowfluff") &&
                   snapshot.Entries.Any(entry => entry.PetId == "phoebe-jiubi"),
                "The gallery must expose the two published packages through one preview and install path.");

            var phoebeEntry = snapshot.Entries.Single(entry => entry.PetId == "phoebe-jiubi");
            var petOptions = new PetApplicationServiceOptions(
                Path.Combine(root, "isolated-codex-pets"),
                Path.Combine(root, "pet-backups"),
                Path.Combine(dataDirectory, "pet-center-state.v1.json"));
            using (var petService = new PetApplicationService(layout, petOptions))
            {
                petService.SelectEntry(phoebeEntry);
                var state = await petService.RefreshAsync();
                Ensure(state.Status == PetCenterStatus.NotInstalled &&
                       state.SourceBadge == "正式宠物" &&
                       state.PreviewFrames.Count == 11 &&
                       state.PrimaryAction == PetCenterAction.Install &&
                       state.PrimaryActionEnabled,
                    "A published card must open the normal animated detail and safe installer state.");
                var installed = await petService.InstallAsync(PetInstallIntent.Install);
                var installedRoot = Path.Combine(petOptions.CodexPetsRoot, "phoebe-jiubi");
                Ensure(installed.Status == PetCenterStatus.AwaitingCodexSelection &&
                       installed.CanUninstall &&
                       File.Exists(Path.Combine(installedRoot, "pet.json")) &&
                       File.Exists(Path.Combine(installedRoot, "spritesheet.png")) &&
                       !File.Exists(Path.Combine(installedRoot, "spritesheet.webp")),
                    "The safe installer must stage and install Phoebe Jiubi's declared APNG runtime file.");
            }

            var changedSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            gallery.PackagesChanged += (_, _) => changedSignal.TrySetResult();
            gallery.SetWatching(true);
            await Task.Delay(120);

            var catalogPath = Path.Combine(layout.PetsDirectory, "phoebe-jiubi", "catalog.json");
            var catalog = JsonNode.Parse(await File.ReadAllTextAsync(catalogPath))!.AsObject();
            catalog["productVersion"] = "1.0.1";
            catalog["previews"]!.AsArray()[0]!["label"] = "资源刷新验证";
            await File.WriteAllTextAsync(catalogPath, catalog.ToJsonString(
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            var signaled = await Task.WhenAny(
                changedSignal.Task,
                Task.Delay(TimeSpan.FromSeconds(5)));
            Ensure(ReferenceEquals(signaled, changedSignal.Task),
                "The active gallery must debounce dist/pets resource changes into a refresh signal.");

            var refreshed = await gallery.ScanAsync();
            var refreshedPhoebe = refreshed.Entries.Single(entry => entry.PetId == "phoebe-jiubi");
            Ensure(refreshedPhoebe.Version == "1.0.1" &&
                   refreshedPhoebe.PreviewFrames.Single(frame => frame.Key == "idle").Label ==
                       "资源刷新验证" &&
                   refreshedPhoebe.IsValid,
                "Refreshing the package library must immediately use changed resources without rebuilding the EXE.");

            gallery.SetWatching(false);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static async Task PetGalleryPresentsOneUnifiedPreviewSurfaceAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var galleryXaml = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Features",
            "Pets",
            "PetGalleryView.xaml"));
        var gallerySource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Features",
            "Pets",
            "PetGalleryView.xaml.cs"));
        var serviceSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Features",
            "Pets",
            "PetGalleryService.cs"));
        var shellSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Shell",
            "Pets",
            "MainWindow.Pets.cs"));

        Ensure(!galleryXaml.Contains("开发预览", StringComparison.Ordinal) &&
               !galleryXaml.Contains("DevelopmentFilterButton", StringComparison.Ordinal) &&
               galleryXaml.Contains("资源更新后刷新画廊即可重新载入预览", StringComparison.Ordinal) &&
               gallerySource.Contains("Entry.CanOpen ? \"查看并安装\" : \"资源不可用\"", StringComparison.Ordinal) &&
               !gallerySource.Contains("IsDevelopment", StringComparison.Ordinal) &&
               !serviceSource.Contains("PetDevelopmentProjectLoader", StringComparison.Ordinal) &&
               serviceSource.Contains("new PetLibraryWatcher(_options.PackagesRoot)", StringComparison.Ordinal) &&
               shellSource.Contains("ReloadSelectedPetEntryAsync", StringComparison.Ordinal) &&
               !shellSource.Contains("RefreshDevelopmentPreview", StringComparison.Ordinal),
            "The product must present one formal preview surface backed by the refreshable pets library.");
    }
}
