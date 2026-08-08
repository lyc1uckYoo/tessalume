using System.IO;

namespace Tessalume.App.Creator;

internal sealed partial class CreatorCenterViewModel
{
    private void StopProjectWatcher(bool keepStatus = false)
    {
        if (_projectWatcher is not null)
        {
            _projectWatcher.Changed -= ProjectWatcher_Changed;
            _projectWatcher.Faulted -= ProjectWatcher_Faulted;
            _projectWatcher.Dispose();
            _projectWatcher = null;
        }
        IsWatching = false;
        if (keepStatus) return;
        WatcherStatusTone = "idle";
        WatcherStatusText = SelectedProject is null ? "选择项目后开始监听" : "文件监听已停止";
        WatcherActivityText = "Tessalume 会在文件写入稳定后自动体检";
    }

    private CancellationTokenSource BeginDevelopmentOperation(CancellationToken cancellationToken)
    {
        CancelDevelopmentOperation();
        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _developmentCancellation = operation;
        return operation;
    }

    private void CancelDevelopmentOperation()
    {
        _developmentCancellation?.Cancel();
        _developmentCancellation = null;
        IsDevelopmentBusy = false;
    }

    private bool CompleteDevelopmentOperation(CancellationTokenSource operation)
    {
        var isCurrent = ReferenceEquals(_developmentCancellation, operation);
        if (isCurrent) _developmentCancellation = null;
        operation.Dispose();
        return isCurrent;
    }

    private void CancelCurrentScan()
    {
        if (_scanCancellation is null) return;
        _scanCancellation.Cancel();
        _scanCancellation.Dispose();
        _scanCancellation = null;
    }

    private void NotifyDevelopmentCommandsChanged()
    {
        OnPropertyChanged(nameof(CanRevalidateSelectedProject));
        OnPropertyChanged(nameof(CanApplySelectedProject));
        OnPropertyChanged(nameof(CanToggleCodexMode));
        OnPropertyChanged(nameof(CanRunAcceptance));
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopProjectWatcher();
        CancelDevelopmentOperation();
        CancelCurrentScan();
    }
}
