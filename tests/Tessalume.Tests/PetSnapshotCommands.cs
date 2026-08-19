using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Pets;
using Tessalume.Core.Pets;

internal static partial class TestSuite
{
    static Task<int> RenderPetGallerySnapshotsAsync(
        string galleryLightPath,
        string galleryDarkPath,
        string detailLightPath,
        string detailDarkPath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var portableRoot = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-pet-gallery-snapshot-{Guid.NewGuid():N}");
        var themes = Path.Combine(portableRoot, "themes");
        var data = Path.Combine(portableRoot, "data");
        Directory.CreateDirectory(themes);
        Directory.CreateDirectory(data);
        var petOptions = new PetApplicationServiceOptions(
            Path.Combine(portableRoot, "codex-pets"),
            Path.Combine(portableRoot, "pet-backups"),
            Path.Combine(data, "pet-center-state.v1.json"));
        var galleryOptions = new PetGalleryServiceOptions(
            Path.Combine(repositoryRoot, "pets"));
        Exception? failure = null;

        using (var preferences = new UiPreferencesStore(data))
        {
            preferences.SaveAsync(new UiPreferences { OnboardingCompleted = true })
                .GetAwaiter()
                .GetResult();
        }

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MainWindow? window = null;
            PetGalleryService? galleryService = null;

            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    var layout = new PortableLayout(portableRoot, themes, data);
                    window = new MainWindow(layout, petOptions, galleryOptions);
                    InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                    InvokeMainWindowMethod(window, "NavigateTo", AppRoute.Pets);
                    CompleteInfoPageTransition(window);
                    galleryService = new PetGalleryService(layout, galleryOptions);
                    var snapshot = await galleryService.ScanAsync();
                    Ensure(snapshot.Entries.Count >= 2,
                        "Gallery snapshots require both published pet packages.");

                    await RenderPetGalleryProfileAsync(
                        window,
                        snapshot,
                        galleryLightPath,
                        darkMode: false,
                        new Size(1600, 900));
                    await RenderPetGalleryProfileAsync(
                        window,
                        snapshot,
                        galleryDarkPath,
                        darkMode: true,
                        new Size(1366, 768));

                    var selected = snapshot.Entries.First(entry =>
                        string.Equals(entry.PetId, "phoebe-jiubi", StringComparison.Ordinal));
                    var detailState = new PetCenterPresentationState
                    {
                        PetId = selected.PetId,
                        DisplayName = selected.DisplayName,
                        Description = selected.Description,
                        SourceBadge = selected.SourceBadge,
                        Status = PetCenterStatus.NotInstalled,
                        StatusTitle = "尚未安装",
                        StatusDetail = selected.HealthMessage,
                        ProductVersion = selected.Version,
                        ProtocolSummary = selected.ProtocolSummary,
                        Author = selected.Author,
                        LicenseSummary = selected.LicenseSummary,
                        InstallLocation = petOptions.CodexPetsRoot,
                        PrimaryAction = PetCenterAction.Install,
                        PrimaryActionText = $"安装{selected.DisplayName}",
                        RecommendedThemeId = selected.RecommendedThemeId,
                        RecommendedThemeName = selected.RecommendedThemeName,
                        HasRecommendedTheme = !string.IsNullOrWhiteSpace(selected.RecommendedThemeId),
                        PreviewFrames = selected.PreviewFrames,
                    };
                    await RenderPetDevelopmentProfileAsync(
                        window,
                        detailState,
                        detailLightPath,
                        darkMode: false,
                        new Size(1600, 900),
                        "idle");
                    await RenderPetDevelopmentProfileAsync(
                        window,
                        detailState,
                        detailDarkPath,
                        darkMode: true,
                        new Size(1366, 768),
                        "showcase");
                }
                catch (Exception exception)
                {
                    failure = exception is TargetInvocationException invocation
                        ? invocation.InnerException ?? invocation
                        : exception;
                }
                finally
                {
                    galleryService?.Dispose();
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
            Console.WriteLine($"Pet gallery light snapshot: {Path.GetFullPath(galleryLightPath)}");
            Console.WriteLine($"Pet gallery dark snapshot: {Path.GetFullPath(galleryDarkPath)}");
            Console.WriteLine($"Pet detail light snapshot: {Path.GetFullPath(detailLightPath)}");
            Console.WriteLine($"Pet detail dark snapshot: {Path.GetFullPath(detailDarkPath)}");
            return Task.FromResult(0);
        }
        finally
        {
            if (Directory.Exists(portableRoot)) Directory.Delete(portableRoot, recursive: true);
        }
    }

    private static Task RenderPetGalleryProfileAsync(
        MainWindow window,
        PetGallerySnapshot snapshot,
        string snapshotPath,
        bool darkMode,
        Size size)
    {
        InvokeMainWindowMethod(window, "ApplyStudioTheme", darkMode);
        ArrangeMainSurface(window, size);
        window.PetCenterPage.RenderGallery(snapshot);
        window.InfoScroll.ScrollToTop();
        ArrangeMainSurface(window, size);
        SaveWindowContent(window, snapshotPath);
        return Task.CompletedTask;
    }

    private static async Task RenderPetDevelopmentProfileAsync(
        MainWindow window,
        PetCenterPresentationState state,
        string snapshotPath,
        bool darkMode,
        Size size,
        string previewKey)
    {
        InvokeMainWindowMethod(window, "ApplyStudioTheme", darkMode);
        ArrangeMainSurface(window, size);
        window.PetCenterPage.Render(state);
        window.PetCenterPage.PreviewPlayer.Select(previewKey);
        window.PetCenterPage.PreviewPlayer.SetActive(true);
        await window.PetCenterPage.PreviewPlayer.WaitForCurrentLoadAsync();
        await Task.Delay(160);
        ArrangeMainSurface(window, size);
        window.InfoScroll.ScrollToTop();
        SaveWindowContent(window, snapshotPath);
    }

    static Task<int> RenderPetCenterSnapshotsAsync(
        string lightSnapshotPath,
        string darkSnapshotPath,
        string? compactSnapshotPath = null) =>
        RenderPetCenterSnapshotSetAsync(
        [
            new(lightSnapshotPath, false, new Size(1600, 900), "idle", "light 1600x900"),
            new(darkSnapshotPath, true, new Size(1600, 900), "showcase", "dark 1600x900"),
            .. compactSnapshotPath is null
                ? Array.Empty<PetCenterSnapshotProfile>()
                : [new(compactSnapshotPath, false, new Size(900, 720), "idle", "compact 900x720")],
        ]);

    static Task<int> RenderPetCenterV4SnapshotsAsync(
        string light1600SnapshotPath,
        string dark1366SnapshotPath,
        string light1266SnapshotPath,
        string compact900SnapshotPath) =>
        RenderPetCenterSnapshotSetAsync(
        [
            new(light1600SnapshotPath, false, new Size(1600, 900), "idle", "light 1600x900"),
            new(dark1366SnapshotPath, true, new Size(1366, 768), "showcase", "dark 1366x768"),
            new(light1266SnapshotPath, false, new Size(1266, 813), "idle", "light 1266x813"),
            new(compact900SnapshotPath, false, new Size(900, 720), "idle", "compact 900x720"),
        ]);

    static Task<int> RenderPetCenterV5SnapshotsAsync(
        string light1600SnapshotPath,
        string dark1366SnapshotPath,
        string light1266SnapshotPath,
        string compact900SnapshotPath) =>
        RenderPetCenterSnapshotSetAsync(
        [
            new(light1600SnapshotPath, false, new Size(1600, 900), "idle", "V5 light 1600x900"),
            new(dark1366SnapshotPath, true, new Size(1366, 768), "showcase", "V5 dark 1366x768"),
            new(light1266SnapshotPath, false, new Size(1266, 813), "idle", "V5 light 1266x813"),
            new(compact900SnapshotPath, false, new Size(900, 720), "idle", "V5 compact 900x720"),
        ]);

    private static Task<int> RenderPetCenterSnapshotSetAsync(
        IReadOnlyList<PetCenterSnapshotProfile> profiles)
    {
        var portableRoot = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-pet-center-snapshot-{Guid.NewGuid():N}");
        var themes = Path.Combine(portableRoot, "themes");
        var data = Path.Combine(portableRoot, "data");
        var previews = Path.Combine(
            FindRepositoryRoot(),
            "pets",
            "flying-snowfluff",
            "previews");
        Directory.CreateDirectory(themes);
        Directory.CreateDirectory(data);
        var packageResult = new PetPackageLoader()
            .LoadAsync(Directory.GetParent(previews)!.FullName)
            .GetAwaiter()
            .GetResult();
        Ensure(packageResult.Validation.IsValid && packageResult.Package is not null,
            "The snapshot command requires the validated built-in Flying Snowfluff package.");
        var package = packageResult.Package
            ?? throw new InvalidOperationException(
                "The validated Flying Snowfluff package is unavailable.");
        var productFrames = package.PreviewFiles
            .Select(frame => new PetPreviewFrame(
                frame.Metadata.ActionKey,
                frame.Metadata.Label ?? frame.Metadata.ActionKey,
                frame.FullPath,
                frame.Metadata.Kind,
                frame.GifInfo.FrameCount,
                frame.GifInfo.Width,
                frame.GifInfo.Height,
                frame.Metadata.RepresentativeFrame))
            .ToArray();
        Ensure(productFrames.Length == 11,
            "The Flying Snowfluff product snapshot requires all eleven animated previews.");
        var petOptions = new PetApplicationServiceOptions(
            Path.Combine(portableRoot, "codex-pets"),
            Path.Combine(portableRoot, "pet-backups"),
            Path.Combine(data, "pet-center-state.v1.json"));
        Exception? failure = null;

        using (var preferences = new UiPreferencesStore(data))
        {
            preferences.SaveAsync(new UiPreferences { OnboardingCompleted = true })
                .GetAwaiter()
                .GetResult();
        }

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MainWindow? window = null;

            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    window = new MainWindow(
                        new PortableLayout(portableRoot, themes, data),
                        petOptions);
                    InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                    ClearPetCenterEventHandlers(window.PetCenterPage);
                    InvokeMainWindowMethod(window, "NavigateTo", AppRoute.Pets);
                    CompleteInfoPageTransition(window);

                    var state = new PetCenterPresentationState
                    {
                        Status = PetCenterStatus.AwaitingCodexSelection,
                        StatusTitle = "等待在 Codex 中选择",
                        StatusDetail = "文件已安全安装，等待你在 Codex 中完成选择。",
                        ProductVersion = package.Catalog.ProductVersion,
                        ProtocolSummary =
                            $"图集协议 v{package.Catalog.Protocol.SpriteVersionNumber} · " +
                            $"{Math.Max(0, package.Catalog.Protocol.States.Count - 2)} 种动作 · " +
                            $"{package.Catalog.Protocol.States.TakeLast(2).Sum(item => item.Frames)} 向转身 · " +
                            $"{package.Catalog.Protocol.UsedFrameCount} 有效格",
                        Author = package.Catalog.Author.Name,
                        LicenseSummary = package.Catalog.License.Name ?? package.Catalog.License.Kind,
                        InstallLocation = "当前用户 .codex\\pets",
                        PrimaryAction = PetCenterAction.OpenCodex,
                        PrimaryActionText = "打开 Codex",
                        PrimaryActionEnabled = true,
                        CanAcknowledgeSelection = true,
                        CanUninstall = true,
                        CanRestoreBackup = false,
                        LatestBackupLabel = null,
                        PreviewFrames = productFrames,
                    };

                    foreach (var profile in profiles)
                    {
                        await RenderPetCenterSnapshotProfileAsync(
                            window,
                            state,
                            profile.Path,
                            profile.DarkMode,
                            profile.Size,
                            profile.PreviewKey);
                    }
                }
                catch (Exception exception)
                {
                    failure = exception is TargetInvocationException invocation
                        ? invocation.InnerException ?? invocation
                        : exception;
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

            foreach (var profile in profiles)
            {
                Console.WriteLine(
                    $"Pet Center {profile.Label} snapshot: {Path.GetFullPath(profile.Path)}");
            }
            return Task.FromResult(0);
        }
        finally
        {
            if (Directory.Exists(portableRoot)) Directory.Delete(portableRoot, recursive: true);
        }
    }

    private static async Task RenderPetCenterSnapshotProfileAsync(
        MainWindow window,
        PetCenterPresentationState state,
        string snapshotPath,
        bool darkMode,
        Size size,
        string previewKey)
    {
        InvokeMainWindowMethod(window, "ApplyStudioTheme", darkMode);
        ArrangeMainSurface(window, size);
        window.PetCenterPage.Render(state);
        window.PetCenterPage.PreviewPlayer.Select(previewKey);
        window.PetCenterPage.PreviewPlayer.SetActive(true);
        await window.PetCenterPage.PreviewPlayer.WaitForCurrentLoadAsync();
        await Task.Delay(220);
        ArrangeMainSurface(window, size);
        window.InfoScroll.ScrollToTop();
        ArrangeMainSurface(window, size);
        var previewButtons = window.PetCenterPage.DailyActionsPanel.Children.OfType<ToggleButton>()
            .Concat(window.PetCenterPage.TaskActionsPanel.Children.OfType<ToggleButton>())
            .Concat(window.PetCenterPage.ViewActionsPanel.Children.OfType<ToggleButton>())
            .ToArray();
        var requiredElements = new FrameworkElement[]
        {
            window.PetCenterPage.PreviewStage,
            window.PetCenterPage.InstallationStatusTitle,
            window.PetCenterPage.PrimaryActionButton,
            window.PetCenterPage.AcknowledgeSelectionButton,
            window.PetCenterPage.ActivationGuidePanel,
            window.PetCenterPage.ActionSelector,
            FindPetButton(window.PetCenterPage, "查看"),
            FindPetButton(window.PetCenterPage, "应用"),
            window.PetCenterPage.ProductVersionText,
            window.PetCenterPage.ProtocolText,
            window.PetCenterPage.AuthorLicenseText,
            window.PetCenterPage.InstallLocationText,
            window.PetCenterPage.RefreshButton,
            window.PetCenterPage.RestoreBackupButton,
            window.PetCenterPage.UninstallButton,
        };
        Ensure(window.InfoScroll.ScrollableWidth <= 0.5 &&
               window.InfoScroll.ScrollableHeight <= 0.5 &&
               window.InfoScroll.ComputedVerticalScrollBarVisibility == Visibility.Collapsed &&
               previewButtons.Length == 11 &&
               previewButtons.All(button => IsInsidePetViewport(button, window.InfoScroll)) &&
               requiredElements.All(element => IsInsidePetViewport(element, window.InfoScroll)),
            $"The real {size.Width:0}x{size.Height:0} WPF pet console must fit every preview and operation without main-content scrolling " +
            $"(view={window.PetCenterPage.ActualWidth:0.0}x{window.PetCenterPage.ActualHeight:0.0}, " +
            $"viewport={window.InfoScroll.ViewportWidth:0.0}x{window.InfoScroll.ViewportHeight:0.0}, " +
            $"scroll={window.InfoScroll.ScrollableWidth:0.0}x{window.InfoScroll.ScrollableHeight:0.0}, " +
            $"workspace={window.PetCenterPage.WorkspaceSurface.ActualWidth:0.0}x{window.PetCenterPage.WorkspaceSurface.ActualHeight:0.0}).");
        SaveWindowContent(window, snapshotPath);
    }

    private sealed record PetCenterSnapshotProfile(
        string Path,
        bool DarkMode,
        Size Size,
        string PreviewKey,
        string Label);
}
