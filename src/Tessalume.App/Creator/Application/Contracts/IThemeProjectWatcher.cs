namespace Tessalume.App.Creator;

internal interface IThemeProjectWatcher : IDisposable
{
    event EventHandler<ThemeProjectChangeBatch>? Changed;

    event EventHandler<string>? Faulted;

    void Start();

    void UpdateWatchedFiles(IEnumerable<string> watchedFiles);
}

internal interface IThemeProjectWatcherFactory
{
    IThemeProjectWatcher Create(
        string projectDirectory,
        IEnumerable<string>? watchedFiles = null);
}
