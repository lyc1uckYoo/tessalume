using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

internal static partial class TestSuite
{
    static Task ArtworkWorkbenchSupportsPreciseInputAndSourceActionsAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var inspector = new ArtworkInspectorView();
                ArtworkParameterValueChangedEventArgs? changed = null;
                ArtworkParameterTextChangedEventArgs? optionChanged = null;
                var imageRequested = false;
                var imageCleared = false;
                var resetRequested = false;
                inspector.NumericValueChanged += (_, args) => changed = args;
                inspector.TextValueChanged += (_, args) => optionChanged = args;
                inspector.ChooseImageRequested += (_, _) => imageRequested = true;
                inspector.ClearImageRequested += (_, _) => imageCleared = true;
                inspector.ResetGroupRequested += (_, _) => resetRequested = true;

                inspector.SetAdjustment(new ThemeArtworkAdjustment
                {
                    Brightness = 91,
                    Blur = 2.5,
                    CustomImagePath = "personalization/images/probe.png",
                    OverlayColor = "#223344",
                    OverlayOpacity = 28,
                    GradientStrength = 42,
                    Vignette = 16,
                    BlendMode = "soft-light",
                });
                inspector.SetSourceSummary("本地图片 · probe.png", hasLocalImage: true);
                Ensure(inspector.BrightnessValue.Text == "91%" &&
                       inspector.BlurValue.Text == "2.5 px" &&
                       inspector.SourceBadgeText.Text.Contains("probe.png", StringComparison.Ordinal) &&
                       inspector.OverlayColorValue.Text == "#223344" &&
                       inspector.BlendModeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem
                       {
                           Tag: "soft-light",
                       },
                    "The workbench inspector must reflect precise values, image source, and advanced effects.");
                inspector.SetGroup(ArtworkParameterGroup.Effects);
                Ensure(Equals(inspector.ResetGroupButton.Content, "恢复效果组") &&
                       inspector.ResetParameterButton.IsEnabled,
                    "The workbench must name the exact parameter and group reset scopes.");
                var original = new ThemeArtworkAdjustment
                {
                    Brightness = 82,
                    Zoom = 143,
                    OffsetX = 17,
                    Vignette = 31,
                    CustomImagePath = "personalization/images/probe.png",
                };
                var resetSettings = ArtworkSettingsReducer.Reset(
                    new ThemeVisualSettings
                    {
                        Light = new ThemeVisualModeSettings { Hero = original },
                    },
                    ArtworkResetRequest.ForGroup(
                        ArtworkColorMode.Light,
                        ArtworkRegion.Hero,
                        ArtworkParameterGroup.Basic));
                var basicReset = ArtworkSettingsAccessor.GetAdjustment(
                    resetSettings,
                    ArtworkColorMode.Light,
                    ArtworkRegion.Hero);
                Ensure(basicReset.Brightness == 100 &&
                       basicReset.Zoom == 143 &&
                       basicReset.OffsetX == 17 &&
                       basicReset.Vignette == 31 &&
                       basicReset.CustomImagePath == original.CustomImagePath,
                    "Resetting basic adjustments must preserve composition, effects, and the selected image.");
                inspector.SetGroup(ArtworkParameterGroup.Basic);
                inspector.BrightnessValue.Text = "137%";
                var commit = typeof(ArtworkInspectorView).GetMethod(
                    "CommitValueEditor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("ArtworkInspectorView.CommitValueEditor");
                commit.Invoke(inspector, [inspector.BrightnessValue]);
                Ensure(inspector.BrightnessSlider.Value == 137 &&
                       changed is { Parameter: ArtworkParameter.Brightness, Value: 137 } &&
                       inspector.ResetGroupButton.IsEnabled,
                    "Precise text input must update the same live adjustment pipeline as the slider.");

                inspector.ResetGroupButton.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Ensure(resetRequested,
                    "The visible reset action must remain clickable after a live parameter edit.");

                inspector.BrightnessValue.Text = "NaN";
                commit.Invoke(inspector, [inspector.BrightnessValue]);
                Ensure(inspector.BrightnessSlider.Value == 137 && inspector.BrightnessValue.Text == "137%",
                    "Non-finite precise input must be rejected without reaching the WPF slider value.");

                inspector.ChooseImageButton.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                inspector.ClearImageButton.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                inspector.BlendModeComboBox.SelectedItem = inspector.BlendModeComboBox.Items
                    .OfType<System.Windows.Controls.ComboBoxItem>()
                    .Single(item => Equals(item.Tag, "screen"));
                Ensure(imageRequested && imageCleared &&
                       optionChanged is { Parameter: ArtworkParameter.BlendMode, Value: "screen" },
                    "Image-source actions and enumerated effect choices must remain available in the inspector.");
            }
            catch (System.Reflection.TargetInvocationException exception)
            {
                failure = exception.InnerException ?? exception;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException(
                "The Artwork Workbench 3.0 precise-input workflow failed.",
                failure);
        }
        return Task.CompletedTask;
    }

    static Task ArtworkWorkbenchHistoryAndDisplaySettingsWorkAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-artwork-workbench-{Guid.NewGuid():N}");
        var themesDirectory = Path.Combine(root, "themes");
        var dataDirectory = Path.Combine(root, "data");
        Directory.CreateDirectory(themesDirectory);
        Directory.CreateDirectory(dataDirectory);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
            MainWindow? window = null;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    window = new MainWindow(new PortableLayout(root, themesDirectory, dataDirectory));
                    var initialize = typeof(MainWindow).GetMethod(
                        "EnsureMainUiInitialized",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(nameof(MainWindow), "EnsureMainUiInitialized");
                    initialize.Invoke(window, null);

                    var package = new ThemePackage(
                        root,
                        Path.Combine(root, "manifest.json"),
                        new ThemeManifest
                        {
                            Id = "editor.probe",
                            Name = "编辑器探针",
                            Version = "1.0.0",
                            Author = "Tessalume",
                            Capabilities = new ThemeCapabilities { Light = true, Dark = true },
                        },
                        null,
                        null,
                        new Dictionary<string, string>(),
                        null,
                        null);
                    var model = new Tessalume.App.Models.ThemeCardModel(
                        new ThemeCatalogItem(root, package, new ThemeValidationResult()),
                        loadPreview: false)
                    {
                        IsApplied = true,
                    };
                    var flags = System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic;
                    var updateWorkbench = typeof(MainWindow).GetMethod(
                        "UpdateArtworkWorkbenchContext",
                        flags) ?? throw new MissingMethodException(
                            nameof(MainWindow),
                            "UpdateArtworkWorkbenchContext");
                    var themeItems = (System.Collections.ObjectModel.ObservableCollection<Tessalume.App.Models.ThemeCardModel>)(
                        typeof(MainWindow).GetField("_themes", flags)?.GetValue(window)
                        ?? throw new MissingFieldException(nameof(MainWindow), "_themes"));
                    themeItems.Add(model);
                    typeof(MainWindow).GetField("_selectedTheme", flags)?.SetValue(window, model);
                    typeof(MainWindow).GetField("_activeThemeId", flags)?.SetValue(window, "editor.probe");
                    updateWorkbench.Invoke(window, null);

                    Ensure(window.ArtworkWorkbench.HeroRegionButton.IsEnabled &&
                           window.ArtworkWorkbench.InspectorScroller.IsEnabled &&
                           Equals(window.ArtworkWorkbench.HeroRegionButton.Tag, "active") &&
                           window.ArtworkWorkbench.EditingRegion == ArtworkRegion.Hero,
                        "The focused artwork workflow must initially target the home banner canvas.");
                    window.ArtworkWorkbench.ChatRegionButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(window.ArtworkWorkbench.EditingRegion == ArtworkRegion.Chat &&
                           Equals(window.ArtworkWorkbench.ChatRegionButton.Tag, "active") &&
                           Equals(window.ArtworkWorkbench.HeroRegionButton.Tag, "inactive") &&
                           window.ArtworkWorkbench.CanvasTitleText.Text.Contains("聊天背景", StringComparison.Ordinal),
                        "Selecting an artwork region must retarget the single canvas and inspector.");
                    window.ArtworkWorkbench.HeroRegionButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

                    var settings = (Dictionary<string, ThemeVisualSettings>)(
                        typeof(MainWindow).GetField("_themeVisualSettings", flags)?.GetValue(window)
                        ?? throw new MissingFieldException(nameof(MainWindow), "_themeVisualSettings"));
                    window.ArtworkWorkbench.Inspector.BrightnessSlider.Value = 125;
                    Ensure(settings["editor.probe"].Light.Hero.Brightness == 125 &&
                           window.ArtworkWorkbench.UndoButton.IsEnabled,
                        "A live slider edit must enter the per-theme undo history.");

                    window.ArtworkWorkbench.UndoButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(settings["editor.probe"].Light.Hero.Brightness == 100 &&
                           window.ArtworkWorkbench.RedoButton.IsEnabled,
                        "Undo must restore the previous complete theme settings snapshot.");
                    window.ArtworkWorkbench.RedoButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(settings["editor.probe"].Light.Hero.Brightness == 125,
                        "Redo must restore the reverted image adjustment.");

                    window.ArtworkWorkbench.Inspector.BasicGroupButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    window.ArtworkWorkbench.Inspector.ResetGroupButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(settings["editor.probe"].Light.Hero.Brightness == 100,
                        "The reset button must reset the visible parameter group through the main editor state.");
                    window.ArtworkWorkbench.UndoButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(settings["editor.probe"].Light.Hero.Brightness == 125,
                        "A parameter-group reset must remain undoable.");

                    window.DisplayPreferencesPage.MotionComboBox.SelectedValue = "reduced";
                    window.DisplayPreferencesPage.TextScaleComboBox.SelectedValue = "large";
                    window.DisplayPreferencesPage.DensityComboBox.SelectedValue = "spacious";

                    await Task.Delay(220);
                    using var savedStore = new UiPreferencesStore(dataDirectory);
                    var saved = savedStore.Load();
                    Ensure(saved.SchemaVersion == UiPreferences.CurrentSchemaVersion &&
                           saved.ThemeVisualOverrides["editor.probe"].Display is
                           { MotionIntensity: "reduced", TextScale: "large", Density: "spacious" },
                        "Display preferences must persist with the current theme without a separate profile workflow.");
                }
                catch (System.Reflection.TargetInvocationException exception)
                {
                    failure = exception.InnerException ?? exception;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    if (window is not null) await window.DisposeAsync();
                    dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                }
            });
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            if (failure is not null)
            {
                throw new InvalidOperationException(
                    "The Artwork Workbench 3.0 history and display-settings workflow failed.",
                    failure);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    static async Task PersonalImagesAreStoredSafelyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-personal-images-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(root, "data");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var sourcePath = Path.Combine(root, "background.png");
            await File.WriteAllBytesAsync(sourcePath, OnePixelPng);
            var store = new Tessalume.App.Features.Personalization.PersonalImageStore(dataDirectory);
            var storedPath = await store.ImportAsync(sourcePath);
            var duplicatePath = await store.ImportAsync(sourcePath);
            var absolutePath = store.ResolvePath(storedPath);
            var runtimeSettings = store.ResolveForRuntime(new ThemeVisualSettings
            {
                Light = new ThemeVisualModeSettings
                {
                    Hero = new ThemeArtworkAdjustment { CustomImagePath = storedPath },
                },
            });

            Ensure(storedPath == duplicatePath &&
                   storedPath.StartsWith("personalization/images/", StringComparison.Ordinal) &&
                   absolutePath is not null && File.Exists(absolutePath) &&
                   runtimeSettings.Light.Hero.CustomImagePath == absolutePath,
                "Personal images must be content-addressed inside the portable data directory and resolve only at runtime.");
            Ensure(store.ResolvePath("../outside.png") is null &&
                   store.ResolvePath(Path.Combine(root, "outside.png")) is null,
                "Personal image references must never escape the dedicated image store.");

            var unsupportedPath = Path.Combine(root, "background.txt");
            await File.WriteAllTextAsync(unsupportedPath, "not an image");
            var rejected = false;
            try
            {
                _ = await store.ImportAsync(unsupportedPath);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Ensure(rejected, "Unsupported personal image extensions must be rejected before persistence.");

            var disguisedPath = Path.Combine(root, "disguised.png");
            await File.WriteAllTextAsync(disguisedPath, "not really a png");
            rejected = false;
            try
            {
                _ = await store.ImportAsync(disguisedPath);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Ensure(rejected, "A personal image extension must match its file signature.");

            var corruptPngPath = Path.Combine(root, "corrupt-frame.png");
            await File.WriteAllBytesAsync(corruptPngPath, OnePixelPng[..16]);
            rejected = false;
            try
            {
                _ = await store.ImportAsync(corruptPngPath);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Ensure(rejected,
                "A signature-correct image with an undecodable frame must be rejected before persistence.");

            var truncatedPath = Path.Combine(root, "truncated.png");
            await File.WriteAllBytesAsync(
                truncatedPath,
                [137, 80, 78, 71, 13, 10, 26, 10]);
            rejected = false;
            try
            {
                _ = await store.ImportAsync(truncatedPath);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Ensure(rejected,
                "A signature-correct but undecodable personal image must be rejected before persistence.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static Task WpfShellLoadsSplitResourcesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-wpf-shell-{Guid.NewGuid():N}");
        var themes = Path.Combine(root, "themes");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(themes);
        Directory.CreateDirectory(data);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            Application? application = null;
            MainWindow? window = null;
            try
            {
                application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                window = new MainWindow(new PortableLayout(root, themes, data));
                var initialize = typeof(MainWindow).GetMethod(
                    "EnsureMainUiInitialized",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(nameof(MainWindow), "EnsureMainUiInitialized");
                initialize.Invoke(window, null);
                Ensure(window.Content is not null,
                    "The compiled MainWindow visual tree did not load.");
            }
            catch (System.Reflection.TargetInvocationException exception)
            {
                failure = exception.InnerException ?? exception;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (window is not null)
                {
                    window.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                application?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            if (failure is not null)
            {
                throw new InvalidOperationException("The WPF shell failed to load split resources.", failure);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    static Task LongProductDialogUsesScrollableBodyAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            ProductDialogWindow? dialog = null;
            ProductDialogWindow? compactDialog = null;
            try
            {
                var longMessage = string.Join(
                    "\n\n",
                    Enumerable.Repeat(
                        "这是一段用于验证更新说明排版和滚动区域的长文本，标题栏与关闭按钮必须保持固定。",
                        24));
                dialog = new ProductDialogWindow(
                    "发现 Tessalume v2.0.1",
                    longMessage,
                    ProductDialogKind.Confirmation,
                    darkMode: false,
                    confirmText: "下载并安装",
                    cancelText: "取消",
                    dangerous: false);

                var surface = dialog.Content as FrameworkElement
                    ?? throw new InvalidOperationException("The product dialog surface did not load.");
                surface.Measure(new Size(480, double.PositiveInfinity));
                surface.Arrange(new Rect(0, 0, 480, surface.DesiredSize.Height));
                surface.UpdateLayout();

                Ensure(
                    dialog.DialogMessageScrollViewer.VerticalScrollBarVisibility ==
                    System.Windows.Controls.ScrollBarVisibility.Auto,
                    "Long product messages must use an automatic vertical scrollbar.");
                Ensure(dialog.DialogMessageScrollViewer.ScrollableHeight > 0,
                    "Long product messages must retain scrollable content instead of clipping it.");
                Ensure(dialog.DialogMessageScrollViewer.ViewportHeight <= 340.5,
                    "The message viewport must stay bounded so the action buttons remain visible.");
                Ensure(dialog.CloseButton.VerticalAlignment == VerticalAlignment.Top &&
                       System.Windows.Controls.Grid.GetRow(dialog.CloseButton) == 0,
                    "The close button must stay pinned to the top-right header row.");
                Ensure(System.Windows.Controls.Grid.GetRow(dialog.DialogMessageScrollViewer) == 1,
                    "The dialog message must remain separate from the fixed title bar.");

                compactDialog = new ProductDialogWindow(
                    "当前已是最新版本",
                    "你正在使用 v2.0.0，暂时没有可安装的新版本。",
                    ProductDialogKind.Information,
                    darkMode: false,
                    confirmText: "知道了",
                    cancelText: null,
                    dangerous: false);
                var compactSurface = compactDialog.Content as FrameworkElement
                    ?? throw new InvalidOperationException("The compact product dialog surface did not load.");
                compactSurface.Measure(new Size(440, double.PositiveInfinity));
                compactSurface.Arrange(new Rect(0, 0, 440, compactSurface.DesiredSize.Height));
                compactSurface.UpdateLayout();
                Ensure(compactSurface.DesiredSize.Height <= 190,
                    "Short product messages must use a compact content-driven height without a blank lower panel.");
                Ensure(compactDialog.Width <= 440.5,
                    "Short product messages must keep a focused compact width.");

                var snapshotPath = Environment.GetEnvironmentVariable("TESSALUME_DIALOG_SNAPSHOT");
                if (!string.IsNullOrWhiteSpace(snapshotPath))
                {
                    var fullSnapshotPath = Path.GetFullPath(snapshotPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullSnapshotPath)!);
                    var width = Math.Max(1, (int)Math.Ceiling(surface.ActualWidth));
                    var height = Math.Max(1, (int)Math.Ceiling(surface.ActualHeight));
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        width,
                        height,
                        96,
                        96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(surface);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(fullSnapshotPath);
                    encoder.Save(stream);
                }

                var compactSnapshotPath = Environment.GetEnvironmentVariable("TESSALUME_COMPACT_DIALOG_SNAPSHOT");
                if (!string.IsNullOrWhiteSpace(compactSnapshotPath))
                {
                    var fullSnapshotPath = Path.GetFullPath(compactSnapshotPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullSnapshotPath)!);
                    var width = Math.Max(1, (int)Math.Ceiling(compactSurface.ActualWidth));
                    var height = Math.Max(1, (int)Math.Ceiling(compactSurface.ActualHeight));
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        width,
                        height,
                        96,
                        96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(compactSurface);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(fullSnapshotPath);
                    encoder.Save(stream);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                compactDialog?.Close();
                dialog?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException(
                "The long product dialog layout did not preserve scrolling and fixed controls.",
                failure);
        }

        return Task.CompletedTask;
    }

    static async Task ArtworkAdjustmentsAreRuntimeOwnedAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimeSource = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        var sharedCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            ThemePayloadBuilder.SharedTemplateStyleFileName));
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var mainWindowXaml = await ReadMainWindowXamlAsync(appRoot);
        var mainWindowSource = await ReadMainWindowSourceAsync(appRoot);
        var artworkWorkbenchRoot = Path.Combine(
            appRoot,
            "Features",
            "Personalization",
            "ArtworkWorkbench");
        var artworkWorkbenchSource = string.Join("\n", await Task.WhenAll(Directory
            .EnumerateFiles(artworkWorkbenchRoot, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => File.ReadAllTextAsync(path))));
        Ensure(runtimeSource.Contains("setVisualSettings", StringComparison.Ordinal) &&
               runtimeSource.Contains("__TESSALUME_STAGED_VISUAL_SETTINGS__", StringComparison.Ordinal),
            "The runtime must stage and live-update persisted artwork settings.");
        Ensure(runtimeSource.Contains("region === \"sidebar\"", StringComparison.Ordinal) &&
               runtimeSource.Contains("${lengthCss(placement?.width)} auto", StringComparison.Ordinal) &&
               runtimeSource.Contains("placementCss(state.placement, state.region)", StringComparison.Ordinal) &&
               runtimeSource.Contains("encodedReferenceHeight", StringComparison.Ordinal) &&
               runtimeSource.Contains("sidebarReferenceTop", StringComparison.Ordinal),
            "Sidebar artwork must preserve its horizontal scale and full-height top edge while clipping the bottom on shorter windows.");
        Ensure(!runtimeSource.Contains("canonicalArtworkTarget", StringComparison.Ordinal) &&
               !runtimeSource.Contains("visualSurfaceReferenceSizes", StringComparison.Ordinal) &&
               runtimeSource.Contains("storedSizeMode === \"explicit\"", StringComparison.Ordinal) &&
               artworkWorkbenchSource.Contains("CommitResponsiveCover", StringComparison.Ordinal) &&
               artworkWorkbenchSource.Contains("AdaptResponsiveCover", StringComparison.Ordinal) &&
               artworkWorkbenchSource.Contains("UseResponsiveCoverMode", StringComparison.Ordinal),
            "Hero and chat artwork must use native responsive cover with one focal point and one uniform zoom, never a frozen canvas.");
        Ensure(mainWindowXaml.Contains("x:Name=\"SettingsThemeControlBar\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("Click=\"SettingsPreviousTheme_Click\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("Click=\"SettingsNextTheme_Click\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"ArtworkWorkbench\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"LightModeButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"DarkModeButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"SyncStatusText\"", StringComparison.Ordinal),
            "Artwork Workbench 3.0 must expose current-theme, independent edit-mode, and apply-state controls.");
        Ensure(mainWindowSource.Contains("ApplyRelativeSettingsThemeAsync", StringComparison.Ordinal) &&
               mainWindowSource.Contains("ToggleCodexColorSchemeAsync", StringComparison.Ordinal) &&
               mainWindowSource.Contains("ArtworkWorkbench_EditingModeChanged", StringComparison.Ordinal) &&
               mainWindowSource.Contains("UpdateArtworkWorkbenchContext", StringComparison.Ordinal),
            "The settings shell must preserve real Codex controls while the workbench may edit either stored mode offline.");
        Ensure(mainWindowXaml.Contains("x:Name=\"UndoButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"RedoButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"CompareButton\"", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("x:Name=\"CopyButton\"", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("x:Name=\"PasteButton\"", StringComparison.Ordinal) &&
               mainWindowSource.Contains("InitializeArtworkWorkbench", StringComparison.Ordinal) &&
               mainWindowSource.Contains("ArtworkWorkbench.SettingsChanged", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("个人图像方案", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("跨模式与恢复", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("主题作者工具", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("ExportArtworkDefaultsButton", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("x:Name=\"PresetComboBox\"", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("x:Name=\"CopyModeButton\"", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("x:Name=\"ResetModeButton\"", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("x:Name=\"ResetThemeButton\"", StringComparison.Ordinal) &&
               !artworkWorkbenchSource.Contains("ImportPresetRequested", StringComparison.Ordinal) &&
               !artworkWorkbenchSource.Contains("ExportPresetRequested", StringComparison.Ordinal) &&
               !artworkWorkbenchSource.Contains("ArtworkResetScope.Mode", StringComparison.Ordinal) &&
               !artworkWorkbenchSource.Contains("ArtworkResetScope.Theme", StringComparison.Ordinal) &&
               !artworkWorkbenchSource.Contains("CopyMode(", StringComparison.Ordinal),
            "The workbench must retain reversible local editing without parameter transfer, personal presets, or coarse recovery workflows.");
        Ensure(mainWindowXaml.Contains("x:Name=\"ChooseImageButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"ClearImageButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("<personalization:DisplaySettingsView", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("ExperienceProfilesView", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("体验方案", StringComparison.Ordinal) &&
               runtimeSource.Contains("customImageKey", StringComparison.Ordinal) &&
               runtimeSource.Contains("tessalumeMotion", StringComparison.Ordinal) &&
               runtimeSource.Contains("tessalumeReadability", StringComparison.Ordinal) &&
               sharedCss.Contains("background-blend-mode", StringComparison.Ordinal) &&
               sharedCss.Contains("data-tessalume-density", StringComparison.Ordinal),
            "Personalization must connect local images, visual effects, readability, and per-theme display preferences without a profile workflow.");
        Ensure(mainWindowXaml.Contains(
                   "Event=\"PreviewKeyDown\" Handler=\"ValueEditor_PreviewKeyDown\"",
                   StringComparison.Ordinal) &&
               mainWindowXaml.Contains(
                   "Event=\"LostKeyboardFocus\" Handler=\"ValueEditor_LostKeyboardFocus\"",
                   StringComparison.Ordinal),
            "Every advanced image value must support precise keyboard entry in addition to sliders.");
        Ensure(mainWindowXaml.Contains("x:Name=\"HeroRegionButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"BasicGroupButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"ResetParameterButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"ResetGroupButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"ResetRegionButton\"", StringComparison.Ordinal) &&
               artworkWorkbenchSource.Contains("RestoreGroupToTheme", StringComparison.Ordinal) &&
               artworkWorkbenchSource.Contains("RestoreSlotToTheme", StringComparison.Ordinal),
            "The workbench must expose only the local parameter, group, and current-slot restore actions.");
        foreach (var region in new[] { "hero", "sidebar", "chat" })
        {
            foreach (var mode in new[] { "light", "dark" })
            {
                Ensure(sharedCss.Contains($"--tessalume-visual-{region}-{mode}-filter", StringComparison.Ordinal) &&
                       sharedCss.Contains($"--tessalume-visual-{region}-{mode}-opacity", StringComparison.Ordinal) &&
                       sharedCss.Contains($"--tessalume-visual-{region}-{mode}-background-size", StringComparison.Ordinal) &&
                       sharedCss.Contains($"--tessalume-visual-{region}-{mode}-background-position", StringComparison.Ordinal),
                    $"The shared template is missing final {mode} {region} placement variables.");
            }
        }

        var normalized = new ThemeVisualSettings
        {
            Light = new ThemeVisualModeSettings
            {
                Hero = new ThemeArtworkAdjustment
                {
                    Brightness = -1,
                    Contrast = 900,
                    Saturation = -5,
                    Opacity = 400,
                    Zoom = 900,
                    OffsetX = -900,
                    OffsetY = 900,
                    Grayscale = 400,
                    HueRotation = -900,
                    Blur = 900,
                    OverlayColor = "invalid",
                    OverlayOpacity = 900,
                    GradientStrength = -50,
                    Vignette = 900,
                    BlendMode = "unsupported",
                },
            },
            Display = new ThemeDisplayPreferences
            {
                MotionIntensity = "unknown",
                TextScale = "LARGE",
                Density = "spacious",
            },
        }.Normalize();
        Ensure(normalized.Light.Hero.Brightness == 20 &&
               normalized.Light.Hero.Contrast == 180 &&
               normalized.Light.Hero.Saturation == 0 &&
               normalized.Light.Hero.Opacity == 100 &&
               normalized.Light.Hero.Zoom == 200 &&
               normalized.Light.Hero.OffsetX == -200 &&
               normalized.Light.Hero.OffsetY == 200 &&
               normalized.Light.Hero.Grayscale == 100 &&
               normalized.Light.Hero.HueRotation == -180 &&
               normalized.Light.Hero.Blur == 20 &&
               normalized.Light.Hero.OverlayColor == "#000000" &&
               normalized.Light.Hero.OverlayOpacity == 100 &&
               normalized.Light.Hero.GradientStrength == 0 &&
               normalized.Light.Hero.Vignette == 100 &&
               normalized.Light.Hero.BlendMode == "normal" &&
               normalized.Display is
               { MotionIntensity: "full", TextScale: "large", Density: "spacious" },
            "Persisted artwork values must be normalized before entering the renderer.");
        var finiteFallback = new ThemeArtworkAdjustment
        {
            Brightness = double.NaN,
            Zoom = double.PositiveInfinity,
            Blur = double.NegativeInfinity,
        }.Normalize();
        Ensure(finiteFallback.Brightness == 100 &&
               finiteFallback.Zoom == 100 &&
               finiteFallback.Blur == 0,
            "Non-finite artwork values must fall back to safe renderer defaults.");
        Ensure(runtimeSource.Contains("grayscale(${grayscale})", StringComparison.Ordinal) &&
               runtimeSource.Contains("hue-rotate(${hueRotation}deg)", StringComparison.Ordinal) &&
               runtimeSource.Contains("blur(${blur}px)", StringComparison.Ordinal) &&
               runtimeSource.Contains("setPlacementVariables(state", StringComparison.Ordinal) &&
               runtimeSource.Contains("compositionMode === \"legacy\" ?", StringComparison.Ordinal) &&
               runtimeSource.Contains(": \"0px 0px\"", StringComparison.Ordinal) &&
               runtimeSource.Contains(": \"1\"", StringComparison.Ordinal),
            "The runtime must compose effects and one final placement while neutralizing transforms outside explicit legacy slots.");

        var rulePattern = new Regex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.CultureInvariant);
        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(repositoryRoot, "themes")))
        {
            var cssPath = Path.Combine(directory, "skin.css");
            var css = await File.ReadAllTextAsync(cssPath);
            var rules = rulePattern.Matches(css).Cast<Match>().Where(match =>
            {
                var selector = match.Groups["selector"].Value;
                return selector.Contains("aside.app-shell-left-panel::after", StringComparison.Ordinal) ||
                       selector.Contains("-is-task main.", StringComparison.Ordinal) && selector.Contains("-main::before", StringComparison.Ordinal) ||
                       selector.Contains("-home>div:first-child>div:first-child>div:first-child::before", StringComparison.Ordinal);
            }).ToArray();
            foreach (var rule in rules)
            {
                var body = rule.Groups["body"].Value;
                Ensure(!Regex.IsMatch(
                           body,
                           @"(?im)^\s*(?:background(?:-image|-size|-position)?|filter|opacity|transform|translate|scale)\s*:",
                           RegexOptions.CultureInvariant),
                    $"{directory} hard-codes an adjustable artwork value inside {rule.Groups["selector"].Value.Trim()}.");
            }
        }
    }

    static async Task MainProductSurfacesShareDesignSystemAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var xaml = await ReadMainWindowXamlAsync(appRoot);
        var source = await ReadMainWindowSourceAsync(appRoot);
        var displaySettingsXaml = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "Personalization",
            "DisplaySettingsView.xaml"));
        var artworkWorkbenchXaml = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "Personalization",
            "ArtworkWorkbench",
            "Presentation",
            "ArtworkWorkbenchView.xaml"));
        var diagnosticsViewSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "Diagnostics",
            "DiagnosticsView.xaml.cs"));
        var cardModel = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Models",
            "ThemeCardModel.cs"));

        foreach (var marker in new[]
                 {
                     "x:Key=\"PageTitleText\"",
                     "x:Key=\"ProductCard\"",
                     "x:Name=\"EmptyStateActionButton\"",
                     "x:Name=\"DiagnosticHealthTitleText\"",
                     "Content=\"选择主题文件夹\"",
                     "Text=\"创作项目中心\"",
                     "x:Name=\"ProjectDetailCard\"",
                 })
        {
            Ensure(xaml.Contains(marker, StringComparison.Ordinal),
                $"The unified product surface is missing {marker}.");
        }

        Ensure(diagnosticsViewSource.Contains("DiagnosticHealthBodyText", StringComparison.Ordinal) &&
               source.Contains("EmptyStateAction_Click", StringComparison.Ordinal),
            "Product surfaces must expose live diagnostic summaries and useful empty-state actions.");
        Ensure(!cardModel.Contains("BUILT-IN", StringComparison.Ordinal) &&
               !cardModel.Contains("LOCAL", StringComparison.Ordinal),
            "Chinese product surfaces should not fall back to legacy English theme badges.");
        Ensure(xaml.Contains("x:Name=\"PersonalizationInfoPanel\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Name=\"PersonalizationPageTitleText\"", StringComparison.Ordinal) &&
               Regex.Matches(xaml, "x:Name=\\\"SettingsThemeControlBar\\\"").Count == 1 &&
               !displaySettingsXaml.Contains("CurrentThemeNameText", StringComparison.Ordinal) &&
               !displaySettingsXaml.Contains("PreviousThemeButton", StringComparison.Ordinal) &&
               !displaySettingsXaml.Contains("WORKBENCH 3.0 READY", StringComparison.Ordinal) &&
               !artworkWorkbenchXaml.Contains("x:Name=\"ThemeNameText\"", StringComparison.Ordinal) &&
               !artworkWorkbenchXaml.Contains("x:Name=\"ThemeContextText\"", StringComparison.Ordinal),
            "Personalization routes must share one compact context bar instead of repeating theme and mode headers inside each page.");
    }


    static async Task AdaptiveLayoutAndKeyboardAccessibilityAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var mainXaml = await ReadMainWindowXamlAsync(appRoot);
        var mainSource = await ReadMainWindowSourceAsync(appRoot);
        var quickXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "ThemeQuickSwitchWindow.xaml"));
        var manifest = await File.ReadAllTextAsync(Path.Combine(appRoot, "app.manifest"));

        Ensure(mainXaml.Contains("x:Name=\"AdaptiveViewport\"", StringComparison.Ordinal) &&
               mainXaml.Contains("x:Name=\"AdaptiveScale\"", StringComparison.Ordinal) &&
               mainXaml.Contains("MinWidth=\"760\" MinHeight=\"420\"", StringComparison.Ordinal),
            "The main product surface must scale down instead of extending beyond a small work area.");
        Ensure(mainSource.Contains("FitWindowToWorkArea", StringComparison.Ordinal) &&
               mainSource.Contains("AdaptiveViewport_SizeChanged", StringComparison.Ordinal) &&
               mainSource.Contains("CloseQuickSwitchWindow(rememberClosed:", StringComparison.Ordinal) &&
               mainSource.Contains("Key.F", StringComparison.Ordinal) &&
               mainSource.Contains("Key.I", StringComparison.Ordinal) &&
               mainSource.Contains("Key.F5", StringComparison.Ordinal),
            "Small-screen fitting and documented keyboard shortcuts must remain wired.");
        Ensure(mainXaml.Contains("x:Key=\"KeyboardFocusVisual\"", StringComparison.Ordinal) &&
               !mainXaml.Contains("FocusVisualStyle\" Value=\"{x:Null}", StringComparison.Ordinal) &&
               mainXaml.Contains("AutomationProperties.Name=\"图像亮度\"", StringComparison.Ordinal) &&
               mainXaml.Contains("AutomationProperties.Name=\"图像柔化\"", StringComparison.Ordinal),
            "Keyboard focus and advanced image sliders require visible, descriptive accessibility metadata.");
        Ensure(quickXaml.Contains("AutomationProperties.Name=\"上一个可切换主题\"", StringComparison.Ordinal) &&
               quickXaml.Contains("AutomationProperties.Name=\"关闭主题浮窗\"", StringComparison.Ordinal) &&
               quickXaml.Contains("IsKeyboardFocused", StringComparison.Ordinal),
            "The icon-only quick bar controls must be named and visibly focusable.");
        Ensure(mainXaml.Contains("TextOptions.TextRenderingMode=\"ClearType\"", StringComparison.Ordinal) &&
               mainXaml.Contains("TextOptions.TextHintingMode=\"Fixed\"", StringComparison.Ordinal) &&
               quickXaml.Contains("TextOptions.TextHintingMode=\"Fixed\"", StringComparison.Ordinal) &&
               !quickXaml.Contains("Storyboard.TargetName=\"MotionRoot\"", StringComparison.Ordinal),
            "Product text must use stable glyph metrics and quick-bar hover feedback must not rescale text-bearing controls.");
        Ensure(manifest.Contains("PerMonitorV2, PerMonitor", StringComparison.Ordinal),
            "The Windows application manifest must opt into per-monitor DPI scaling.");
    }

    static async Task DiagnosticsRecoveryIsAvailableAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var xaml = await ReadMainWindowXamlAsync(appRoot);
        var mainSource = await ReadMainWindowSourceAsync(appRoot);
        var diagnosticsSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.Diagnostics.cs"));
        var diagnosticsViewXaml = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "Diagnostics",
            "DiagnosticsView.xaml"));
        var diagnosticsViewSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "Diagnostics",
            "DiagnosticsView.xaml.cs"));
        var diagnosticsServiceSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "Diagnostics",
            "DiagnosticsInspectionService.cs"));
        var aboutViewXaml = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "About",
            "AboutView.xaml"));
        var appSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "App.xaml.cs"));
        var installerSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Infrastructure",
            "BuiltInAssetInstaller.cs"));
        var logSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Infrastructure",
            "LocalLog.cs"));

        foreach (var marker in new[]
                 {
                     "Content=\"打开日志目录\"",
                     "Content=\"恢复内置主题\"",
                 })
        {
            Ensure(diagnosticsViewXaml.Contains(marker, StringComparison.Ordinal),
                $"The recovery surface is missing {marker}.");
        }

        Ensure(mainSource.Contains("RefreshDiagnosticsAsync", StringComparison.Ordinal) &&
               mainSource.Contains("DiagnosticsPage_RestoreBuiltInThemesRequested", StringComparison.Ordinal) &&
               !mainSource.Contains("CopyDiagnosticReport_Click", StringComparison.Ordinal) &&
               !diagnosticsViewXaml.Contains("复制诊断报告", StringComparison.Ordinal) &&
               diagnosticsViewSource.Contains("RestoreBuiltInThemesRequested", StringComparison.Ordinal) &&
               diagnosticsServiceSource.Contains("InspectAsync", StringComparison.Ordinal),
            "The diagnostics page must retain local status and recovery without clipboard report actions.");
        var diagnosticsHandlerStart = diagnosticsSource.IndexOf("private async void Diagnostics_Click", StringComparison.Ordinal);
        var diagnosticsHandlerEnd = diagnosticsSource.IndexOf("private async Task RefreshDiagnosticsAsync", StringComparison.Ordinal);
        var diagnosticsHandler = diagnosticsHandlerStart >= 0 && diagnosticsHandlerEnd > diagnosticsHandlerStart
            ? diagnosticsSource[diagnosticsHandlerStart..diagnosticsHandlerEnd]
            : string.Empty;
        Ensure(diagnosticsViewXaml.Contains("x:Name=\"DiagnosticLoadingPanel\"", StringComparison.Ordinal) &&
               diagnosticsHandler.Contains("NavigateTo(Features.Navigation.AppRoute.Diagnostics)", StringComparison.Ordinal) &&
               diagnosticsHandler.IndexOf("NavigateTo(Features.Navigation.AppRoute.Diagnostics)", StringComparison.Ordinal) <
               diagnosticsHandler.IndexOf("await RefreshDiagnosticsAsync()", StringComparison.Ordinal) &&
               diagnosticsSource.Contains("DiagnosticsPage.SetLoading(true)", StringComparison.Ordinal) &&
               diagnosticsSource.Contains("Dispatcher.Yield(DispatcherPriority.Render)", StringComparison.Ordinal),
            "Diagnostics navigation must render an explicit loading state before local inspection begins.");
        var settingsStart = xaml.IndexOf("x:Name=\"SettingsInfoPanel\"", StringComparison.Ordinal);
        var displayPreferencesStart = xaml.IndexOf("x:Name=\"DisplayPreferencesInfoPanel\"", StringComparison.Ordinal);
        var settingsMarkup = settingsStart >= 0 && displayPreferencesStart > settingsStart
            ? xaml[settingsStart..displayPreferencesStart]
            : string.Empty;
        Ensure(!settingsMarkup.Contains("x:Name=\"StartupCheckBox\"", StringComparison.Ordinal) &&
               !settingsMarkup.Contains("x:Name=\"AutomaticUpdatesCheckBox\"", StringComparison.Ordinal) &&
               aboutViewXaml.Contains("x:Name=\"StartupCheckBox\"", StringComparison.Ordinal) &&
               aboutViewXaml.Contains("x:Name=\"AutomaticUpdatesCheckBox\"", StringComparison.Ordinal) &&
               settingsMarkup.Contains("x:Name=\"ArtworkWorkbench\"", StringComparison.Ordinal),
            "Application behavior belongs on About while personalization remains a focused image workflow.");
        Ensure(appSource.Contains("LocalLog.Initialize(layout.DataDirectory)", StringComparison.Ordinal) &&
               appSource.IndexOf("LocalLog.Initialize(layout.DataDirectory)", StringComparison.Ordinal) <
               appSource.IndexOf("new MainWindow(layout)", StringComparison.Ordinal),
            "Local logging must be initialized before the main product surface starts.");
        Ensure(logSource.Contains("MaximumLogBytes", StringComparison.Ordinal) &&
               logSource.Contains("tessalume.previous.log", StringComparison.Ordinal) &&
               logSource.Contains("TakeLast", StringComparison.Ordinal),
            "Local logs must be bounded, rotated, and suitable for concise diagnostics.");
        Ensure(installerSource.Contains("RestoreDeletedThemes", StringComparison.Ordinal) &&
               installerSource.Contains("File.Delete(path)", StringComparison.Ordinal) &&
               installerSource.Contains("EnsureInstalled(layout)", StringComparison.Ordinal),
            "Built-in recovery must clear the deletion marker and reinstall embedded themes.");
    }

}
