using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Pets;
using Tessalume.Core.Pets;

internal static partial class TestSuite
{
    private static readonly int[] CodexIdleDurations = [1680, 660, 660, 840, 840, 1920];
    private static readonly string[] RuntimeShowcaseKeys =
    [
        "idle", "move-right", "move-left", "wave-touch", "jump",
        "blocked", "needs-input", "running", "ready",
    ];

    static Task PetPreviewClockMatchesCurrentCodexDesktopAsync()
    {
        Ensure(PetCodexMotionSchedule.RuntimeContractId ==
               "codex-desktop-v2-2026-08-19",
            "The runtime clock must expose the audited Codex compatibility revision.");
        Ensure(PetCodexMotionSchedule.TryCreate("idle", out var idle) &&
               idle.MatchesCodexState &&
               idle.LoopStartIndex == 0 &&
               idle.ActionCycleCount == 0 &&
               idle.Frames.Select(frame => frame.Row).All(row => row == 0) &&
               idle.Frames.Select(frame => frame.Column).SequenceEqual(
                   Enumerable.Range(0, 6)) &&
               idle.Frames.Select(frame => frame.DurationMilliseconds).SequenceEqual(
                   CodexIdleDurations) &&
               idle.Frames.Sum(frame => frame.DurationMilliseconds) == 6600,
            "Idle must use Codex's six slow v2 cells and exact 6.6 second loop.");

        var actions = new[]
        {
            (Key: "move-right", Row: 1, Count: 8, Step: 120, Last: 220),
            (Key: "move-left", Row: 2, Count: 8, Step: 120, Last: 220),
            (Key: "wave-touch", Row: 3, Count: 4, Step: 140, Last: 280),
            (Key: "jump", Row: 4, Count: 5, Step: 140, Last: 280),
            (Key: "blocked", Row: 5, Count: 8, Step: 140, Last: 240),
            (Key: "needs-input", Row: 6, Count: 6, Step: 150, Last: 260),
            (Key: "running", Row: 7, Count: 6, Step: 120, Last: 220),
            (Key: "ready", Row: 8, Count: 6, Step: 150, Last: 280),
        };
        foreach (var expected in actions)
        {
            Ensure(PetCodexMotionSchedule.TryCreate(expected.Key, out var sequence) &&
                   sequence.MatchesCodexState &&
                   sequence.ReturnsToIdle &&
                   sequence.ActionCycleCount == 3 &&
                   sequence.LoopStartIndex == expected.Count * 3 &&
                   sequence.Frames.Count == expected.Count * 3 + idle.Frames.Count,
                $"{expected.Key} must play exactly three Codex action cycles before idle.");
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var action = sequence.Frames
                    .Skip(cycle * expected.Count)
                    .Take(expected.Count)
                    .ToArray();
                Ensure(action.Select(frame => frame.Row).All(row => row == expected.Row) &&
                       action.Select(frame => frame.Column).SequenceEqual(
                           Enumerable.Range(0, expected.Count)) &&
                       action.Take(expected.Count - 1).All(frame =>
                           frame.DurationMilliseconds == expected.Step) &&
                       action[^1].DurationMilliseconds == expected.Last,
                    $"{expected.Key} cycle {cycle + 1} must keep Codex's row, order, and delays.");
            }
            Ensure(sequence.Frames.Skip(sequence.LoopStartIndex).SequenceEqual(idle.Frames),
                $"{expected.Key} must enter the exact slow idle tail after its third cycle.");
        }

        Ensure(PetCodexMotionSchedule.TryCreate("gaze-clockwise", out var gaze) &&
               !gaze.MatchesCodexState &&
               gaze.Frames.Count == 16 &&
               gaze.Frames.Take(8).Select(frame => frame.Row).All(row => row == 9) &&
               gaze.Frames.Skip(8).Select(frame => frame.Row).All(row => row == 10),
            "The 16-direction diagnostic must use the real two atlas rows without pretending to be a Codex status.");
        Ensure(PetCodexMotionSchedule.TryCreate("showcase", out var showcase) &&
               showcase.IsShowcase &&
               !showcase.MatchesCodexState &&
               showcase.Tracks.Count == 9 &&
               showcase.Tracks.Select(track => track.Key).SequenceEqual(RuntimeShowcaseKeys) &&
               showcase.Tracks.All(track => track.LoopStartIndex == 0) &&
               showcase.Tracks[0].Frames.SequenceEqual(idle.Frames) &&
               showcase.Tracks.Skip(1).All(track =>
                   track.Frames.Count > 1 &&
                   track.Frames.All(frame => frame.Row > 0)),
            "The nine-action showcase must render nine independent real-atlas tracks, including the live idle blink cell.");
        return Task.CompletedTask;
    }

    static async Task PetCenterRouteAndAccessibilityContractIsCompleteAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var petRoot = Path.Combine(appRoot, "Features", "Pets");
        var mainXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
        var routeSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "Navigation",
            "AppRoute.cs"));
        var navigationSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Shell",
            "Navigation",
            "MainWindow.NavigationRouter.cs"));
        var petShellSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Shell",
            "Pets",
            "MainWindow.Pets.cs"));
        var presentationSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "MainWindow.Presentation.cs"));
        var petXaml = await File.ReadAllTextAsync(Path.Combine(petRoot, "PetCenterView.xaml"));
        var galleryXaml = await File.ReadAllTextAsync(Path.Combine(petRoot, "PetGalleryView.xaml"));
        var galleryViewSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetGalleryView.xaml.cs"));
        var galleryServiceSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetGalleryService.cs"));
        var projectWatcherSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetProjectWatcher.cs"));
        var petViewSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetCenterView.xaml.cs"));
        var petServiceSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetApplicationService.cs"));
        var previewSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetPreviewPlayer.cs"));
        var runtimeRendererSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetCodexRuntimeRenderer.cs"));
        var motionScheduleSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetCodexMotionSchedule.cs"));
        var decoderSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetGifFrameDecoder.cs"));
        var motionSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetMotionPreference.cs"));
        var developmentLoaderSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.Core",
            "Pets",
            "PetDevelopmentProjectLoader.cs"));
        var mainWindowSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "MainWindow.xaml.cs"));
        var snapshotSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "tests",
            "Tessalume.Tests",
            "PetSnapshotCommands.cs"));
        var themeDetailXaml = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Controls",
            "ThemeDetailPanel.xaml"));
        var themeDetailSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Controls",
            "ThemeDetailPanel.xaml.cs"));
        var themeExperienceSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "MainWindow.ThemeLibraryExperience.cs"));
        var themeRuntimeSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "MainWindow.ThemeRuntime.cs"));
        var manifest = await File.ReadAllTextAsync(Path.Combine(appRoot, "app.manifest"));
        var publishedPackageResult = await new PetPackageLoader().LoadAsync(Path.Combine(
            repositoryRoot,
            "pets",
            PetApplicationService.BuiltInPetId));
        var publishedPackage = publishedPackageResult.Package;
        Ensure(publishedPackageResult.Validation.IsValid && publishedPackage is not null,
            "The pet UI contract requires the validated published Flying Snowfluff package.");

        Ensure(mainXaml.Contains("x:Name=\"PetsButton\"", StringComparison.Ordinal) &&
               mainXaml.Contains("Click=\"Pets_Click\"", StringComparison.Ordinal) &&
               mainXaml.Contains("Text=\"宠物画廊\"", StringComparison.Ordinal) &&
               mainXaml.Contains("x:Name=\"PetCenterPage\"", StringComparison.Ordinal) &&
               mainXaml.Contains("<pets:PetCenterView", StringComparison.Ordinal),
            "The shell must expose Codex Pets as a dedicated personalization destination.");
        Ensure(routeSource.Contains("Pets,", StringComparison.Ordinal) &&
               petShellSource.Contains(
                   "NavigateTo(Features.Navigation.AppRoute.Pets)",
                   StringComparison.Ordinal) &&
               navigationSource.Contains(
                   "AppRoute.Pets => double.PositiveInfinity",
                   StringComparison.Ordinal) &&
               navigationSource.Contains(
                   "PetCenterPage.Visibility = route == AppRoute.Pets",
                   StringComparison.Ordinal) &&
               navigationSource.Contains(
                   "PetCenterPage.SetPageActive(route == AppRoute.Pets)",
                   StringComparison.Ordinal) &&
               presentationSource.Contains(
                   "UpdateInfoNavigationButton(PetsButton, _currentRoute == Features.Navigation.AppRoute.Pets)",
                   StringComparison.Ordinal),
            "The pet destination must participate in the route model, active navigation, and page lifecycle.");

        foreach (var marker in new[]
                 {
                     "AutomationProperties.Name=\"宠物动态动作预览\"",
                     "AutomationProperties.Name=\"返回宠物画廊\"",
                     "AutomationProperties.Name=\"宠物主操作\"",
                     "AutomationProperties.Name=\"确认已在 Codex 完成宠物选择\"",
                     "AutomationProperties.Name=\"卸载 Tessalume 管理的宠物文件\"",
                     "AutomationProperties.Name=\"恢复最近的宠物备份\"",
                     "AutomationProperties.Name=\"重新检查宠物状态\"",
                 })
        {
            Ensure(petXaml.Contains(marker, StringComparison.Ordinal),
                $"The pet center is missing accessibility metadata {marker}.");
        }

        foreach (var guidance in new[]
                 {
                     "Settings → Pets → Refresh → 选择宠物 → 输入 /pet",
                     "仅管理当前用户 .codex\\pets",
                     "配套主题",
                     "爱弥斯 · 星海远航",
                 })
        {
            Ensure(petXaml.Contains(guidance, StringComparison.Ordinal),
                $"The newcomer guidance or privacy boundary is missing: {guidance}");
        }

        Ensure(!petXaml.Contains("复制 /pet", StringComparison.Ordinal) &&
               !petViewSource.Contains("CopyCommand", StringComparison.Ordinal) &&
               !petShellSource.Contains("CopyCommand", StringComparison.Ordinal) &&
               !petShellSource.Contains("无法复制命令", StringComparison.Ordinal) &&
               !mainWindowSource.Contains("IPetCommandClipboard", StringComparison.Ordinal) &&
               !petViewSource.Contains("Process.Start", StringComparison.Ordinal),
            "The pet center must not expose a copy command or a pet-specific clipboard bridge.");
        Ensure(petXaml.Contains("x:Key=\"PetToolButton\"", StringComparison.Ordinal) &&
               petXaml.Contains("x:Key=\"PetSelectionButton\"", StringComparison.Ordinal) &&
               petXaml.Contains("x:Key=\"PetAccentToolButton\"", StringComparison.Ordinal) &&
               petXaml.Contains("x:Key=\"PetDangerToolButton\"", StringComparison.Ordinal) &&
               petXaml.Contains("x:Key=\"PetPrimaryButton\"", StringComparison.Ordinal) &&
               petXaml.Contains("<Setter Property=\"Width\" Value=\"148\"", StringComparison.Ordinal) &&
               petXaml.Contains("<Setter Property=\"Height\" Value=\"39\"", StringComparison.Ordinal) &&
               petXaml.Contains("<Setter TargetName=\"ToolSurface\" Property=\"Opacity\" Value=\"0.48\"", StringComparison.Ordinal) &&
               petXaml.Contains("<local:PetGalleryView x:Name=\"GalleryPanel\"", StringComparison.Ordinal) &&
               petXaml.Contains("x:Name=\"DetailPanel\" Visibility=\"Collapsed\"", StringComparison.Ordinal) &&
               petXaml.Contains("MaxHeight=\"720\"", StringComparison.Ordinal) &&
               petXaml.Contains("Text=\"返回画廊\"", StringComparison.Ordinal) &&
               petXaml.Contains("Style=\"{DynamicResource HomeCrispButton}\"", StringComparison.Ordinal) &&
               petXaml.Contains("Text=\"动作预览\"", StringComparison.Ordinal) &&
               petXaml.Contains("x:Name=\"PetHeaderIcon\"", StringComparison.Ordinal) &&
               petXaml.Contains("x:Name=\"HeaderStatusBadge\"", StringComparison.Ordinal) &&
               petXaml.Contains("x:Name=\"PetConsoleShell\"", StringComparison.Ordinal) &&
               petXaml.Contains("Background=\"Transparent\"", StringComparison.Ordinal) &&
               petXaml.Contains("MaxHeight=\"650\"", StringComparison.Ordinal) &&
               petXaml.Contains("Effect=\"{DynamicResource HomeCardShadow}\"", StringComparison.Ordinal) &&
               petXaml.Contains("Background=\"{DynamicResource PersonalizationMotionGradient}\"", StringComparison.Ordinal) &&
               !petXaml.Contains("只解码当前选择", StringComparison.Ordinal) &&
               petViewSource.Contains("FormatLicenseSummary", StringComparison.Ordinal) &&
               petViewSource.Contains("FormatLocation", StringComparison.Ordinal) &&
               petViewSource.Contains("保留所有权利", StringComparison.Ordinal),
            "The pet console must reuse Tessalume surfaces, present a labelled back action, and avoid a disconnected full-page ornament shell.");
        Ensure(galleryXaml.Contains("Text=\"Codex 宠物画廊\"", StringComparison.Ordinal) &&
               galleryXaml.Contains("x:Name=\"SearchBox\"", StringComparison.Ordinal) &&
               !galleryXaml.Contains("开发预览", StringComparison.Ordinal) &&
               !galleryXaml.Contains("DevelopmentFilterButton", StringComparison.Ordinal) &&
               galleryXaml.Contains("资源更新后刷新画廊即可重新载入预览", StringComparison.Ordinal) &&
               galleryXaml.Contains("只有通过完整哈希校验的资源才会开放安装", StringComparison.Ordinal) &&
               galleryXaml.Contains("<UniformGrid Columns=\"2\"", StringComparison.Ordinal) &&
               galleryXaml.Contains("Background=\"{DynamicResource HomeHeroGradient}\"", StringComparison.Ordinal) &&
               galleryXaml.Contains("Style=\"{DynamicResource HomeCrispButton}\"", StringComparison.Ordinal) &&
               galleryViewSource.Contains("Entry.CanOpen ? \"查看并安装\" : \"资源不可用\"", StringComparison.Ordinal) &&
               !galleryViewSource.Contains("IsDevelopment", StringComparison.Ordinal) &&
               galleryServiceSource.Contains("UsesLastGoodPreview = true", StringComparison.Ordinal) &&
               galleryServiceSource.Contains("PetLibraryWatcher", StringComparison.Ordinal) &&
               projectWatcherSource.Contains("DebounceDelay", StringComparison.Ordinal) &&
               projectWatcherSource.Contains("StopCore()", StringComparison.Ordinal),
            "The pet gallery must present one searchable package library with a bounded live-preview lifecycle.");
        Ensure(galleryServiceSource.Contains("_options.PackagesRoot", StringComparison.Ordinal) &&
               !galleryServiceSource.Contains("PetDevelopmentProjectLoader", StringComparison.Ordinal) &&
               petShellSource.Contains("ReloadSelectedPetEntryAsync", StringComparison.Ordinal) &&
               petShellSource.Contains("await RefreshPetGalleryAsync(showGallery: false)", StringComparison.Ordinal) &&
               !petShellSource.Contains("RefreshDevelopmentPreview", StringComparison.Ordinal) &&
               !petServiceSource.Contains("EnsurePetsInstalled(_layout)", StringComparison.Ordinal) &&
               petServiceSource.Contains("LoadSelectedPackageAsync", StringComparison.Ordinal),
            "Published pet resources must refresh from dist/pets without a rebuild or a separate installer boundary.");
        Ensure(petViewSource.Contains("width < 720", StringComparison.Ordinal) &&
               petViewSource.Contains("width < 1100", StringComparison.Ordinal) &&
               petViewSource.Contains("new GridLength(2, GridUnitType.Star)", StringComparison.Ordinal) &&
               petViewSource.Contains("new GridLength(3, GridUnitType.Star)", StringComparison.Ordinal) &&
               petViewSource.Contains("WorkspaceSurface_SizeChanged", StringComparison.Ordinal) &&
               petViewSource.Contains("UpdatePreviewStageBounds", StringComparison.Ordinal) &&
               petViewSource.Contains("PetRuntimePreview.Width = displayWidth", StringComparison.Ordinal) &&
               petViewSource.Contains("PetRuntimePreview.Height = displayHeight", StringComparison.Ordinal) &&
               !petViewSource.Contains("Grid.SetRow(ControlPanelHost, 1)", StringComparison.Ordinal),
            "Pet layout must remain a height-bound two-column console whose real WebView preview keeps an explicit non-zero stage size.");
        Ensure(decoderSource.Contains("GifBitmapDecoder", StringComparison.Ordinal) &&
               decoderSource.Contains("MaximumRetainedDecodedBytes = 24L * 1024 * 1024", StringComparison.Ordinal) &&
               decoderSource.Contains("CalculateTargetSize", StringComparison.Ordinal) &&
               decoderSource.Contains("ReadFrameDuration", StringComparison.Ordinal) &&
               previewSource.Contains("CancelLoadAndReleaseFrames", StringComparison.Ordinal) &&
               previewSource.Contains("_timer.Interval = _frameDurations[_frameIndex]", StringComparison.Ordinal) &&
               previewSource.Contains("_timer.Stop()", StringComparison.Ordinal) &&
               motionSource.Contains("SystemParameters.ClientAreaAnimation", StringComparison.Ordinal) &&
               petViewSource.Contains("SetPageActive", StringComparison.Ordinal) &&
               petViewSource.Contains("PetCenterView_IsVisibleChanged", StringComparison.Ordinal) &&
               petViewSource.Contains("PetCenterView_Unloaded", StringComparison.Ordinal),
            "The product preview must keep bounded decoding and stop its timer when the page is inactive.");
        Ensure(petXaml.Contains("WebView2CompositionControl", StringComparison.Ordinal) &&
               previewSource.Contains("LoadRuntimeCurrentAsync", StringComparison.Ordinal) &&
               previewSource.Contains("GIF 兼容回退", StringComparison.Ordinal) &&
               previewSource.Contains("实时图集 · 九动作独立循环", StringComparison.Ordinal) &&
               runtimeRendererSource.Contains("SetVirtualHostNameToFolderMapping", StringComparison.Ordinal) &&
               runtimeRendererSource.Contains("backgroundPosition", StringComparison.Ordinal) &&
               runtimeRendererSource.Contains("setTimeout", StringComparison.Ordinal) &&
               runtimeRendererSource.Contains("stage.className", StringComparison.Ordinal) &&
               runtimeRendererSource.Contains("state.tracks.map", StringComparison.Ordinal) &&
               motionScheduleSource.Contains("ActionCycleCount = 3", StringComparison.Ordinal) &&
               motionScheduleSource.Contains("IdleSlowdown = 6", StringComparison.Ordinal) &&
               motionScheduleSource.Contains("CreateShowcase", StringComparison.Ordinal) &&
               galleryServiceSource.Contains("runtimeSpritesheetPath", StringComparison.Ordinal) &&
               !petXaml.Contains("Codex 同步", StringComparison.Ordinal) &&
               !petViewSource.Contains("Codex 同步", StringComparison.Ordinal) &&
               !previewSource.Contains("Codex 同步", StringComparison.Ordinal),
            "All pet previews must use the self-contained real-atlas runtime, including the live nine-panel scene, with GIF only as a compatibility fallback.");
        foreach (var previewMarker in new[]
                 {
                     "Content=\"待机\" Tag=\"idle\"",
                     "Content=\"向右移动\" Tag=\"move-right\"",
                     "Content=\"向左移动\" Tag=\"move-left\"",
                     "Content=\"挥手互动\" Tag=\"wave-touch\"",
                     "Content=\"跳跃\" Tag=\"jump\"",
                     "Content=\"遇到阻塞\" Tag=\"blocked\"",
                     "Content=\"等待输入\" Tag=\"needs-input\"",
                     "Content=\"正在工作\" Tag=\"running\"",
                     "Content=\"完成待看\" Tag=\"ready\"",
                     "Content=\"16 向转身\" Tag=\"gaze-clockwise\"",
                     "Content=\"动态九宫格\" Tag=\"showcase\"",
                 })
        {
            Ensure(petXaml.Contains(previewMarker, StringComparison.Ordinal),
                $"The pet stage is missing animated selector {previewMarker}.");
        }
        Ensure(petServiceSource.Contains("种动作 ·", StringComparison.Ordinal) &&
               petServiceSource.Contains("向转身 ·", StringComparison.Ordinal) &&
               petServiceSource.Contains("有效格", StringComparison.Ordinal) &&
               snapshotSource.Contains(
                   "new Size(1600, 900)",
                   StringComparison.Ordinal) &&
               snapshotSource.Contains(
                   "new Size(1366, 768)",
                   StringComparison.Ordinal) &&
               snapshotSource.Contains(
                   "new Size(1266, 813)",
                   StringComparison.Ordinal) &&
               snapshotSource.Contains(
                   "new Size(900, 720)",
                   StringComparison.Ordinal) &&
               snapshotSource.Contains(
                   "ComputedVerticalScrollBarVisibility == Visibility.Collapsed",
                   StringComparison.Ordinal) &&
               snapshotSource.Contains(
                   "\"showcase\"",
                   StringComparison.Ordinal),
            "The supplied nine-panel showcase and truthful 9-action/16-direction protocol summary must remain visible.");
        Ensure(publishedPackage!.Catalog.Protocol.States.Count - 2 == 9 &&
               publishedPackage.Catalog.Protocol.States.TakeLast(2).Sum(state => state.Frames) == 16 &&
               publishedPackage.Catalog.Protocol.UsedFrameCount == 74 &&
               publishedPackage.PreviewFiles.Count() == 11 &&
               publishedPackage.PreviewFiles.Any(preview =>
                   preview.Metadata.ActionKey == "showcase" &&
                   preview.Metadata.Kind == "showcase" &&
                   preview.Metadata.Label == "动态九宫格" &&
                   preview.GifInfo.FrameCount == 8 &&
                   File.Exists(preview.FullPath)),
            "The published package must supply 9 actions, 16 directional turns, 74 used cells, and its verified showcase asset.");
        Ensure(petServiceSource.Contains("RunInBackgroundAsync", StringComparison.Ordinal) &&
               petServiceSource.Contains("Task.Run(", StringComparison.Ordinal) &&
               petServiceSource.Contains("WaitForIdleAsync", StringComparison.Ordinal) &&
               petServiceSource.Contains("_activeOperations", StringComparison.Ordinal) &&
               petServiceSource.Contains("finally", StringComparison.Ordinal) &&
               petServiceSource.Contains("CompleteOperation();", StringComparison.Ordinal) &&
               mainWindowSource.Contains(
                   "await _petApplicationService.WaitForIdleAsync()",
                   StringComparison.Ordinal) &&
               AppearsInOrder(
                   mainWindowSource,
                   "_petCancellation.Cancel();",
                   "await _petApplicationService.WaitForIdleAsync();",
                   "_petApplicationService.Dispose();",
                   "_petCancellation.Dispose();"),
            "Large pet file transactions must run off the dispatcher and drain before installer disposal.");
        foreach (var operation in new[]
                 {
                     "RefreshAsync",
                     "InstallAsync",
                     "UninstallAsync",
                     "AcknowledgeCodexSelectionAsync",
                     "RestoreLatestBackupAsync",
                     "RecoverManagementStateAsync",
                     "NeedsInformationalDisclosureAsync",
                     "MarkInformationalDisclosureShownAsync",
                     "TryClaimCompanionSuggestionAsync",
                 })
        {
            Ensure(PublicPetOperationUsesBackgroundWrapper(petServiceSource, operation),
                $"Pet application I/O operation {operation} must use the tracked background wrapper.");
        }

        var uninstallBody = SliceSource(
            petShellSource,
            "private async void PetCenterPage_UninstallRequested",
            "private async void PetCenterPage_RestoreBackupRequested");
        Ensure(uninstallBody.Contains(
                   "PetCenterStatus.UnknownModification or PetCenterStatus.Damaged",
                   StringComparison.Ordinal) &&
               uninstallBody.Contains(
                   "PetUninstallIntent.RemoveModifiedManagedFilesConfirmed",
                   StringComparison.Ordinal) &&
               AppearsInOrder(
                   uninstallBody,
                   "EnsurePetInformationDisclosureAsync()",
                   "ShowProductConfirmation(",
                   "PetService.UninstallAsync("),
            "Unknown-modified and damaged uninstall must require disclosure, confirmation, and the explicit modified-files intent.");
        var restoreBody = SliceSource(
            petShellSource,
            "private async void PetCenterPage_RestoreBackupRequested",
            "private async void PetCenterPage_SelectionAcknowledgementRequested");
        Ensure(AppearsInOrder(
                   restoreBody,
                   "EnsurePetInformationDisclosureAsync()",
                   "ShowProductConfirmation(",
                   "PetService.RestoreLatestBackupAsync"),
            "Backup restore must show the same bounded permission disclosure before confirmation and mutation.");
        var recoverStateBody = SliceSource(
            petShellSource,
            "case PetCenterAction.RecoverState:",
            "case PetCenterAction.ReplaceModified:");
        Ensure(petServiceSource.Contains(
                   "!snapshot.StateIsValid",
                   StringComparison.Ordinal) &&
               petServiceSource.Contains(
                   "PetCenterAction.RecoverState, \"归档并重建管理状态\"",
                   StringComparison.Ordinal) &&
               petServiceSource.Contains("\"管理状态损坏\"", StringComparison.Ordinal) &&
               petServiceSource.Contains(
                   "CanRestoreBackup = snapshot.StateIsValid &&",
                   StringComparison.Ordinal) &&
               recoverStateBody.Contains("先原样归档损坏文件", StringComparison.Ordinal) &&
               recoverStateBody.Contains("空的 schema 1 状态", StringComparison.Ordinal) &&
               recoverStateBody.Contains("不会修改任何 Codex 宠物文件", StringComparison.Ordinal) &&
               recoverStateBody.Contains("非受管冲突", StringComparison.Ordinal) &&
               recoverStateBody.Contains(
                   "PetService.RecoverManagementStateAsync",
                   StringComparison.Ordinal) &&
               !recoverStateBody.Contains("InstallAsync", StringComparison.Ordinal) &&
               !recoverStateBody.Contains("UninstallAsync", StringComparison.Ordinal),
            "An unreadable management schema must offer explicit archival recovery without mutating Codex pet files.");

        Ensure(themeDetailXaml.Contains("x:Name=\"CompanionPetButton\"", StringComparison.Ordinal) &&
               themeDetailSource.Contains(
                   "private const string CompanionThemeId = \"aemeath.star-voyage\"",
                   StringComparison.Ordinal) &&
               themeDetailSource.Contains("CompanionPetRequested", StringComparison.Ordinal) &&
               themeDetailSource.Contains("CompanionPetButton.Visibility", StringComparison.Ordinal) &&
               themeExperienceSource.Contains(
                   "ThemeDetailPanel_CompanionPetRequested",
                   StringComparison.Ordinal) &&
               themeExperienceSource.Contains(
                   "NavigateTo(Features.Navigation.AppRoute.Pets)",
                   StringComparison.Ordinal) &&
               themeExperienceSource.Contains(
                   "OpenRecommendedCompanionPetAsync()",
                   StringComparison.Ordinal) &&
               petShellSource.Contains(
                   "_petGallerySnapshot?.Entries.FirstOrDefault",
                   StringComparison.Ordinal),
            "The Aemeath theme detail must expose a visible, event-driven route to its companion pet.");
        var applyThemeStart = themeRuntimeSource.IndexOf(
            "private async Task<bool> ApplyThemeAsync",
            StringComparison.Ordinal);
        var applyThemeEnd = themeRuntimeSource.IndexOf(
            "private async Task<ThemeApplicationResult> ApplyPackageAsync",
            StringComparison.Ordinal);
        var applyThemeBody = applyThemeStart >= 0 && applyThemeEnd > applyThemeStart
            ? themeRuntimeSource[applyThemeStart..applyThemeEnd]
            : string.Empty;
        Ensure(applyThemeBody.Contains("ScheduleCompanionPetSuggestion", StringComparison.Ordinal) &&
               !applyThemeBody.Contains("InstallAsync", StringComparison.Ordinal) &&
               !applyThemeBody.Contains("PetInstallIntent", StringComparison.Ordinal),
            "Applying a paired theme may schedule one informational hint but must never install a pet.");
        var petThemeStart = petShellSource.IndexOf(
            "private void PetCenterPage_RecommendedThemeRequested",
            StringComparison.Ordinal);
        var petThemeEnd = petShellSource.IndexOf(
            "private void RenderPetState",
            StringComparison.Ordinal);
        var petThemeBody = petThemeStart >= 0 && petThemeEnd > petThemeStart
            ? petShellSource[petThemeStart..petThemeEnd]
            : string.Empty;
        Ensure(petThemeBody.Contains("ShowThemes", StringComparison.Ordinal) &&
               petThemeBody.Contains("ApplyThemeAsync", StringComparison.Ordinal) &&
               !petThemeBody.Contains("InstallAsync", StringComparison.Ordinal) &&
               !petThemeBody.Contains("PetInstallIntent", StringComparison.Ordinal),
            "Pet companion-theme actions must only view or apply the theme and never mutate pet installation.");
        Ensure(manifest.Contains("PerMonitorV2, PerMonitor", StringComparison.Ordinal) &&
               petXaml.Contains("TextOptions.TextRenderingMode=\"ClearType\"", StringComparison.Ordinal) &&
               petXaml.Contains("TextOptions.TextHintingMode=\"Fixed\"", StringComparison.Ordinal),
            "The pet center must inherit per-monitor DPI behavior and stable text rendering.");
    }

    static Task PetCenterViewRendersAndAdaptsWithoutExternalEffectsAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-pet-center-ui-{Guid.NewGuid():N}");
        var themesDirectory = Path.Combine(root, "themes");
        var dataDirectory = Path.Combine(root, "data");
        Directory.CreateDirectory(themesDirectory);
        Directory.CreateDirectory(dataDirectory);
        var portableLayout = new PortableLayout(root, themesDirectory, dataDirectory);
        BuiltInAssetInstaller.EnsurePetsInstalled(portableLayout);
        var corruptPreviewPath = Path.Combine(dataDirectory, "corrupt-preview.gif");
        File.WriteAllBytes(corruptPreviewPath, [0x47]);
        var petOptions = new PetApplicationServiceOptions(
            Path.Combine(root, "codex-pets"),
            Path.Combine(root, "pet-backups"),
            Path.Combine(dataDirectory, "pet-center-state.v1.json"));
        var publishedPetRoot = Path.Combine(
            FindRepositoryRoot(),
            "pets",
            PetApplicationService.BuiltInPetId);
        var unmanagedPetDirectory = Path.Combine(
            petOptions.CodexPetsRoot,
            "existing-user-snowfluff");
        Directory.CreateDirectory(unmanagedPetDirectory);
        var unmanagedManifestPath = Path.Combine(unmanagedPetDirectory, "pet.json");
        var unmanagedSpritesheetPath = Path.Combine(unmanagedPetDirectory, "spritesheet.webp");
        File.Copy(Path.Combine(publishedPetRoot, "pet.json"), unmanagedManifestPath);
        File.Copy(Path.Combine(publishedPetRoot, "spritesheet.webp"), unmanagedSpritesheetPath);
        const string invalidManagementStateContent = "{ invalid management state";
        File.WriteAllText(petOptions.StatePath, invalidManagementStateContent);
        var unmanagedManifestHash = ComputeFileSha256(unmanagedManifestPath);
        var unmanagedSpritesheetHash = ComputeFileSha256(unmanagedSpritesheetPath);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            MainWindow? window = null;
            PetCenterView? view = null;
            try
            {
                PetCenterPresentationState invalidManagementState;
                PetCenterPresentationState recoveredManagementState;
                using (var service = new PetApplicationService(
                           portableLayout,
                           petOptions))
                {
                    invalidManagementState = service.RefreshAsync()
                        .GetAwaiter()
                        .GetResult();
                    service.WaitForIdleAsync().GetAwaiter().GetResult();
                    Ensure(invalidManagementState.Status == PetCenterStatus.Damaged &&
                           invalidManagementState.StatusTitle == "管理状态损坏" &&
                           invalidManagementState.PrimaryAction == PetCenterAction.RecoverState &&
                           invalidManagementState.PrimaryActionText == "归档并重建管理状态" &&
                           !invalidManagementState.CanRestoreBackup &&
                           invalidManagementState.ProtocolSummary ==
                           "图集协议 v2 · 9 种动作 · 16 向转身 · 74 有效格" &&
                           invalidManagementState.PreviewFrames.Count == 11 &&
                           invalidManagementState.PreviewFrames.Any(frame =>
                               frame.Key == "showcase" &&
                               frame.Label == "动态九宫格" &&
                               frame.ExpectedFrameCount == 8 &&
                               frame.FilePath is not null &&
                               File.Exists(frame.FilePath)),
                        "A corrupt management schema must render recovery while preserving the actual package protocol and all product previews.");

                    VerifyPetServiceTracksActiveOperations(service);
                    recoveredManagementState = service.RecoverManagementStateAsync()
                        .GetAwaiter()
                        .GetResult();
                    service.WaitForIdleAsync().GetAwaiter().GetResult();
                }
                using var recoveredStateDocument = JsonDocument.Parse(
                    File.ReadAllText(petOptions.StatePath));
                var corruptStateArchives = Directory.GetFiles(
                    dataDirectory,
                    "pet-center-state.v1.json.corrupt-*.bak",
                    SearchOption.TopDirectoryOnly);
                Ensure(recoveredManagementState.Status == PetCenterStatus.DuplicateIdConflict &&
                       recoveredManagementState.PrimaryAction == PetCenterAction.ExplainConflict &&
                       ComputeFileSha256(unmanagedManifestPath) == unmanagedManifestHash &&
                       ComputeFileSha256(unmanagedSpritesheetPath) == unmanagedSpritesheetHash &&
                       corruptStateArchives.Length == 1 &&
                       File.ReadAllText(corruptStateArchives[0]) == invalidManagementStateContent &&
                       recoveredStateDocument.RootElement.GetProperty("schemaVersion").GetInt32() == 1,
                    "Recovering management state must archive the corrupt bytes, rebuild schema 1, leave Codex pet files unchanged, and rescan them as an unmanaged conflict.");

                window = new MainWindow(
                    portableLayout,
                    petOptions);
                InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                InvokeMainWindowMethod(window, "NavigateTo", AppRoute.Pets);
                CompleteInfoPageTransition(window);
                view = window.PetCenterPage;

                // The product shell owns installer and process actions. This test deliberately
                // removes those subscriptions and verifies only the view's intent events.
                ClearPetCenterEventHandlers(view);
                PetCenterAction? requestedAction = null;
                view.PrimaryActionRequested += (_, action) => requestedAction = action;
                view.Render(invalidManagementState);
                view.PreviewPlayer.SetActive(true);
                AwaitWithDispatcher(view.PreviewPlayer.WaitForCurrentLoadAsync());
                VerifyEveryAnimatedPetPreview(view, invalidManagementState.PreviewFrames);
                view.PrimaryActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Ensure(requestedAction == PetCenterAction.RecoverState &&
                       view.RestoreBackupButton.Visibility == Visibility.Visible &&
                       !view.RestoreBackupButton.IsEnabled &&
                       view.UninstallButton.Visibility == Visibility.Visible &&
                       !view.UninstallButton.IsEnabled,
                    "The corrupt-state UI must keep every management action visible while disabling unavailable mutations.");

                var state = invalidManagementState with
                {
                    Status = PetCenterStatus.UpdateAvailable,
                    StatusTitle = "有更新",
                    StatusDetail = "新版已完整校验；更新前会先备份当前受管文件。",
                    PrimaryAction = PetCenterAction.Update,
                    PrimaryActionText = "安全更新",
                    PrimaryActionEnabled = true,
                    CanUninstall = true,
                    CanAcknowledgeSelection = false,
                    CanRestoreBackup = true,
                    LatestBackupLabel = "测试备份 · 可恢复",
                };
                view.Render(state);

                Ensure(view.InstallationStatusTitle.Text == "有更新" &&
                       view.InstallationStatusDetail.Text.Contains("备份", StringComparison.Ordinal) &&
                       Equals(view.PrimaryActionButton.Content, "安全更新") &&
                       view.PrimaryActionButton.IsEnabled &&
                       view.UninstallButton.Visibility == Visibility.Visible &&
                       view.UninstallButton.IsEnabled &&
                       view.RefreshButton.Visibility == Visibility.Visible &&
                       view.RestoreBackupButton.Visibility == Visibility.Visible &&
                       view.RestoreBackupButton.IsEnabled &&
                       view.AcknowledgeSelectionButton.Visibility == Visibility.Collapsed &&
                       view.ActivationGuidePanel.Visibility == Visibility.Collapsed &&
                       Equals(view.RestoreBackupButton.ToolTip, "测试备份 · 可恢复") &&
                       view.ProductVersionText.Text == "1.0.0" &&
                       view.ProtocolText.Text.Contains("9 种动作", StringComparison.Ordinal) &&
                       view.InstallLocationText.Text.Contains('…') &&
                       view.InstallLocationText.ToolTip is string installLocationToolTip &&
                       installLocationToolTip.Contains(root, StringComparison.OrdinalIgnoreCase),
                    "Rendering must present one truthful status, one primary action, compact metadata, and visible management tools.");

                requestedAction = null;
                view.PrimaryActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Ensure(requestedAction == PetCenterAction.Update,
                    "The pet primary action must continue to emit its existing state-machine intent.");
                Ensure(view.PrimaryActionButton.Focusable &&
                       view.PrimaryActionButton.IsTabStop &&
                       AutomationProperties.GetName(view.PetPreviewImage) == "飞行雪绒动态动作预览",
                    "Primary and preview controls must remain keyboard and UI Automation reachable.");

                var awaitingState = state with
                {
                    Status = PetCenterStatus.AwaitingCodexSelection,
                    StatusTitle = "等待 Codex 中选择",
                    StatusDetail = "文件已安装；需要在 Codex 中刷新并选择。",
                    PrimaryAction = PetCenterAction.OpenCodex,
                    PrimaryActionText = "打开 Codex",
                    CanAcknowledgeSelection = true,
                };
                view.Render(awaitingState);
                Ensure(view.ActivationGuidePanel.Visibility == Visibility.Visible &&
                       view.AcknowledgeSelectionButton.Visibility == Visibility.Visible &&
                       CountVisibleButtonsWithContent(view, "打开 Codex") == 1 &&
                       Equals(view.PrimaryActionButton.Content, "打开 Codex"),
                    "Awaiting selection must show one Open Codex action and one concise activation guide without duplicate instructions.");

                var pageParent = LogicalTreeHelper.GetParent(view);
                Ensure(pageParent is Panel,
                    "The pet center must remain hosted by a detachable shell panel for isolated layout verification.");
                ((Panel)pageParent).Children.Remove(view);
                view.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/Tessalume;component/Styles/MainWindowResources.xaml",
                        UriKind.Absolute),
                });
                var host = new ScrollViewer
                {
                    Content = view,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    PanningMode = PanningMode.VerticalOnly,
                    Background = Brushes.Transparent,
                };

                var viewportProfiles = new (string Name, double Width, double Height)[]
                {
                    ("1600x900 content", 1310, 853),
                    ("1366x768 content", 1076, 721),
                    ("1266x813 content", 976, 766),
                    ("900x720 content", 610, 673),
                };
                foreach (var profile in viewportProfiles)
                {
                    ArrangePetCenter(host, profile.Width, profile.Height);
                    var previewButtons = view.DailyActionsPanel.Children.OfType<ToggleButton>()
                        .Concat(view.TaskActionsPanel.Children.OfType<ToggleButton>())
                        .Concat(view.ViewActionsPanel.Children.OfType<ToggleButton>())
                        .ToArray();
                    var themeViewButton = FindPetButton(view, "查看");
                    var themeApplyButton = FindPetButton(view, "应用");
                    var visibleEssentials = new FrameworkElement[]
                    {
                        view.PreviewStage,
                        view.InstallationStatusTitle,
                        view.PrimaryActionButton,
                        view.AcknowledgeSelectionButton,
                        view.ActivationGuidePanel,
                        view.ActionSelector,
                        themeViewButton,
                        themeApplyButton,
                        view.ProductVersionText,
                        view.ProtocolText,
                        view.AuthorLicenseText,
                        view.InstallLocationText,
                        view.RefreshButton,
                        view.RestoreBackupButton,
                        view.UninstallButton,
                    };
                    Ensure(Grid.GetColumn(view.PreviewStage) == 0 &&
                           Grid.GetRow(view.PreviewStage) == 0 &&
                           Grid.GetColumn(view.ControlPanelHost) == 2 &&
                           Grid.GetRow(view.ControlPanelHost) == 0 &&
                           Grid.GetRowSpan(view.ControlPanelHost) == 1 &&
                            Grid.GetRow(view.ActionSelector) == 1 &&
                            view.PreviewStage.ActualWidth >= view.WorkspaceGrid.ActualWidth * 0.32 &&
                            view.WorkspaceSurface.ActualHeight <= 650.5 &&
                           host.ScrollableWidth <= 0.5 &&
                           host.ScrollableHeight <= 0.5 &&
                           host.ComputedVerticalScrollBarVisibility == Visibility.Collapsed &&
                           view.ActualWidth <= host.ViewportWidth + 0.5 &&
                           view.ActualHeight <= host.ViewportHeight + 0.5 &&
                           previewButtons.Length == 11 &&
                           previewButtons.All(button => button.ActualWidth > 0 && button.ActualHeight >= 24) &&
                           visibleEssentials.All(element => IsInsidePetViewport(element, host)),
                        $"The {profile.Name} pet console must keep preview, 11 actions, metadata, theme, and management controls in one viewport without scrolling.");
                }
                Ensure(view.PrimaryActionButton.ActualWidth is >= 140 and <= 155 &&
                       view.PrimaryActionButton.ActualHeight is >= 38 and <= 40 &&
                       view.AcknowledgeSelectionButton.ActualWidth is >= 115 and <= 135 &&
                       view.AcknowledgeSelectionButton.ActualHeight is >= 38 and <= 40 &&
                       view.RefreshButton.ActualHeight is >= 35 and <= 38 &&
                       view.RestoreBackupButton.ActualHeight is >= 35 and <= 38 &&
                       view.UninstallButton.ActualHeight is >= 35 and <= 38,
                    "The one-screen console must use a coordinated primary/confirmation group and consistent always-visible tools.");

                var highDpiBitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(view.ActualWidth * 2),
                    (int)Math.Ceiling(Math.Min(view.ActualHeight, 720) * 2),
                    192,
                    192,
                    PixelFormats.Pbgra32);
                highDpiBitmap.Render(view);
                Ensure(highDpiBitmap.DpiX == 192 &&
                       highDpiBitmap.DpiY == 192 &&
                       highDpiBitmap.PixelWidth >= Math.Ceiling(view.ActualWidth * 2),
                    "The arranged pet center must remain renderable at 200% DPI without changing its DIP layout.");

                view.PreviewPlayer.Select("showcase");
                view.PreviewPlayer.SetActive(true);
                AwaitWithDispatcher(view.PreviewPlayer.WaitForCurrentLoadAsync());
                Ensure((view.PreviewPlayer.IsReducedMotion
                            ? !view.PreviewPlayer.IsAnimating &&
                              view.PreviewPlayer.DecodedFrameCount == 1
                            : view.PreviewPlayer.IsAnimating &&
                              view.PreviewPlayer.DecodedFrameCount == 8) &&
                       view.PreviewPlayer.EstimatedDecodedBytes <= 24L * 1024 * 1024,
                    "The dynamic nine-panel showcase must respect the system motion preference while staying bounded.");
                view.Visibility = Visibility.Collapsed;
                InvokePetVisibilityChanged(view);
                Ensure(!view.PreviewPlayer.IsAnimating &&
                       view.PreviewPlayer.DecodedFrameCount == 0 &&
                       view.PreviewPlayer.EstimatedDecodedBytes == 0 &&
                       view.PetPreviewImage.Source is null,
                    "Collapsing the pet page must stop playback and release the selected GIF frames immediately.");
                var hiddenFrameIndex = view.PreviewPlayer.CurrentFrameIndex;
                AwaitWithDispatcher(Task.Delay(260));
                Ensure(view.PreviewPlayer.CurrentFrameIndex == hiddenFrameIndex,
                    "A hidden pet page must not advance frames in the background.");
                view.Visibility = Visibility.Visible;
                InvokePetVisibilityChanged(view);
                view.PreviewPlayer.SetActive(true);
                AwaitWithDispatcher(view.PreviewPlayer.WaitForCurrentLoadAsync());
                Ensure(view.PreviewPlayer.IsReducedMotion
                        ? !view.PreviewPlayer.IsAnimating &&
                          view.PreviewPlayer.DecodedFrameCount == 1
                        : view.PreviewPlayer.IsAnimating &&
                          view.PreviewPlayer.DecodedFrameCount == 8,
                    "A later visible route activation should resume the preview according to the system motion preference.");
                view.SetPageActive(false);
                Ensure(!view.PreviewPlayer.IsAnimating &&
                       view.PreviewPlayer.DecodedFrameCount == 0,
                    "Leaving the route must stop playback and release frames even when the control remains visible.");

                VerifyReducedMotionAndCorruptPreviewFallback(
                    invalidManagementState.PreviewFrames,
                    corruptPreviewPath);

                host.Content = null;
            }
            catch (Exception exception)
            {
                failure = exception is TargetInvocationException invocation
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

        try
        {
            if (failure is not null)
            {
                throw new InvalidOperationException(
                    "The Codex Pets view render, accessibility, responsive, and preview lifecycle check failed.",
                    failure);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static void VerifyEveryAnimatedPetPreview(
        PetCenterView view,
        IReadOnlyList<PetPreviewFrame> previews)
    {
        var expected = new (string Key, string Label, int Frames)[]
        {
            ("idle", "待机", 6),
            ("move-right", "向右移动", 8),
            ("move-left", "向左移动", 8),
            ("wave-touch", "挥手互动", 4),
            ("jump", "跳跃", 5),
            ("blocked", "遇到阻塞", 8),
            ("needs-input", "等待输入", 6),
            ("running", "正在工作", 6),
            ("ready", "完成待看", 6),
            ("gaze-clockwise", "16 向转身", 16),
            ("showcase", "动态九宫格", 8),
        };
        Ensure(previews.Count == expected.Length,
            "The pet center must expose all 11 animated preview entries.");

        foreach (var (key, label, frameCount) in expected)
        {
            var toggle = FindPetPreviewToggle(view, label);
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            AwaitWithDispatcher(view.PreviewPlayer.WaitForCurrentLoadAsync());
            var reducedMotion = view.PreviewPlayer.IsReducedMotion;
            Ensure(view.PreviewPlayer.CurrentKey == key &&
                   view.PreviewStateText.Text == label &&
                   view.PreviewPlayer.DecodedFrameCount == (reducedMotion ? 1 : frameCount) &&
                   view.PreviewPlayer.EstimatedDecodedBytes is > 0 and <= 24L * 1024 * 1024 &&
                   view.PreviewPlayer.IsAnimating == !reducedMotion &&
                   view.PetPreviewImage.Source is BitmapSource
                   {
                       PixelWidth: > 0 and <= 720,
                       PixelHeight: > 0 and <= 720,
                   } &&
                   (!reducedMotion ||
                    view.PreviewPlayer.PlaybackDescription.Contains("减少动态", StringComparison.Ordinal)),
                $"Animated preview '{label}' must follow the system motion preference within the runtime bounds " +
                $"(key={view.PreviewPlayer.CurrentKey}, frames={view.PreviewPlayer.DecodedFrameCount}, " +
                $"bytes={view.PreviewPlayer.EstimatedDecodedBytes}, animating={view.PreviewPlayer.IsAnimating}, " +
                $"source={view.PetPreviewImage.Source?.GetType().Name ?? "null"}, " +
                $"playback={view.PreviewPlayer.PlaybackDescription}).");
            var sourceBefore = view.PetPreviewImage.Source;
            var frameBefore = view.PreviewPlayer.CurrentFrameIndex;
            if (reducedMotion)
            {
                AwaitWithDispatcher(Task.Delay(180));
                Ensure(ReferenceEquals(sourceBefore, view.PetPreviewImage.Source) &&
                       view.PreviewPlayer.CurrentFrameIndex == frameBefore,
                    $"Reduced-motion preview '{label}' must remain on its representative frame.");
            }
            else
            {
                WaitForPetFrameAdvance(
                    view.PreviewPlayer,
                    view.PetPreviewImage,
                    sourceBefore,
                    frameBefore);
                Ensure(!ReferenceEquals(sourceBefore, view.PetPreviewImage.Source) ||
                       view.PreviewPlayer.CurrentFrameIndex != frameBefore,
                    $"Animated preview '{label}' must visibly advance to a different decoded frame.");
            }
        }

        VerifyFullMotionAnimatedPreviews(previews, expected);
    }

    private static void VerifyFullMotionAnimatedPreviews(
        IReadOnlyList<PetPreviewFrame> previews,
        IReadOnlyList<(string Key, string Label, int Frames)> expected)
    {
        var image = new Image();
        var label = new TextBlock();
        using var player = new PetPreviewPlayer(
            image,
            label,
            new FixedPetMotionPreference(isReducedMotion: false));
        player.Configure(previews);
        player.SetDisplayBounds(560, 500);
        player.SetActive(true);

        foreach (var (key, expectedLabel, frameCount) in expected)
        {
            player.Select(key);
            AwaitWithDispatcher(player.WaitForCurrentLoadAsync());
            Ensure(player.CurrentKey == key &&
                   label.Text == expectedLabel &&
                   !player.IsReducedMotion &&
                   player.DecodedFrameCount == frameCount &&
                   player.EstimatedDecodedBytes is > 0 and <= 24L * 1024 * 1024 &&
                   player.IsAnimating &&
                   image.Source is BitmapSource
                   {
                       PixelWidth: > 0 and <= 720,
                       PixelHeight: > 0 and <= 720,
                   },
                $"Full-motion preview '{expectedLabel}' must decode and animate all {frameCount} catalog frames.");
            var sourceBefore = image.Source;
            var frameBefore = player.CurrentFrameIndex;
            WaitForPetFrameAdvance(player, image, sourceBefore, frameBefore);
        }
    }

    private static void WaitForPetFrameAdvance(
        PetPreviewPlayer player,
        Image image,
        ImageSource? sourceBefore,
        int frameBefore)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            AwaitWithDispatcher(Task.Delay(70));
            if (!ReferenceEquals(sourceBefore, image.Source) ||
                player.CurrentFrameIndex != frameBefore)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Preview '{player.CurrentKey}' did not advance within the bounded wait.");
    }

    private static void VerifyReducedMotionAndCorruptPreviewFallback(
        IReadOnlyList<PetPreviewFrame> previews,
        string corruptPreviewPath)
    {
        var showcase = previews.Single(preview => preview.Key == "showcase");
        var reducedImage = new Image();
        var reducedLabel = new TextBlock();
        using (var reducedPlayer = new PetPreviewPlayer(
                   reducedImage,
                   reducedLabel,
                   new FixedPetMotionPreference(isReducedMotion: true)))
        {
            reducedPlayer.Configure([showcase]);
            reducedPlayer.SetDisplayBounds(420, 455);
            reducedPlayer.SetActive(true);
            AwaitWithDispatcher(reducedPlayer.WaitForCurrentLoadAsync());
            var reducedSource = reducedImage.Source;
            Ensure(reducedPlayer.IsReducedMotion &&
                   !reducedPlayer.IsAnimating &&
                   reducedPlayer.DecodedFrameCount == 1 &&
                   reducedSource is BitmapSource &&
                   reducedPlayer.PlaybackDescription.Contains("减少动态", StringComparison.Ordinal),
                "Reduced-motion mode must decode one representative frame and keep its timer stopped.");
            AwaitWithDispatcher(Task.Delay(260));
            Ensure(ReferenceEquals(reducedSource, reducedImage.Source) &&
                   reducedPlayer.CurrentFrameIndex == 0,
                "Reduced-motion mode must never advance in the background.");
        }

        var corruptImage = new Image();
        var corruptLabel = new TextBlock();
        using var corruptPlayer = new PetPreviewPlayer(
            corruptImage,
            corruptLabel,
            new FixedPetMotionPreference(isReducedMotion: false));
        corruptPlayer.Configure(
        [
            new PetPreviewFrame(
                "corrupt",
                "损坏预览",
                corruptPreviewPath,
                ExpectedFrameCount: 2,
                SourceWidth: 2,
                SourceHeight: 2),
        ]);
        corruptPlayer.SetActive(true);
        AwaitWithDispatcher(corruptPlayer.WaitForCurrentLoadAsync());
        Ensure(corruptImage.Source is null &&
               !corruptPlayer.IsAnimating &&
               corruptPlayer.DecodedFrameCount == 0 &&
               corruptPlayer.EstimatedDecodedBytes == 0 &&
               corruptPlayer.PlaybackDescription.Contains("暂不可用", StringComparison.Ordinal),
            "A damaged GIF must fail closed without blocking the UI or retaining decoded memory.");
    }

    private static void ArrangePetCenter(ScrollViewer host, double width, double height)
    {
        host.Width = width;
        host.Height = height;
        if (host.Content is FrameworkElement content)
        {
            content.SetCurrentValue(FrameworkElement.HeightProperty, height);
        }
        var size = new Size(width, height);
        for (var pass = 0; pass < 3; pass++)
        {
            host.Measure(size);
            host.Arrange(new Rect(size));
            host.UpdateLayout();
        }
    }

    private static bool IsInsidePetViewport(FrameworkElement element, ScrollViewer host)
    {
        if (element.Visibility != Visibility.Visible ||
            element.ActualWidth <= 0 ||
            element.ActualHeight <= 0)
        {
            return false;
        }

        var origin = element.TransformToAncestor(host).Transform(new Point());
        return origin.X >= -0.5 &&
               origin.Y >= -0.5 &&
               origin.X + element.ActualWidth <= host.ViewportWidth + 0.5 &&
               origin.Y + element.ActualHeight <= host.ViewportHeight + 0.5;
    }

    private static Button FindPetButton(DependencyObject root, string content)
    {
        foreach (var childValue in LogicalTreeHelper.GetChildren(root))
        {
            if (childValue is Button { Content: string text } button &&
                string.Equals(text, content, StringComparison.Ordinal))
            {
                return button;
            }

            if (childValue is not DependencyObject child)
            {
                continue;
            }

            try
            {
                return FindPetButton(child, content);
            }
            catch (InvalidOperationException)
            {
                // Continue searching siblings until the requested product action is found.
            }
        }

        throw new InvalidOperationException($"Unable to find pet action '{content}'.");
    }

    private static ToggleButton FindPetPreviewToggle(DependencyObject root, string content)
    {
        foreach (var childValue in LogicalTreeHelper.GetChildren(root))
        {
            if (childValue is ToggleButton { Content: string text } toggle &&
                string.Equals(text, content, StringComparison.Ordinal))
            {
                return toggle;
            }

            if (childValue is not DependencyObject child)
            {
                continue;
            }

            try
            {
                return FindPetPreviewToggle(child, content);
            }
            catch (InvalidOperationException)
            {
                // Continue searching siblings until the requested animated action is found.
            }
        }

        throw new InvalidOperationException($"Unable to find pet preview '{content}'.");
    }

    private static int CountVisibleButtonsWithContent(DependencyObject root, string content)
    {
        var count = root is Button
        {
            Content: string text,
            Visibility: Visibility.Visible,
        } && string.Equals(text, content, StringComparison.Ordinal)
            ? 1
            : 0;
        foreach (var childValue in LogicalTreeHelper.GetChildren(root))
        {
            if (childValue is DependencyObject child)
            {
                count += CountVisibleButtonsWithContent(child, content);
            }
        }
        return count;
    }

    private static void ClearPetCenterEventHandlers(PetCenterView view)
    {
        foreach (var name in new[]
                 {
                     "RefreshRequested",
                     "PrimaryActionRequested",
                     "OpenCodexRequested",
                     "RecommendedThemeRequested",
                     "ApplyRecommendedThemeRequested",
                     "UninstallRequested",
                     "RestoreBackupRequested",
                     "SelectionAcknowledgementRequested",
                 })
        {
            typeof(PetCenterView).GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(view, null);
        }
    }

    private static void InvokePetVisibilityChanged(PetCenterView view)
    {
        var method = typeof(PetCenterView).GetMethod(
            "PetCenterView_IsVisibleChanged",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(
                nameof(PetCenterView),
                "PetCenterView_IsVisibleChanged");
        method.Invoke(view, [view, new DependencyPropertyChangedEventArgs()]);
    }

    private static string SliceSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = start < 0
            ? -1
            : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        return start >= 0 && end > start ? source[start..end] : string.Empty;
    }

    private static bool AppearsInOrder(string source, params string[] markers)
    {
        var position = -1;
        foreach (var marker in markers)
        {
            position = source.IndexOf(marker, position + 1, StringComparison.Ordinal);
            if (position < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PublicPetOperationUsesBackgroundWrapper(
        string source,
        string methodName)
    {
        var start = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var end = source.IndexOf("\n    public ", start + methodName.Length, StringComparison.Ordinal);
        var body = end > start ? source[start..end] : source[start..];
        return body.Contains("RunInBackgroundAsync(", StringComparison.Ordinal);
    }

    private static string ComputeFileSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void VerifyPetServiceTracksActiveOperations(PetApplicationService service)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var begin = typeof(PetApplicationService).GetMethod("BeginOperation", flags)
            ?? throw new MissingMethodException(nameof(PetApplicationService), "BeginOperation");
        var complete = typeof(PetApplicationService).GetMethod("CompleteOperation", flags)
            ?? throw new MissingMethodException(nameof(PetApplicationService), "CompleteOperation");
        begin.Invoke(service, null);
        var idle = service.WaitForIdleAsync();
        try
        {
            Ensure(!idle.IsCompleted,
                "WaitForIdleAsync must remain pending while a tracked pet operation is active.");
        }
        finally
        {
            complete.Invoke(service, null);
        }
        idle.GetAwaiter().GetResult();
        Ensure(idle.IsCompletedSuccessfully,
            "Completing the final tracked operation must release lifecycle shutdown waiters.");
    }

    private static void AwaitWithDispatcher(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(
                _ => dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false)),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }

        task.GetAwaiter().GetResult();
    }

    private sealed class FixedPetMotionPreference(bool isReducedMotion) : IPetMotionPreference
    {
        public bool IsReducedMotion { get; } = isReducedMotion;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
