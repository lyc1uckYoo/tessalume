namespace Tessalume.App.Creator;

internal sealed class ThemeProjectWatcherFactory : IThemeProjectWatcherFactory
{
    public IThemeProjectWatcher Create(
        string projectDirectory,
        IEnumerable<string>? watchedFiles = null) =>
        new ThemeProjectWatcher(projectDirectory, watchedFiles);
}
