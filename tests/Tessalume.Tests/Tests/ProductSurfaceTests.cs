internal static partial class TestSuite
{
    static Task ArtworkEditorSupportsPreciseInputAndTransferAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editor = new Tessalume.App.Controls.ArtworkAdjustmentEditor
                {
                    RegionKey = "hero",
                    Title = "首页横幅",
                };
                Tessalume.App.Controls.ArtworkAdjustmentChangedEventArgs? changed = null;
                var copied = false;
                var pasted = false;
                editor.AdjustmentChanged += (_, args) => changed = args;
                editor.CopyRequested += (_, _) => copied = true;
                editor.PasteRequested += (_, _) => pasted = true;

                editor.SetAdjustment(new ThemeArtworkAdjustment { Brightness = 91, Blur = 2.5 });
                Ensure(editor.BrightnessValue.Text == "91%" && editor.BlurValue.Text == "2.5 px",
                    "The precise value editors must reflect the current adjustment.");
                editor.BrightnessValue.Text = "137%";
                var commit = typeof(Tessalume.App.Controls.ArtworkAdjustmentEditor).GetMethod(
                    "CommitValueEditor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("ArtworkAdjustmentEditor.CommitValueEditor");
                commit.Invoke(editor, [editor.BrightnessValue]);
                Ensure(editor.BrightnessSlider.Value == 137 &&
                       changed is { Region: "hero", Property: "brightness", Value: 137 },
                    "Precise text input must update the same live adjustment pipeline as the slider.");

                editor.BrightnessValue.Text = "NaN";
                commit.Invoke(editor, [editor.BrightnessValue]);
                Ensure(editor.BrightnessSlider.Value == 137 && editor.BrightnessValue.Text == "137%",
                    "Non-finite precise input must be rejected without reaching the WPF slider value.");

                editor.CopyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                editor.SetPasteAvailable(true);
                editor.PasteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Ensure(copied && pasted && editor.PasteButton.IsEnabled,
                    "Region copy and paste actions must be exposed by the reusable editor.");
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
                "The advanced artwork editor precise-input workflow failed.",
                failure);
        }
        return Task.CompletedTask;
    }

    static Task ArtworkEditorHistoryAndPresetsWorkAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-artwork-editor-{Guid.NewGuid():N}");
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
                    var themeItems = (System.Collections.ObjectModel.ObservableCollection<Tessalume.App.Models.ThemeCardModel>)(
                        typeof(MainWindow).GetField("_themes", flags)?.GetValue(window)
                        ?? throw new MissingFieldException(nameof(MainWindow), "_themes"));
                    themeItems.Add(model);
                    typeof(MainWindow).GetField("_selectedTheme", flags)?.SetValue(window, model);
                    typeof(MainWindow).GetField("_activeThemeId", flags)?.SetValue(window, "editor.probe");
                    typeof(MainWindow).GetMethod("UpdateVisualAdjustmentControls", flags)?.Invoke(window, null);

                    Ensure(window.HeroAdjustmentEditor.Visibility == Visibility.Visible &&
                           window.SidebarAdjustmentEditor.Visibility == Visibility.Collapsed &&
                           window.ChatAdjustmentEditor.Visibility == Visibility.Collapsed,
                        "The focused artwork workflow must initially show only the home banner editor.");
                    window.VisualChatRegionButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(window.HeroAdjustmentEditor.Visibility == Visibility.Collapsed &&
                           window.SidebarAdjustmentEditor.Visibility == Visibility.Collapsed &&
                           window.ChatAdjustmentEditor.Visibility == Visibility.Visible &&
                           Equals(window.VisualChatRegionButton.Tag, "active"),
                        "Selecting an artwork region must replace the editor instead of displaying three dense panels.");
                    window.VisualHeroRegionButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

                    var settings = (Dictionary<string, ThemeVisualSettings>)(
                        typeof(MainWindow).GetField("_themeVisualSettings", flags)?.GetValue(window)
                        ?? throw new MissingFieldException(nameof(MainWindow), "_themeVisualSettings"));
                    window.HeroAdjustmentEditor.BrightnessSlider.Value = 125;
                    Ensure(settings["editor.probe"].Light.Hero.Brightness == 125 &&
                           window.VisualUndoButton.IsEnabled,
                        "A live slider edit must enter the per-theme undo history.");

                    window.VisualUndoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(settings["editor.probe"].Light.Hero.Brightness == 100 &&
                           window.VisualRedoButton.IsEnabled,
                        "Undo must restore the previous complete theme settings snapshot.");
                    window.VisualRedoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(settings["editor.probe"].Light.Hero.Brightness == 125,
                        "Redo must restore the reverted image adjustment.");

                    window.HeroAdjustmentEditor.CopyButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    window.SidebarAdjustmentEditor.PasteButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(settings["editor.probe"].Light.Sidebar.Brightness == 125,
                        "Pasting a region must transfer all values through the main editor state.");

                    window.VisualPresetNameBox.Text = "明亮构图";
                    window.SaveVisualPresetButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    window.HeroAdjustmentEditor.BrightnessSlider.Value = 160;
                    window.ApplyVisualPresetButton.RaiseEvent(
                        new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(settings["editor.probe"].Light.Hero.Brightness == 125 &&
                           window.VisualPresetComboBox.SelectedItem is ThemeArtworkPreset { Name: "明亮构图" } &&
                           window.ImportVisualPresetButton.IsEnabled &&
                           window.ExportVisualPresetButton.IsEnabled,
                        "A personal preset must save and restore the complete current visual mode.");

                    await Task.Delay(220);
                    using var savedStore = new UiPreferencesStore(dataDirectory);
                    var saved = savedStore.Load();
                    Ensure(saved.SchemaVersion == 3 &&
                           saved.ArtworkPresets is [{ Name: "明亮构图" }],
                        "Personal image presets must persist in schema three.");
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
                    "The advanced artwork editor history and preset workflow failed.",
                    failure);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    static async Task ArtworkPresetFilesRoundTripSafelyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-artwork-preset-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "柔和背景" + ThemeArtworkPresetExchange.FileExtension);
            var preset = new ThemeArtworkPreset
            {
                Name = "柔和背景",
                Settings = new ThemeVisualModeSettings
                {
                    Hero = new ThemeArtworkAdjustment
                    {
                        Brightness = 112,
                        Contrast = 94,
                        Zoom = 118,
                        OffsetY = -12,
                    },
                    Sidebar = new ThemeArtworkAdjustment
                    {
                        Saturation = 86,
                        Opacity = 92,
                        Grayscale = 8,
                    },
                    Chat = new ThemeArtworkAdjustment
                    {
                        Brightness = 82,
                        Blur = 2.5,
                        HueRotation = -14,
                    },
                },
            };
            await ThemeArtworkPresetExchange.ExportAsync(path, preset);
            var imported = await ThemeArtworkPresetExchange.ImportAsync(path);
            var text = await File.ReadAllTextAsync(path);
            Ensure(imported == preset.Normalize() &&
                   text.Contains($"\"format\": \"{ThemeArtworkPresetExchange.FormatId}\"", StringComparison.Ordinal) &&
                   text.Contains("\"schemaVersion\": 1", StringComparison.Ordinal) &&
                   !text.Contains("themeId", StringComparison.OrdinalIgnoreCase) &&
                   !text.Contains("path", StringComparison.OrdinalIgnoreCase),
                "A shared artwork preset must round-trip as a versioned parameter-only file.");

            var replacement = preset with
            {
                Settings = preset.Settings with
                {
                    Chat = preset.Settings.Chat with { Brightness = 76 },
                },
            };
            await ThemeArtworkPresetExchange.ExportAsync(path, replacement);
            Ensure(await ThemeArtworkPresetExchange.ImportAsync(path) == replacement.Normalize() &&
                   !Directory.EnumerateFiles(root, "*.tmp-*", SearchOption.TopDirectoryOnly).Any(),
                "Preset export must atomically replace an existing file without leaving temporary output.");

            var validText = await File.ReadAllTextAsync(path);
            var unknownPath = Path.Combine(root, "unknown.json");
            await File.WriteAllTextAsync(unknownPath, "{\"unexpected\":true," + validText[1..]);
            Ensure(await ImportIsRejectedAsync(unknownPath),
                "Preset import must reject unknown fields instead of silently accepting a different format.");

            var outOfRangePath = Path.Combine(root, "out-of-range.json");
            var outOfRangeText = validText.Replace(
                "\"brightness\": 112",
                "\"brightness\": 999",
                StringComparison.Ordinal);
            Ensure(!string.Equals(outOfRangeText, validText, StringComparison.Ordinal),
                "The preset safety fixture did not locate the expected brightness field.");
            await File.WriteAllTextAsync(outOfRangePath, outOfRangeText);
            Ensure(await ImportIsRejectedAsync(outOfRangePath),
                "Preset import must reject renderer values outside the supported range.");

            var oversizedPath = Path.Combine(root, "oversized.json");
            await File.WriteAllBytesAsync(
                oversizedPath,
                new byte[ThemeArtworkPresetExchange.MaximumFileBytes + 1]);
            Ensure(await ImportIsRejectedAsync(oversizedPath),
                "Preset import must enforce its small parameter-file size boundary.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return;

        static async Task<bool> ImportIsRejectedAsync(string path)
        {
            try
            {
                await ThemeArtworkPresetExchange.ImportAsync(path);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
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
                    "发现 Tessalume v1.4.0",
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
                    "你正在使用 v1.4.0，暂时没有可安装的新版本。",
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
        var runtimeSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            "theme-runtime-v2.js"));
        var sharedCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            ThemePayloadBuilder.SharedTemplateStyleFileName));
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var mainWindowXaml = await ReadMainWindowXamlAsync(appRoot);
        var mainWindowSource = await ReadMainWindowSourceAsync(appRoot);
        Ensure(runtimeSource.Contains("setVisualSettings", StringComparison.Ordinal) &&
               runtimeSource.Contains("__TESSALUME_STAGED_VISUAL_SETTINGS__", StringComparison.Ordinal),
            "The runtime must stage and live-update persisted artwork settings.");
        Ensure(mainWindowXaml.Contains("x:Name=\"SettingsThemeControlBar\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("Click=\"SettingsPreviousTheme_Click\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("Click=\"SettingsNextTheme_Click\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("Click=\"SettingsColorMode_Click\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"VisualEditingModeText\"", StringComparison.Ordinal),
            "Advanced artwork settings must expose the compact live theme and color-mode controls.");
        Ensure(mainWindowSource.Contains("ApplyRelativeSettingsThemeAsync", StringComparison.Ordinal) &&
               mainWindowSource.Contains("ToggleCodexColorSchemeAsync", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("VisualLightModeButton", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("VisualDarkModeButton", StringComparison.Ordinal),
            "The settings editor must follow the real Codex mode instead of a detached parameter-only toggle.");
        Ensure(mainWindowXaml.Contains("x:Name=\"VisualUndoButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"VisualRedoButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"VisualOriginalPreviewButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"VisualPresetComboBox\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"ImportVisualPresetButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("x:Name=\"ExportVisualPresetButton\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("CopyRequested=\"ArtworkAdjustmentEditor_CopyRequested\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("PasteRequested=\"ArtworkAdjustmentEditor_PasteRequested\"", StringComparison.Ordinal) &&
               mainWindowSource.Contains("RecordVisualUndo", StringComparison.Ordinal) &&
               mainWindowSource.Contains("SetOriginalPreviewAsync", StringComparison.Ordinal) &&
               mainWindowSource.Contains("ThemeArtworkPresetExchange.ImportAsync", StringComparison.Ordinal) &&
               mainWindowSource.Contains("ThemeArtworkPresetExchange.ExportAsync", StringComparison.Ordinal),
            "The image editor must support reversible editing, original comparison, region transfer, and shareable personal presets.");
        Ensure(mainWindowXaml.Contains("PreviewKeyDown=\"ValueEditor_PreviewKeyDown\"", StringComparison.Ordinal) &&
               mainWindowXaml.Contains("LostKeyboardFocus=\"ValueEditor_LostKeyboardFocus\"", StringComparison.Ordinal),
            "Every advanced image value must support precise keyboard entry in addition to sliders.");
        foreach (var region in new[] { "hero", "sidebar", "chat" })
        {
            foreach (var mode in new[] { "light", "dark" })
            {
                Ensure(sharedCss.Contains($"--tessalume-visual-{region}-{mode}-filter", StringComparison.Ordinal) &&
                       sharedCss.Contains($"--tessalume-visual-{region}-{mode}-opacity", StringComparison.Ordinal) &&
                       sharedCss.Contains($"--tessalume-visual-{region}-{mode}-translate", StringComparison.Ordinal) &&
                       sharedCss.Contains($"--tessalume-visual-{region}-{mode}-scale", StringComparison.Ordinal),
                    $"The shared template is missing {mode} {region} adjustment variables.");
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
                },
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
               normalized.Light.Hero.Blur == 20,
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
        var normalizedPreset = new ThemeArtworkPreset
        {
            Name = $"  {new string('A', 40)}  ",
            Settings = new ThemeVisualModeSettings
            {
                Chat = new ThemeArtworkAdjustment { Blur = 99 },
            },
        }.Normalize();
        Ensure(normalizedPreset.Name.Length == 32 && normalizedPreset.Settings.Chat.Blur == 20,
            "Personal artwork presets must normalize names and renderer values before persistence.");
        Ensure(runtimeSource.Contains("grayscale(${grayscale})", StringComparison.Ordinal) &&
               runtimeSource.Contains("hue-rotate(${hueRotation}deg)", StringComparison.Ordinal) &&
               runtimeSource.Contains("blur(${blur}px)", StringComparison.Ordinal) &&
               runtimeSource.Contains("translateVariable", StringComparison.Ordinal) &&
               runtimeSource.Contains("scaleVariable", StringComparison.Ordinal),
            "The runtime must compose color effects and non-destructive crop correction.");

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
            Ensure(rules.Length >= 3, $"{directory} must expose all three adjustable artwork layers.");
            foreach (var rule in rules)
            {
                var body = rule.Groups["body"].Value;
                Ensure(!body.Contains("filter:", StringComparison.OrdinalIgnoreCase) &&
                       !body.Contains("opacity:", StringComparison.OrdinalIgnoreCase),
                    $"{directory} hard-codes artwork correction inside {rule.Groups["selector"].Value.Trim()}.");
            }
        }
    }

    static async Task MainProductSurfacesShareDesignSystemAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var xaml = await ReadMainWindowXamlAsync(appRoot);
        var source = await ReadMainWindowSourceAsync(appRoot);
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

        Ensure(source.Contains("DiagnosticHealthBodyText", StringComparison.Ordinal) &&
               source.Contains("EmptyStateAction_Click", StringComparison.Ordinal),
            "Product surfaces must expose live diagnostic summaries and useful empty-state actions.");
        Ensure(!cardModel.Contains("BUILT-IN", StringComparison.Ordinal) &&
               !cardModel.Contains("LOCAL", StringComparison.Ordinal),
            "Chinese product surfaces should not fall back to legacy English theme badges.");
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
               mainSource.Contains("_quickSwitchWindow.Close();", StringComparison.Ordinal) &&
               mainSource.Contains("Key.F", StringComparison.Ordinal) &&
               mainSource.Contains("Key.I", StringComparison.Ordinal) &&
               mainSource.Contains("Key.F5", StringComparison.Ordinal),
            "Small-screen fitting and documented keyboard shortcuts must remain wired.");
        Ensure(mainXaml.Contains("x:Key=\"KeyboardFocusVisual\"", StringComparison.Ordinal) &&
               !mainXaml.Contains("FocusVisualStyle\" Value=\"{x:Null}", StringComparison.Ordinal) &&
               mainXaml.Contains("AutomationProperties.Name=\"图像亮度\"", StringComparison.Ordinal) &&
               mainXaml.Contains("AutomationProperties.Name=\"图像模糊\"", StringComparison.Ordinal),
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

    static async Task Version14ProductWorkflowIsCompleteAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var xaml = await ReadMainWindowXamlAsync(appRoot);
        var source = await ReadMainWindowSourceAsync(appRoot);
        var dialogXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "ProductDialogWindow.xaml"));
        var dialogSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "ProductDialogWindow.xaml.cs"));
        var firstRunXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "FirstRunWindow.xaml"));
        var project = await File.ReadAllTextAsync(Path.Combine(appRoot, "Tessalume.App.csproj"));
        var readme = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "README.md"));

        foreach (var marker in new[]
                 {
                     "x:Name=\"ThemeSearchBox\"",
                     "x:Name=\"AllThemesFilterButton\"",
                     "x:Name=\"LightThemesFilterButton\"",
                     "x:Name=\"DarkThemesFilterButton\"",
                     "x:Name=\"ThemeResultText\"",
                     "x:Name=\"ToastPanel\"",
                     "x:Name=\"AboutInfoPanel\"",
                     "x:Name=\"AboutLibrarySummaryText\"",
                     "1.4 版本亮点",
                 })
        {
            Ensure(xaml.Contains(marker, StringComparison.Ordinal), $"The product interface is missing {marker}.");
        }

        Ensure(source.Contains("ApplyThemeLibraryFilter", StringComparison.Ordinal) &&
               source.Contains("ThemeSearchBox_FocusChanged", StringComparison.Ordinal) &&
               source.Contains("ShowProductConfirmation", StringComparison.Ordinal) &&
               source.Contains("ShowToast", StringComparison.Ordinal) &&
               !source.Contains("MessageBox.Show", StringComparison.Ordinal),
            "The main interface must use searchable filtering and unified in-product feedback.");
        Ensure(xaml.Contains("Property=\"Cursor\" Value=\"IBeam\"", StringComparison.Ordinal) &&
               xaml.Contains("GotKeyboardFocus=\"ThemeSearchBox_FocusChanged\"", StringComparison.Ordinal),
            "The search field must keep a clear text caret and hide its placeholder while focused.");
        Ensure(dialogXaml.Contains("DialogAccentBrush", StringComparison.Ordinal) &&
               dialogXaml.Contains("IsDefault=\"True\"", StringComparison.Ordinal) &&
               dialogXaml.Contains("IsCancel=\"True\"", StringComparison.Ordinal) &&
               dialogSource.Contains("CancelButton.IsDefault = true", StringComparison.Ordinal),
            "The product dialog must support consistent styling and keyboard-safe confirmation.");
        Ensure(dialogXaml.Contains("TextOptions.TextHintingMode=\"Fixed\"", StringComparison.Ordinal) &&
               firstRunXaml.Contains("TextOptions.TextHintingMode=\"Fixed\"", StringComparison.Ordinal) &&
               !xaml.Contains("Storyboard.TargetName=\"PrimaryMotion\"", StringComparison.Ordinal) &&
               !xaml.Contains("x:Name=\"ButtonScale\"", StringComparison.Ordinal),
            "Shared actions, dialogs, and onboarding must keep stable text rendering without hover scaling.");
        Ensure(project.Contains("<Version>1.4.0</Version>", StringComparison.Ordinal) &&
               firstRunXaml.Contains("{x:Static local:BrandInfo.VersionLabel}", StringComparison.Ordinal) &&
               !firstRunXaml.Contains("Text=\"v1.2\"", StringComparison.Ordinal) &&
               readme.Contains("## Tessalume 1.4.0", StringComparison.Ordinal) &&
               readme.Contains("十套内置旗舰主题", StringComparison.Ordinal) &&
               File.Exists(Path.Combine(repositoryRoot, "CHANGELOG.md")),
            "Version metadata and release documentation must agree on Tessalume 1.4.0.");
    }


    static async Task DiagnosticsRecoveryIsAvailableAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var xaml = await ReadMainWindowXamlAsync(appRoot);
        var mainSource = await ReadMainWindowSourceAsync(appRoot);
        var diagnosticsSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.Diagnostics.cs"));
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
            Ensure(xaml.Contains(marker, StringComparison.Ordinal),
                $"The recovery surface is missing {marker}.");
        }

        Ensure(mainSource.Contains("RefreshDiagnosticsAsync", StringComparison.Ordinal) &&
               mainSource.Contains("RestoreBuiltInThemes_Click", StringComparison.Ordinal) &&
               !mainSource.Contains("CopyDiagnosticReport_Click", StringComparison.Ordinal) &&
               !xaml.Contains("复制诊断报告", StringComparison.Ordinal),
            "The diagnostics page must retain local status and recovery without clipboard report actions.");
        var diagnosticsHandlerStart = diagnosticsSource.IndexOf("private async void Diagnostics_Click", StringComparison.Ordinal);
        var diagnosticsHandlerEnd = diagnosticsSource.IndexOf("private async Task RefreshDiagnosticsAsync", StringComparison.Ordinal);
        var diagnosticsHandler = diagnosticsHandlerStart >= 0 && diagnosticsHandlerEnd > diagnosticsHandlerStart
            ? diagnosticsSource[diagnosticsHandlerStart..diagnosticsHandlerEnd]
            : string.Empty;
        Ensure(xaml.Contains("x:Name=\"DiagnosticLoadingPanel\"", StringComparison.Ordinal) &&
               diagnosticsHandler.Contains("ShowInfoPage(RightPane.Diagnostics)", StringComparison.Ordinal) &&
               diagnosticsHandler.IndexOf("ShowInfoPage(RightPane.Diagnostics)", StringComparison.Ordinal) <
               diagnosticsHandler.IndexOf("await RefreshDiagnosticsAsync()", StringComparison.Ordinal) &&
               diagnosticsHandler.Contains("Dispatcher.Yield(DispatcherPriority.Render)", StringComparison.Ordinal),
            "Diagnostics navigation must render an explicit loading state before local inspection begins.");
        var settingsStart = xaml.IndexOf("x:Name=\"SettingsInfoPanel\"", StringComparison.Ordinal);
        var aboutStart = xaml.IndexOf("x:Name=\"AboutInfoPanel\"", StringComparison.Ordinal);
        var diagnosticsStart = xaml.IndexOf("x:Name=\"DiagnosticsInfoPanel\"", StringComparison.Ordinal);
        var settingsMarkup = settingsStart >= 0 && aboutStart > settingsStart
            ? xaml[settingsStart..aboutStart]
            : string.Empty;
        var aboutMarkup = aboutStart >= 0 && diagnosticsStart > aboutStart
            ? xaml[aboutStart..diagnosticsStart]
            : string.Empty;
        Ensure(!settingsMarkup.Contains("x:Name=\"StartupCheckBox\"", StringComparison.Ordinal) &&
               !settingsMarkup.Contains("x:Name=\"AutomaticUpdatesCheckBox\"", StringComparison.Ordinal) &&
               aboutMarkup.Contains("x:Name=\"StartupCheckBox\"", StringComparison.Ordinal) &&
               aboutMarkup.Contains("x:Name=\"AutomaticUpdatesCheckBox\"", StringComparison.Ordinal) &&
               settingsMarkup.Contains("x:Name=\"VisualHeroRegionButton\"", StringComparison.Ordinal),
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
