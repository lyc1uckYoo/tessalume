using System.Reflection;
using System.Windows.Threading;

internal static partial class TestSuite
{
    static async Task NavigationRoutesKeepDenseWorkflowsSeparatedAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
        var mainXaml = await ReadMainWindowXamlAsync(appRoot);
        var navigationSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Shell",
            "Navigation",
            "MainWindow.NavigationRouter.cs"));
        var routeSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "Navigation",
            "AppRoute.cs"));
        var aboutSource = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Features",
            "About",
            "AboutView.xaml.cs"));

        foreach (var marker in new[]
                 {
                     "x:Name=\"SettingsButton\"",
                     "x:Name=\"DisplayPreferencesButton\"",
                     "x:Name=\"DataButton\"",
                     "x:Name=\"AboutButton\"",
                     "x:Name=\"SettingsInfoPanel\"",
                     "x:Name=\"DisplayPreferencesInfoPanel\"",
                 })
        {
            Ensure(mainXaml.Contains(marker, StringComparison.Ordinal),
                $"The application shell is missing the routed destination marker {marker}.");
        }

        var sidebarEnd = mainXaml.IndexOf("x:Name=\"ThemeLibraryPage\"", StringComparison.Ordinal);
        var sidebarMarkup = sidebarEnd > 0 ? mainXaml[..sidebarEnd] : mainXaml;
        Ensure(!sidebarMarkup.Contains("Click=\"RefreshThemes_Click\"", StringComparison.Ordinal) &&
               !sidebarMarkup.Contains("Click=\"RestoreTheme_Click\"", StringComparison.Ordinal),
            "The sidebar must contain destinations, not immediate refresh or restore commands.");
        Ensure(routeSource.Contains("internal enum AppRoute", StringComparison.Ordinal) &&
               routeSource.Contains("ArtworkStudio", StringComparison.Ordinal) &&
               routeSource.Contains("DisplayPreferences", StringComparison.Ordinal) &&
               routeSource.Contains("DataAndUpdates", StringComparison.Ordinal) &&
               navigationSource.Contains("private void NavigateTo(AppRoute route)", StringComparison.Ordinal) &&
               aboutSource.Contains("ShowSection(AboutSection section)", StringComparison.Ordinal),
            "Top-level destinations must use a product-facing route model and explicit page sections.");

        var portableRoot = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-navigation-test-{Guid.NewGuid():N}");
        var themes = Path.Combine(portableRoot, "themes");
        var data = Path.Combine(portableRoot, "data");
        Directory.CreateDirectory(themes);
        Directory.CreateDirectory(data);
        Exception? failure = null;

        using (var preferences = new UiPreferencesStore(data))
        {
            await preferences.SaveAsync(new UiPreferences { OnboardingCompleted = true });
        }

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                MainWindow? window = null;
                try
                {
                    window = new MainWindow(new PortableLayout(portableRoot, themes, data));
                    typeof(MainWindow).GetMethod(
                        "EnsureMainUiInitialized",
                        BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(window, null);

                    window.SettingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(window.PersonalizationInfoPanel.Visibility == Visibility.Visible &&
                           window.PersonalizationPageTitleText.Text == "图像工作台" &&
                           window.SettingsInfoPanel.Visibility == Visibility.Visible &&
                           window.DisplayPreferencesInfoPanel.Visibility == Visibility.Collapsed,
                        "Image adjustments must open in their own workspace route.");

                    window.DisplayPreferencesButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(window.PersonalizationInfoPanel.Visibility == Visibility.Visible &&
                           window.PersonalizationPageTitleText.Text == "显示偏好" &&
                           window.SettingsInfoPanel.Visibility == Visibility.Collapsed &&
                           window.DisplayPreferencesInfoPanel.Visibility == Visibility.Visible &&
                           Equals(window.DisplayPreferencesButton.Tag, "active"),
                        "Display preferences must open on a separate active route.");

                    window.DataButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(window.AboutPage.Visibility == Visibility.Visible &&
                           window.AboutPage.IdentityCard.Visibility == Visibility.Collapsed &&
                           window.AboutPage.DataManagementCard.Visibility == Visibility.Visible &&
                           Equals(window.DataButton.Tag, "active"),
                        "Update and local data management must have a dedicated sidebar destination.");

                    window.AboutButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Ensure(window.AboutPage.IdentityCard.Visibility == Visibility.Visible &&
                           window.AboutPage.DataManagementCard.Visibility == Visibility.Collapsed &&
                           Equals(window.AboutButton.Tag, "active"),
                        "Product information must remain a focused About destination.");
                }
                catch (TargetInvocationException exception)
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
                        await window.DisposeAsync();
                    }
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
                throw new InvalidOperationException(
                    "The application route separation workflow failed.",
                    failure);
            }
        }
        finally
        {
            if (Directory.Exists(portableRoot))
            {
                Directory.Delete(portableRoot, recursive: true);
            }
        }
    }
}
