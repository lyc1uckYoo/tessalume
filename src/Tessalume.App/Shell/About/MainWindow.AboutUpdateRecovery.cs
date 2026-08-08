using System.Diagnostics;
using System.IO;
using System.Windows;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Updates;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void AboutPage_RollbackRequested(object? sender, EventArgs e)
    {
        if (_updateCheckInProgress || _rollbackInProgress) return;
        _availableRollback ??= await _aboutUpdateService.LoadRollbackAsync(
            _updateCancellation.Token);
        if (_availableRollback is not { } rollback)
        {
            UpdateRollbackControls();
            ShowProductMessage(
                "没有可用的上一版本",
                "本机没有找到完整且校验通过的更新恢复点。当前软件和用户数据均未被修改。",
                ProductDialogKind.Information);
            return;
        }

        var confirmed = ShowProductConfirmation(
            $"恢复 {rollback.PreviousVersionLabel}",
            $"当前版本：{rollback.CurrentVersionLabel}\n" +
            $"恢复版本：{rollback.PreviousVersionLabel}\n\n" +
            "软件将关闭并恢复更新前保留的 EXE，随后自动重新打开。" +
            "data、主题、收藏和个性化参数不会被删除。恢复成功后该恢复点会被移除。",
            "恢复并重新启动");
        if (!confirmed) return;

        _rollbackInProgress = true;
        UpdateUpdateControls();
        try
        {
            var helperProcessId = await UpdateBootstrapper.StartRollbackHelperAsync(
                _layout,
                rollback,
                _updateCancellation.Token);
            LocalLog.Write(
                $"Rollback helper {helperProcessId} started for {rollback.PreviousVersionLabel}.");
            _shutdownRequested = true;
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            _rollbackInProgress = false;
            LocalLog.Write("Starting the version rollback helper failed.", exception);
            UpdateUpdateControls();
            ShowProductMessage(
                "无法恢复上一版本",
                $"当前版本没有被修改，可以继续使用。\n\n{exception.Message}",
                ProductDialogKind.Error);
        }
    }

    private async Task RefreshRollbackAvailabilityAsync()
    {
        if (!_uiInitialized || AboutPage is null) return;
        AboutPage.RenderUpdateState(BuildAboutUpdateState(
            "正在校验上一版本恢复点…",
            rollbackAvailable: false));
        try
        {
            _availableRollback = await _aboutUpdateService.LoadRollbackAsync(
                _updateCancellation.Token);
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
        {
            return;
        }
        UpdateRollbackControls();
    }

    private void UpdateRollbackControls()
    {
        if (!_uiInitialized || AboutPage is null) return;
        AboutPage.RenderUpdateState(BuildAboutUpdateState());
    }

    internal void SetStartupUpdateResult(PortableUpdateResult? result) =>
        _startupUpdateResult = result;

    internal void SetStartupHealthToken(string? token) =>
        _startupHealthToken = token;

    private async Task ShowStartupUpdateResultAsync()
    {
        if (_startupUpdateResult is not { } result) return;
        _startupUpdateResult = null;
        if (result is
            {
                Success: true,
                Operation: PortableUpdateOperation.Install,
                BackupPath: { } backup,
            } && File.Exists(backup))
        {
            try
            {
                var previousVersionLabel = string.IsNullOrWhiteSpace(result.PreviousVersionLabel)
                    ? ReadExecutableVersionLabel(backup)
                    : result.PreviousVersionLabel;
                _availableRollback = await _aboutUpdateService.SaveRollbackAsync(
                    result.VersionLabel,
                    previousVersionLabel,
                    backup,
                    result.DataSnapshotId,
                    _updateCancellation.Token);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                LocalLog.Write("Preserving the previous application version failed.", exception);
            }
        }
        LocalLog.Write(result.Success
            ? $"Update completed: {result.VersionLabel}."
            : $"Update failed: {result.Message}");
        var title = result switch
        {
            { RolledBack: true } => "新版本启动失败，已自动恢复",
            { Success: true, Operation: PortableUpdateOperation.Rollback } => "已恢复上一版本",
            { Success: true } => "Tessalume 已完成更新",
            _ => "自动更新未完成",
        };
        var message = result switch
        {
            { RolledBack: true } =>
                $"{result.Message}\n\n用户数据、主题和个性化参数均未被删除。",
            { Success: true, Operation: PortableUpdateOperation.Rollback } =>
                $"当前已运行 {result.VersionLabel}。用户数据、主题、收藏和个性化参数均已保留。",
            { Success: true } =>
                $"当前已运行 {result.VersionLabel}。启动健康检查已经通过，" +
                "更新前版本会保留为本机恢复点。主题、收藏和个性化参数均已保留。",
            _ => $"当前版本仍可继续使用。\n\n原因：{result.Message}",
        };
        ShowProductMessage(
            title,
            message,
            result.Success ? ProductDialogKind.Information : ProductDialogKind.Warning);
        UpdateBootstrapper.DismissResult(_layout);
        _ = UpdateBootstrapper.CleanupArtifactsAsync(_layout, result);
        _ = RefreshRollbackAvailabilityAsync();
    }

    private static string ReadExecutableVersionLabel(string path)
    {
        try
        {
            var value = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (Version.TryParse(value, out var version))
            {
                return $"v{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or System.ComponentModel.Win32Exception)
        {
        }
        return "v更新前版本";
    }
}
