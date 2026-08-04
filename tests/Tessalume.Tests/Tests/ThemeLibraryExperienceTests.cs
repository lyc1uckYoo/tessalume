using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Tessalume.App.Models;
using Tessalume.Core.Themes;

internal static partial class TestSuite
{
    static Task ThemeLibraryStateIsNormalizedAndVersionAwareAsync()
    {
        var now = DateTimeOffset.Now;
        var normalized = ThemeLibraryState.NormalizeUsage(
        [
            new ThemeUsageRecord { ThemeId = " sample.theme ", LastUsedAt = now.AddDays(-2), UseCount = 3 },
            new ThemeUsageRecord { ThemeId = "SAMPLE.THEME", LastUsedAt = now, UseCount = 8 },
            new ThemeUsageRecord { ThemeId = "other.theme", LastUsedAt = now.AddHours(-1), UseCount = 0 },
            new ThemeUsageRecord { ThemeId = " ", LastUsedAt = now },
        ]);
        Ensure(normalized.Count == 2 &&
               normalized[0].ThemeId == "SAMPLE.THEME" &&
               normalized[0].UseCount == 8 &&
               normalized[1].UseCount == 1,
            "Recent theme usage must be unique, newest-first, trimmed, and safely bounded.");
        Ensure(ThemeLibraryState.NormalizeSort("RECENT") == ThemeLibraryState.RecentSort &&
               ThemeLibraryState.NormalizeSort("unsupported") == ThemeLibraryState.DefaultSort,
            "Theme sort preferences must accept known values and recover from unknown values.");
        Ensure(ThemeLibraryState.CompareVersions("1.3.9", "1.4.0") == ThemeVersionRelation.Newer &&
               ThemeLibraryState.CompareVersions("v1.4", "1.4.0") == ThemeVersionRelation.Same &&
               ThemeLibraryState.CompareVersions("1.4.0", "1.4.0-beta.2") == ThemeVersionRelation.Older &&
               ThemeLibraryState.CompareVersions("custom-a", "custom-b") == ThemeVersionRelation.Unknown,
            "Theme replacement must distinguish upgrades, equal versions, downgrades, and unknown formats.");

        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var xaml = File.ReadAllText(Path.Combine(appRoot, "MainWindow.xaml"));
        var experienceSource = File.ReadAllText(Path.Combine(appRoot, "MainWindow.ThemeLibraryExperience.cs"));
        Ensure(xaml.Contains("x:Name=\"ThemeSortComboBox\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Name=\"ThemeDetailPanel\"", StringComparison.Ordinal) &&
               xaml.Contains("AllowDrop=\"True\"", StringComparison.Ordinal) &&
               xaml.Contains("Drop=\"ThemeLibraryPage_Drop\"", StringComparison.Ordinal) &&
               experienceSource.Contains("ConfirmThemeOverwriteAsync", StringComparison.Ordinal) &&
               experienceSource.Contains("收藏、图像调节和其他本地配置会继续保留", StringComparison.Ordinal),
            "Theme Library 2.0 must keep details, sorting, drag import, and safe conflict messaging wired to the product surface.");

        var root = Path.Combine(Path.GetTempPath(), $"tessalume-import-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "theme.ZIP");
            var text = Path.Combine(root, "theme.txt");
            File.WriteAllBytes(archive, []);
            File.WriteAllText(text, "not a theme");
            Ensure(ThemeLibraryState.ClassifyImportSource(root) == ThemeImportSourceKind.Directory &&
                   ThemeLibraryState.ClassifyImportSource(archive) == ThemeImportSourceKind.ZipArchive &&
                   ThemeLibraryState.ClassifyImportSource(text) == ThemeImportSourceKind.Unsupported,
                "Drag-and-drop import must accept only directories and ZIP packages.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    static Task ThemeLibraryDetailsAndRecentSortingWorkAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-library-experience-{Guid.NewGuid():N}");
        var themesDirectory = Path.Combine(root, "themes");
        var dataDirectory = Path.Combine(root, "data");
        Directory.CreateDirectory(themesDirectory);
        Directory.CreateDirectory(dataDirectory);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            MainWindow? window = null;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    window = new MainWindow(new PortableLayout(root, themesDirectory, dataDirectory));
                    InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var themes = (ObservableCollection<ThemeCardModel>)(
                        typeof(MainWindow).GetField("_themes", flags)?.GetValue(window)
                        ?? throw new MissingFieldException(nameof(MainWindow), "_themes"));
                    var usage = (Dictionary<string, ThemeUsageRecord>)(
                        typeof(MainWindow).GetField("_themeUsage", flags)?.GetValue(window)
                        ?? throw new MissingFieldException(nameof(MainWindow), "_themeUsage"));
                    var visible = (ObservableCollection<ThemeCardModel>)(
                        typeof(MainWindow).GetField("_visibleThemes", flags)?.GetValue(window)
                        ?? throw new MissingFieldException(nameof(MainWindow), "_visibleThemes"));

                    var older = CreateThemeCard(root, "older.theme", "旧主题", "1.0.0", "作者乙");
                    var recent = CreateThemeCard(root, "recent.theme", "近期主题", "1.1.0", "作者甲");
                    themes.Add(older);
                    themes.Add(recent);
                    usage[recent.ThemeId!] = new ThemeUsageRecord
                    {
                        ThemeId = recent.ThemeId!,
                        LastUsedAt = DateTimeOffset.Now,
                        UseCount = 4,
                    };
                    typeof(MainWindow).GetField("_themeLibrarySort", flags)?.SetValue(
                        window,
                        ThemeLibraryState.RecentSort);
                    InvokeMainWindowMethod(window, "ApplyThemeLibraryFilter", new object[] { null! });
                    Ensure(visible.Count == 2 && ReferenceEquals(visible[0], recent),
                        "Recently used sorting must place the latest used theme first.");

                    InvokeMainWindowMethod(window, "SelectTheme", recent);
                    window.ThemeDetailsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(window.ThemeDetailPanel.Visibility == Visibility.Visible &&
                           window.ThemeDetailPanel.ThemeNameText.Text == "近期主题" &&
                           window.ThemeDetailPanel.LightFallback.Visibility == Visibility.Visible &&
                           window.ThemeDetailPanel.DarkFallback.Visibility == Visibility.Visible,
                        "Theme details must open from the selection dock and explain missing light/dark previews.");
                    InvokeMainWindowMethod(window, "CloseThemeDetailPanel");
                    Ensure(window.ThemeDetailPanel.Visibility == Visibility.Collapsed,
                        "Theme details must close without changing the selected theme.");

                    window.ThemeSortComboBox.SelectedValue = ThemeLibraryState.NameSort;
                    await Task.Delay(180);
                    using var store = new UiPreferencesStore(dataDirectory);
                    var saved = store.Load();
                    Ensure(saved.ThemeLibrarySort == ThemeLibraryState.NameSort,
                        "The selected theme library sort must persist in portable preferences.");
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
                throw new InvalidOperationException("The theme library 2.0 interaction workflow failed.", failure);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static ThemeCardModel CreateThemeCard(
        string root,
        string id,
        string name,
        string version,
        string author)
    {
        var directory = Path.Combine(root, id);
        Directory.CreateDirectory(directory);
        var package = new ThemePackage(
            directory,
            Path.Combine(directory, "manifest.json"),
            new ThemeManifest
            {
                Id = id,
                Name = name,
                Description = $"{name}的完整主题说明。",
                Version = version,
                Author = author,
                Capabilities = new ThemeCapabilities { Light = true, Dark = true },
                Template = new ThemeTemplate { Id = "flagship", Version = "1.0", Style = "shared" },
            },
            null,
            Path.Combine(directory, "theme.js"),
            new Dictionary<string, string>(),
            null,
            null);
        return new ThemeCardModel(
            new ThemeCatalogItem(directory, package, new ThemeValidationResult()),
            loadPreview: false);
    }
}
