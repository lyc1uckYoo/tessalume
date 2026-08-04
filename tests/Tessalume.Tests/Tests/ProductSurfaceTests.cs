internal static partial class TestSuite
{
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
            try
            {
                var longMessage = string.Join(
                    "\n\n",
                    Enumerable.Repeat(
                        "这是一段用于验证更新说明排版和滚动区域的长文本，标题栏与关闭按钮必须保持固定。",
                        24));
                dialog = new ProductDialogWindow(
                    "发现 Tessalume v1.3.0",
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
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
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
        foreach (var region in new[] { "hero", "sidebar", "chat" })
        {
            foreach (var mode in new[] { "light", "dark" })
            {
                Ensure(sharedCss.Contains($"--tessalume-visual-{region}-{mode}-filter", StringComparison.Ordinal) &&
                       sharedCss.Contains($"--tessalume-visual-{region}-{mode}-opacity", StringComparison.Ordinal),
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
                },
            },
        }.Normalize();
        Ensure(normalized.Light.Hero.Brightness == 20 &&
               normalized.Light.Hero.Contrast == 180 &&
               normalized.Light.Hero.Saturation == 0 &&
               normalized.Light.Hero.Opacity == 100,
            "Persisted artwork values must be normalized before entering the renderer.");

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
               mainXaml.Contains("AutomationProperties.Name=\"首页横幅亮度\"", StringComparison.Ordinal) &&
               mainXaml.Contains("AutomationProperties.Name=\"聊天背景不透明度\"", StringComparison.Ordinal),
            "Keyboard focus and advanced image sliders require visible, descriptive accessibility metadata.");
        Ensure(quickXaml.Contains("AutomationProperties.Name=\"上一个可切换主题\"", StringComparison.Ordinal) &&
               quickXaml.Contains("AutomationProperties.Name=\"关闭主题浮窗\"", StringComparison.Ordinal) &&
               quickXaml.Contains("IsKeyboardFocused", StringComparison.Ordinal),
            "The icon-only quick bar controls must be named and visibly focusable.");
        Ensure(manifest.Contains("PerMonitorV2, PerMonitor", StringComparison.Ordinal),
            "The Windows application manifest must opt into per-monitor DPI scaling.");
    }

    static async Task Version13ProductWorkflowIsCompleteAsync()
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
                     "1.3 版本亮点",
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
        Ensure(project.Contains("<Version>1.3.0</Version>", StringComparison.Ordinal) &&
               firstRunXaml.Contains("{x:Static local:BrandInfo.VersionLabel}", StringComparison.Ordinal) &&
               !firstRunXaml.Contains("Text=\"v1.2\"", StringComparison.Ordinal) &&
               readme.Contains("## Tessalume 1.3.0", StringComparison.Ordinal) &&
               readme.Contains("十套内置旗舰主题", StringComparison.Ordinal) &&
               File.Exists(Path.Combine(repositoryRoot, "CHANGELOG.md")),
            "Version metadata and release documentation must agree on Tessalume 1.3.0.");
    }


    static async Task DiagnosticsRecoveryIsAvailableAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var xaml = await ReadMainWindowXamlAsync(appRoot);
        var mainSource = await ReadMainWindowSourceAsync(appRoot);
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
