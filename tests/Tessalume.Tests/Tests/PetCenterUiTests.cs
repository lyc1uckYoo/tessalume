using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Pets;
using Tessalume.Core.Pets;

internal static partial class TestSuite
{
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
        var petViewSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetCenterView.xaml.cs"));
        var petServiceSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetApplicationService.cs"));
        var previewSource = await File.ReadAllTextAsync(Path.Combine(
            petRoot,
            "PetPreviewPlayer.cs"));
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
               mainXaml.Contains("x:Name=\"PetCenterPage\"", StringComparison.Ordinal) &&
               mainXaml.Contains("<pets:PetCenterView", StringComparison.Ordinal),
            "The shell must expose Codex Pets as a dedicated personalization destination.");
        Ensure(routeSource.Contains("Pets,", StringComparison.Ordinal) &&
               petShellSource.Contains(
                   "NavigateTo(Features.Navigation.AppRoute.Pets)",
                   StringComparison.Ordinal) &&
               navigationSource.Contains("AppRoute.Pets => 1040", StringComparison.Ordinal) &&
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
                     "AutomationProperties.Name=\"飞行雪绒状态预览\"",
                     "AutomationProperties.Name=\"宠物主操作\"",
                     "AutomationProperties.Name=\"复制斜杠 pet 命令\"",
                     "AutomationProperties.Name=\"打开 Codex\"",
                     "AutomationProperties.Name=\"确认已在 Codex 完成宠物选择\"",
                     "AutomationProperties.Name=\"卸载 Tessalume 管理的宠物文件\"",
                     "AutomationProperties.Name=\"恢复最近的宠物备份\"",
                     "AutomationProperties.Name=\"重新检查宠物状态\"",
                 })
        {
            Ensure(petXaml.Contains(marker, StringComparison.Ordinal),
                $"The pet center is missing accessibility metadata {marker}.");
        }

        foreach (var sharedStyle in new[]
                 {
                     "Style=\"{DynamicResource SectionEyebrowText}\"",
                     "Style=\"{DynamicResource PageTitleText}\"",
                     "Style=\"{DynamicResource PageDescriptionText}\"",
                     "Style=\"{DynamicResource ProductCard}\"",
                     "Style=\"{DynamicResource PrimaryActionButton}\"",
                     "Style=\"{DynamicResource QuietActionButton}\"",
                     "Style=\"{DynamicResource InsetCard}\"",
                 })
        {
            Ensure(petXaml.Contains(sharedStyle, StringComparison.Ordinal),
                $"A reusable pet view must resolve the shell-owned shared style at runtime: {sharedStyle}");
        }

        foreach (var guidance in new[]
                 {
                     "Settings",
                     "Pets",
                     "Refresh 后选择",
                     "输入 /pet",
                     "不会点击 Codex 界面、不会发送命令",
                     "只读取和管理当前用户 .codex\\pets",
                     "不读取聊天、账号、日志或其他 Codex 配置",
                     "应用主题不会偷偷安装宠物",
                 })
        {
            Ensure(petXaml.Contains(guidance, StringComparison.Ordinal),
                $"The newcomer guidance or privacy boundary is missing: {guidance}");
        }

        Ensure(petViewSource.Contains("CopyCommandRequested?.Invoke", StringComparison.Ordinal) &&
               !petViewSource.Contains("Clipboard.", StringComparison.Ordinal) &&
               !petViewSource.Contains("Process.Start", StringComparison.Ordinal),
            "The reusable view must expose copy/open intents without touching the clipboard or launching Codex itself.");
        Ensure(previewSource.Contains("MaximumCachedFrames = 6", StringComparison.Ordinal) &&
               previewSource.Contains("DecodePixelWidth = 288", StringComparison.Ordinal) &&
               previewSource.Contains("TimeSpan.FromMilliseconds(900)", StringComparison.Ordinal) &&
               previewSource.Contains("_timer.Stop()", StringComparison.Ordinal) &&
               petViewSource.Contains("SetPageActive", StringComparison.Ordinal) &&
               petViewSource.Contains("PetCenterView_IsVisibleChanged", StringComparison.Ordinal) &&
               petViewSource.Contains("PetCenterView_Unloaded", StringComparison.Ordinal),
            "The product preview must keep bounded decoding and stop its timer when the page is inactive.");
        Ensure(petXaml.Contains("Content=\"九宫格\" Tag=\"showcase\"", StringComparison.Ordinal) &&
               petServiceSource.Contains("种动作 ·", StringComparison.Ordinal) &&
               petServiceSource.Contains("向转身 ·", StringComparison.Ordinal) &&
               petServiceSource.Contains("有效格", StringComparison.Ordinal) &&
               snapshotSource.Contains("if (darkMode)", StringComparison.Ordinal) &&
               snapshotSource.Contains(
                   "FindPetButton(window.PetCenterPage, \"九宫格\")",
                   StringComparison.Ordinal),
            "The supplied nine-panel showcase and truthful 9-action/16-direction protocol summary must remain visible.");
        Ensure(publishedPackage!.Catalog.Protocol.States.Count - 2 == 9 &&
               publishedPackage.Catalog.Protocol.States.TakeLast(2).Sum(state => state.Frames) == 16 &&
               publishedPackage.Catalog.Protocol.UsedFrameCount == 74 &&
               publishedPackage.PreviewFiles.Count() == 6 &&
               publishedPackage.PreviewFiles.Any(preview =>
                   preview.Metadata.StateKey == "showcase" &&
                   preview.Metadata.Kind == "showcase" &&
                   preview.Metadata.Label == "动作九宫格" &&
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
        var previewDirectory = Path.Combine(root, "preview");
        Directory.CreateDirectory(themesDirectory);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(previewDirectory);
        var idlePreview = CreatePetPreviewProbe(
            Path.Combine(previewDirectory, "idle.png"),
            Color.FromRgb(117, 226, 255));
        var readyPreview = CreatePetPreviewProbe(
            Path.Combine(previewDirectory, "ready.png"),
            Color.FromRgb(151, 241, 186));
        var clipboard = new RecordingPetClipboard();
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
            MainWindow? window = null;
            PetCenterView? view = null;
            try
            {
                PetCenterPresentationState invalidManagementState;
                PetCenterPresentationState recoveredManagementState;
                using (var service = new PetApplicationService(
                           new PortableLayout(root, themesDirectory, dataDirectory),
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
                           invalidManagementState.PreviewFrames.Count == 6 &&
                           invalidManagementState.PreviewFrames.Any(frame =>
                               frame.Key == "showcase" &&
                               frame.Label == "动作九宫格" &&
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
                    new PortableLayout(root, themesDirectory, dataDirectory),
                    petOptions,
                    clipboard);
                InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                InvokeMainWindowMethod(window, "NavigateTo", AppRoute.Pets);
                CompleteInfoPageTransition(window);
                view = window.PetCenterPage;

                // The product shell owns real clipboard, installer, and process actions. This test
                // deliberately removes those subscriptions and verifies only the view's intent events.
                ClearPetCenterEventHandlers(view, preserveCopyCommand: true);
                PetCenterAction? requestedAction = null;
                view.PrimaryActionRequested += (_, action) => requestedAction = action;
                view.Render(invalidManagementState);
                FindPetButton(view, "九宫格")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Ensure(view.PreviewStateText.Text == "动作九宫格" &&
                       view.PetPreviewImage.Source is BitmapSource
                       {
                           PixelWidth: > 0 and <= 288,
                           PixelHeight: > 0,
                       },
                    "Selecting 九宫格 must decode and present the published showcase through the bounded preview player.");
                view.PrimaryActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Ensure(requestedAction == PetCenterAction.RecoverState &&
                       view.RestoreBackupButton.Visibility == Visibility.Collapsed,
                    "The corrupt-state UI must expose only the archival recovery action and hide restore.");

                var state = CreatePetCenterProbeState(idlePreview, readyPreview);
                view.Render(state);

                Ensure(view.HeaderStatusText.Text == "有更新" &&
                       view.InstallationStatusTitle.Text == "有更新" &&
                       view.InstallationStatusDetail.Text.Contains("备份", StringComparison.Ordinal) &&
                       Equals(view.PrimaryActionButton.Content, "安全更新") &&
                       view.PrimaryActionButton.IsEnabled &&
                       view.UninstallButton.Visibility == Visibility.Visible &&
                       view.AcknowledgeSelectionButton.Visibility == Visibility.Visible &&
                       view.RestoreBackupButton.Visibility == Visibility.Visible &&
                       Equals(view.RestoreBackupButton.ToolTip, "测试备份 · 可恢复") &&
                       view.ProductVersionText.Text == "1.0.0" &&
                       view.ProtocolText.Text == "Codex Pets v1" &&
                       view.InstallLocationText.Text.Contains(root, StringComparison.OrdinalIgnoreCase),
                    "Rendering must present one truthful status, one primary action, and package metadata.");

                requestedAction = null;
                var copyRequests = 0;
                view.CopyCommandRequested += (_, _) => copyRequests++;
                view.PrimaryActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var copyButton = FindPetButton(view, "复制 /pet");
                copyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Ensure(requestedAction == PetCenterAction.Update &&
                       copyRequests == 1 &&
                       clipboard.CopyCount == 1 &&
                       clipboard.LastText == PetApplicationService.WakeCommand,
                    "Pet actions must emit intents and copy exactly /pet through the injected clipboard boundary.");
                Ensure(AutomationProperties.GetName(copyButton) == "复制斜杠 pet 命令" &&
                       copyButton.Focusable &&
                       view.PrimaryActionButton.Focusable &&
                       copyButton.IsTabStop &&
                       view.PrimaryActionButton.IsTabStop &&
                       AutomationProperties.GetName(view.PetPreviewImage) == "飞行雪绒状态预览",
                    "Primary, copy, and preview controls must remain keyboard and UI Automation reachable.");

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

                ArrangePetCenter(host, 1040, 760);
                Ensure(Grid.GetColumn(view.DetailsPanel) == 2 &&
                       Grid.GetRow(view.DetailsPanel) == 0 &&
                       Grid.GetColumn(view.CompanionThemeCard) == 2 &&
                       Grid.GetRow(view.CompanionThemeCard) == 0,
                    "The wide pet center must retain a balanced preview/details and guide/theme composition.");

                ArrangePetCenter(host, 680, 720);
                Ensure(Grid.GetColumn(view.DetailsPanel) == 0 &&
                       Grid.GetRow(view.DetailsPanel) == 0 &&
                       Grid.GetColumn(view.PreviewCard) == 0 &&
                       Grid.GetRow(view.PreviewCard) == 2 &&
                       Grid.GetColumn(view.MetadataPanel) == 0 &&
                       Grid.GetRow(view.MetadataPanel) == 3 &&
                       Grid.GetColumn(view.CompanionThemeCard) == 0 &&
                       Grid.GetRow(view.CompanionThemeCard) == 1 &&
                       Grid.GetColumnSpan(view.CompanionThemeCard) == 3 &&
                       host.ScrollableWidth <= 0.5 &&
                       view.ActualWidth <= host.ViewportWidth + 0.5,
                    "The narrow pet center must stack without horizontal overflow or unreachable controls.");
                var primaryActionOrigin = view.PrimaryActionButton
                    .TransformToAncestor(host)
                    .Transform(new Point());
                Ensure(copyButton.ActualWidth > 0 &&
                       copyButton.ActualHeight >= 36 &&
                       view.PrimaryActionButton.ActualWidth > 0 &&
                       view.PrimaryActionButton.ActualHeight >= 43 &&
                       primaryActionOrigin.Y >= 0 &&
                       primaryActionOrigin.Y + view.PrimaryActionButton.ActualHeight <=
                       host.ViewportHeight + 0.5,
                    "Key pet actions must retain usable hit targets and the primary CTA must stay in the first compact viewport.");

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

                var previewTimer = GetPetPreviewTimer(view);
                SetPetPreviewPlayerActive(view, true);
                Ensure(previewTimer.IsEnabled,
                    "A route-active preview player with multiple bounded frames should animate.");
                view.Visibility = Visibility.Collapsed;
                InvokePetVisibilityChanged(view);
                Ensure(!previewTimer.IsEnabled,
                    "Collapsing the pet page must stop its preview timer immediately.");
                view.Visibility = Visibility.Visible;
                SetPetPreviewPlayerActive(view, true);
                Ensure(previewTimer.IsEnabled,
                    "A later visible route activation should resume the bounded preview.");
                view.SetPageActive(false);
                Ensure(!previewTimer.IsEnabled,
                    "Leaving the route must stop the preview even when the control remains visible.");

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

    private static PetCenterPresentationState CreatePetCenterProbeState(
        string idlePreview,
        string readyPreview) =>
        new()
        {
            Status = PetCenterStatus.UpdateAvailable,
            StatusTitle = "有更新",
            StatusDetail = "新版已完整校验；更新前会先备份当前受管文件。",
            ProductVersion = "1.0.0",
            ProtocolSummary = "Codex Pets v1",
            Author = "Tessalume Tests",
            LicenseSummary = "测试许可",
            InstallLocation = Path.Combine(Path.GetDirectoryName(idlePreview)!, "codex-pets"),
            PrimaryAction = PetCenterAction.Update,
            PrimaryActionText = "安全更新",
            PrimaryActionEnabled = true,
            CanUninstall = true,
            CanAcknowledgeSelection = true,
            CanRestoreBackup = true,
            LatestBackupLabel = "测试备份 · 可恢复",
            PreviewFrames =
            [
                new PetPreviewFrame("idle", "待机", idlePreview),
                new PetPreviewFrame("ready", "完成", readyPreview),
            ],
        };

    private static string CreatePetPreviewProbe(string path, Color color)
    {
        const int width = 48;
        const int height = 52;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                var inside = Math.Pow((x - width / 2d) / 18d, 2) +
                    Math.Pow((y - height / 2d) / 21d, 2) <= 1;
                pixels[offset] = inside ? color.B : (byte)0;
                pixels[offset + 1] = inside ? color.G : (byte)0;
                pixels[offset + 2] = inside ? color.R : (byte)0;
                pixels[offset + 3] = inside ? (byte)255 : (byte)0;
            }
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static void ArrangePetCenter(ScrollViewer host, double width, double height)
    {
        host.Width = width;
        host.Height = height;
        var size = new Size(width, height);
        for (var pass = 0; pass < 3; pass++)
        {
            host.Measure(size);
            host.Arrange(new Rect(size));
            host.UpdateLayout();
        }
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

    private static void ClearPetCenterEventHandlers(
        PetCenterView view,
        bool preserveCopyCommand = false)
    {
        foreach (var name in new[]
                 {
                     "RefreshRequested",
                     "PrimaryActionRequested",
                     "CopyCommandRequested",
                     "OpenCodexRequested",
                     "RecommendedThemeRequested",
                     "ApplyRecommendedThemeRequested",
                     "UninstallRequested",
                     "RestoreBackupRequested",
                     "SelectionAcknowledgementRequested",
                 })
        {
            if (preserveCopyCommand && name == "CopyCommandRequested")
            {
                continue;
            }

            typeof(PetCenterView).GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(view, null);
        }
    }

    private static DispatcherTimer GetPetPreviewTimer(PetCenterView view)
    {
        var player = typeof(PetCenterView).GetField(
                "_previewPlayer",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(view)
            ?? throw new MissingFieldException(nameof(PetCenterView), "_previewPlayer");
        return (DispatcherTimer)(player.GetType().GetField(
                "_timer",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(player)
            ?? throw new MissingFieldException(player.GetType().Name, "_timer"));
    }

    private static void SetPetPreviewPlayerActive(PetCenterView view, bool active)
    {
        var player = typeof(PetCenterView).GetField(
                "_previewPlayer",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(view)
            ?? throw new MissingFieldException(nameof(PetCenterView), "_previewPlayer");
        var method = player.GetType().GetMethod(
            "SetActive",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(player.GetType().Name, "SetActive");
        method.Invoke(player, [active]);
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

    private sealed class RecordingPetClipboard : IPetCommandClipboard
    {
        public int CopyCount { get; private set; }

        public string? LastText { get; private set; }

        public void Copy(string text)
        {
            CopyCount++;
            LastText = text;
        }
    }
}
