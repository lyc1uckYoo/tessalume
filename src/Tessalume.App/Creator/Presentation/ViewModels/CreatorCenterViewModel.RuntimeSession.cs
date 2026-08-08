using System.IO;

namespace Tessalume.App.Creator;

internal sealed partial class CreatorCenterViewModel
{
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
            status = await _runtimeGateway.ReadStatusAsync(cancellationToken);
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
            var status = await _runtimeGateway.ToggleColorSchemeAsync(operation.Token);
            ApplyRuntimeStatus(status);
            return status;
        }
        finally
        {
            if (CompleteDevelopmentOperation(operation)) IsDevelopmentBusy = false;
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
            var result = await _runtimeGateway.ApplyProjectAsync(
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
            if (ownsOperation && CompleteDevelopmentOperation(operation!)) IsDevelopmentBusy = false;
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
}
