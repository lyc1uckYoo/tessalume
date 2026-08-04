using System.IO;
using System.Text.Json;

namespace Tessalume.App.Creator;

internal sealed partial class CreatorCenterViewModel
{
    public async Task SelectProjectAsync(ThemeProjectItemViewModel? project)
    {
        ThrowIfDisposed();
        StopProjectWatcher();
        CancelDevelopmentOperation();
        SetSelectedProject(project);
        if (project is null) return;

        StartProjectWatcher(project.DirectoryPath);
        await RefreshCodexStatusAsync();
    }

    public async Task RevalidateSelectedProjectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await RevalidateSelectedProjectCoreAsync(fromWatcher: false, cancellationToken);
    }

    public async Task<CreatorRuntimeActionResult> ApplySelectedProjectAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await ApplySelectedProjectCoreAsync(automatic: false, cancellationToken);
    }

    public async Task RefreshCodexStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CreatorRuntimeStatus status;
        try
        {
            status = await _runtimeBridge.ReadStatusAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            status = CreatorRuntimeStatus.Disconnected(exception.Message);
        }
        ApplyRuntimeStatus(status);
    }

    public async Task<CreatorRuntimeStatus> ToggleCodexModeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var operation = BeginDevelopmentOperation(cancellationToken);
        IsDevelopmentBusy = true;
        try
        {
            var status = await _runtimeBridge.ToggleColorSchemeAsync(operation.Token);
            ApplyRuntimeStatus(status);
            return status;
        }
        finally
        {
            if (CompleteDevelopmentOperation(operation))
            {
                IsDevelopmentBusy = false;
            }
        }
    }

    private void StartProjectWatcher(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory))
        {
            IsWatching = false;
            WatcherStatusTone = "error";
            WatcherStatusText = "项目目录已不存在";
            WatcherActivityText = "请刷新工作区，或在 Codex 中恢复这个项目目录";
            return;
        }

        var watcher = new ThemeProjectWatcher(projectDirectory, SelectedProject?.Snapshot.WatchedFiles);
        watcher.Changed += ProjectWatcher_Changed;
        watcher.Faulted += ProjectWatcher_Faulted;
        watcher.Start();
        _projectWatcher = watcher;
        IsWatching = true;
        WatcherStatusTone = "ready";
        WatcherStatusText = "实时监听中";
        WatcherActivityText = AutoApplyEnabled
            ? "文件稳定后自动体检；通过后自动重新应用"
            : "文件稳定后自动体检；自动应用当前关闭";
    }

    private async void ProjectWatcher_Changed(object? sender, ThemeProjectChangeBatch change)
    {
        try
        {
            await RunOnSynchronizationContextAsync(async () =>
            {
                if (_disposed || !ReferenceEquals(sender, _projectWatcher) ||
                    SelectedProject is not { } selected ||
                    !PathsEqual(selected.DirectoryPath, change.ProjectDirectory))
                {
                    return;
                }

                WatcherStatusTone = change.ProjectExists ? "working" : "error";
                WatcherStatusText = change.ProjectExists ? "检测到文件变化" : "项目目录已被移除";
                WatcherActivityText = change.ProjectExists
                    ? $"{change.ChangedPaths.Count} 个文件已稳定，正在重新体检…"
                    : "监听已停止；项目仍会保留在列表中供你定位问题";
                await RevalidateSelectedProjectCoreAsync(fromWatcher: true, CancellationToken.None);
            });
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            await RunOnSynchronizationContextAsync(() =>
            {
                if (!_disposed)
                {
                    WatcherStatusTone = "error";
                    WatcherStatusText = "自动体检未完成";
                    WatcherActivityText = exception.Message;
                }
                return Task.CompletedTask;
            });
        }
    }

    private void ProjectWatcher_Faulted(object? sender, string message)
    {
        _ = RunOnSynchronizationContextAsync(() =>
        {
            if (!_disposed && ReferenceEquals(sender, _projectWatcher))
            {
                WatcherStatusTone = "warning";
                WatcherStatusText = "正在等待文件稳定";
                WatcherActivityText = message;
            }
            return Task.CompletedTask;
        });
    }

    private async Task RevalidateSelectedProjectCoreAsync(
        bool fromWatcher,
        CancellationToken cancellationToken)
    {
        var selected = SelectedProject
            ?? throw new InvalidOperationException("尚未选择要重新体检的主题项目。");
        var projectDirectory = selected.DirectoryPath;
        var operation = BeginDevelopmentOperation(cancellationToken);
        var operationToken = operation.Token;
        IsDevelopmentBusy = true;
        if (!fromWatcher)
        {
            WatcherStatusTone = "working";
            WatcherStatusText = "正在重新体检";
            WatcherActivityText = "重新读取清单、入口文件、素材和 Template 1.0 契约";
        }

        try
        {
            var snapshot = await _scanner.ScanProjectAsync(projectDirectory, operationToken);
            operationToken.ThrowIfCancellationRequested();
            if (SelectedProject is not { } current ||
                !PathsEqual(current.DirectoryPath, projectDirectory))
            {
                return;
            }

            var replacement = new ThemeProjectItemViewModel(snapshot);
            var index = Projects.IndexOf(current);
            if (index >= 0)
            {
                Projects[index] = replacement;
            }
            SetSelectedProject(replacement);
            _projectWatcher?.UpdateWatchedFiles(snapshot.WatchedFiles);
            UpdateWorkspaceSummary();

            var localTime = DateTimeOffset.Now.ToString(
                "HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture);
            WatcherStatusTone = replacement.StatusTone;
            WatcherStatusText = replacement.StatusTone switch
            {
                "error" => $"体检发现 {replacement.ErrorCount} 项错误",
                "warning" => $"体检通过，另有 {replacement.WarningCount} 项建议",
                _ => "项目体检通过",
            };
            WatcherActivityText = $"最近检查 {localTime} · " +
                (AutoApplyEnabled
                    ? "自动应用已开启"
                    : "自动应用已关闭");

            if (!Directory.Exists(projectDirectory))
            {
                StopProjectWatcher(keepStatus: true);
                return;
            }

            if (fromWatcher && AutoApplyEnabled)
            {
                if (!replacement.CanExport)
                {
                    LastAppliedText = "自动应用已跳过：请先修复体检错误";
                    return;
                }

                var result = await ApplySelectedProjectCoreAsync(automatic: true, operationToken);
                if (!result.Succeeded)
                {
                    LastAppliedText = $"自动应用已跳过：{result.Message}";
                }
            }
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (CompleteDevelopmentOperation(operation))
            {
                IsDevelopmentBusy = false;
            }
        }
    }

    private async Task<CreatorRuntimeActionResult> ApplySelectedProjectCoreAsync(
        bool automatic,
        CancellationToken cancellationToken)
    {
        var project = SelectedProject
            ?? throw new InvalidOperationException("尚未选择要应用的主题项目。");
        if (!project.CanExport)
        {
            throw new InvalidDataException("主题项目仍有阻断错误，修复并重新体检后才能应用。");
        }

        CancellationToken operationToken;
        CancellationTokenSource? operation = null;
        var ownsOperation = !IsDevelopmentBusy;
        if (ownsOperation)
        {
            operation = BeginDevelopmentOperation(cancellationToken);
            operationToken = operation.Token;
            IsDevelopmentBusy = true;
        }
        else
        {
            operationToken = cancellationToken;
        }

        try
        {
            LastAppliedText = automatic ? "体检通过，正在自动重新应用…" : "正在重新应用到 Codex…";
            var result = await _runtimeBridge.ApplyProjectAsync(
                project.DirectoryPath,
                automatic,
                operationToken);
            ApplyRuntimeStatus(result.Status);
            LastAppliedText = result.Succeeded
                ? $"{(automatic ? "自动" : "手动")}应用成功 · {DateTimeOffset.Now:HH:mm:ss}"
                : result.Message;
            return result;
        }
        finally
        {
            if (ownsOperation && CompleteDevelopmentOperation(operation!))
            {
                IsDevelopmentBusy = false;
            }
        }
    }

    private void ApplyRuntimeStatus(CreatorRuntimeStatus status)
    {
        if (!status.IsConnected)
        {
            CodexStatusTone = "idle";
            CodexStatusText = "Codex 未连接";
            CodexModeText = string.IsNullOrWhiteSpace(status.Detail)
                ? "请先应用一次主题以建立本地连接"
                : status.Detail;
            NotifyDevelopmentCommandsChanged();
            return;
        }

        CodexStatusTone = "ready";
        CodexStatusText = status.Port is { } port ? $"Codex 已连接 · {port}" : "Codex 已连接";
        CodexModeText = status.IsDarkMode switch
        {
            true => "当前暗色",
            false => "当前亮色",
            null => "明暗状态未知",
        };
        NotifyDevelopmentCommandsChanged();
    }

    private void SetSelectedProject(ThemeProjectItemViewModel? project)
    {
        SelectedProject = project;
        HealthGroups.Clear();
        if (project is null) return;
        foreach (var group in project.HealthGroups)
        {
            HealthGroups.Add(group);
        }
    }

    private void UpdateWorkspaceSummary()
    {
        if (SelectedWorkspace is null) return;
        var ready = Projects.Count(project => project.CanExport);
        var blocked = Projects.Count(project => project.StatusTone == "error");
        WorkspaceSummary = Projects.Count == 0
            ? SelectedWorkspace.DirectoryPath
            : $"{Projects.Count} 个项目 · {ready} 个可导出 · {blocked} 个需要修复";
    }

    private Task RunOnSynchronizationContextAsync(Func<Task> operation)
    {
        if (_synchronizationContext is null ||
            ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            return operation();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _synchronizationContext.Post(async _ =>
        {
            try
            {
                await operation();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);
        return completion.Task;
    }
}
