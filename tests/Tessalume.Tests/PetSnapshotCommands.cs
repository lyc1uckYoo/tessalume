using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Pets;
using Tessalume.Core.Pets;

internal static partial class TestSuite
{
    static Task<int> RenderPetCenterSnapshotsAsync(
        string lightSnapshotPath,
        string darkSnapshotPath,
        string? compactSnapshotPath = null)
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
                        petOptions,
                        new RecordingPetClipboard());
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

                    await RenderPetCenterSnapshotProfileAsync(
                        window,
                        state,
                        lightSnapshotPath,
                        darkMode: false,
                        new Size(1600, 900),
                        "idle");
                    await RenderPetCenterSnapshotProfileAsync(
                        window,
                        state,
                        darkSnapshotPath,
                        darkMode: true,
                        new Size(1600, 900),
                        "showcase");
                    if (!string.IsNullOrWhiteSpace(compactSnapshotPath))
                    {
                        await RenderPetCenterSnapshotProfileAsync(
                            window,
                            state,
                            compactSnapshotPath,
                            darkMode: false,
                            new Size(900, 720),
                            "idle");
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

            Console.WriteLine($"Pet Center light snapshot: {Path.GetFullPath(lightSnapshotPath)}");
            Console.WriteLine($"Pet Center dark snapshot: {Path.GetFullPath(darkSnapshotPath)}");
            if (!string.IsNullOrWhiteSpace(compactSnapshotPath))
            {
                Console.WriteLine(
                    $"Pet Center compact snapshot: {Path.GetFullPath(compactSnapshotPath)}");
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
        SaveWindowContent(window, snapshotPath);
    }
}
