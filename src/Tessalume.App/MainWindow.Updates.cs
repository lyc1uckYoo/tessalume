using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using Tessalume.App.Features.About;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Updates;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void AboutPage_AutomaticUpdateSettingChanged(
        object? sender,
        AboutBooleanSettingChangedEventArgs e)
    {
        _automaticUpdateChecks = e.Enabled;
        if (_automaticUpdateChecks)
        {
            _lastUpdateCheckAt = null;
            _updateStatusMessage = "自动检查已开启，将从 GitHub 获取软件与页面兼容规则更新";
        }
        else
        {
            _updateStatusMessage = "自动检查已关闭，仍可随时手动检查";
        }

        UpdateUpdateControls();
        await SavePreferencesAsync();
        ShowToast(_automaticUpdateChecks ? "已开启自动更新检查" : "已关闭自动更新检查");
        if (_automaticUpdateChecks)
        {
            ScheduleAutomaticUpdateCheck(forceSoon: true);
        }
    }

    private async void AboutPage_CheckForUpdatesRequested(object? sender, EventArgs e) =>
        await CheckForUpdatesAsync(automatic: false);

    private async void UpdateAvailableBadge_Click(object sender, RoutedEventArgs e)
    {
        if (_updateCheckInProgress || _availableUpdate is not { } release) return;

        _updateCheckInProgress = true;
        UpdateUpdateControls();
        try
        {
            await ConfirmAndInstallUpdateAsync(release);
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleUpdateFailure(exception, showDialog: true);
        }
        finally
        {
            _updateCheckInProgress = false;
            UpdateUpdateControls();
        }
    }

    private void ScheduleAutomaticUpdateCheck(bool forceSoon = false)
    {
        if (!_automaticUpdateChecks || _updateCheckInProgress || _automaticUpdateCheckScheduled) return;
        _automaticUpdateCheckScheduled = true;
        _ = RunScheduledUpdateCheckAsync(forceSoon ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2));
    }

    private async Task RunScheduledUpdateCheckAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _updateCancellation.Token);
            if (_automaticUpdateChecks)
            {
                await CheckForUpdatesAsync(automatic: true);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _automaticUpdateCheckScheduled = false;
        }
    }

    private async Task CheckForUpdatesAsync(bool automatic)
    {
        if (_updateCheckInProgress) return;
        _updateCheckInProgress = true;
        if (_uiInitialized)
        {
            AboutPage.SetUpdateProgress(0, string.Empty, visible: false);
        }
        _updateStatusMessage = automatic ? "正在自动检查更新…" : "正在连接 GitHub Releases…";
        UpdateUpdateControls();
        try
        {
            _lastUpdateCheckAt = DateTimeOffset.Now;
            await SavePreferencesAsync();
            var result = await _aboutUpdateService.CheckAsync(_updateCancellation.Token);
            var compatibilityInstall = result.CompatibilityUpdate;
            var compatibilityUpdateError = result.CompatibilityError;
            if (compatibilityUpdateError is not null)
            {
                LocalLog.Write("Compatibility update check failed.", compatibilityUpdateError);
            }
            if (compatibilityInstall?.Changed == true)
            {
                LocalLog.Write(
                    $"Compatibility rules updated from " +
                    $"{compatibilityInstall.PreviousPack.PackVersionLabel} to " +
                    $"{compatibilityInstall.ActivePack.PackVersionLabel}.");
                if (_uiInitialized && IsVisible)
                {
                    ShowToast(
                        $"页面兼容规则已更新到 " +
                        compatibilityInstall.ActivePack.PackVersionLabel);
                }
            }

            var release = result.ApplicationUpdate;
            if (release is null)
            {
                _availableUpdate = null;
                _updateStatusMessage = compatibilityInstall?.Changed == true
                    ? $"兼容规则已更新到 {compatibilityInstall.ActivePack.PackVersionLabel} · 软件 {BrandInfo.VersionLabel}"
                    : compatibilityUpdateError is not null
                        ? $"软件已是最新版本 · 兼容规则检查未完成"
                        : $"当前已是最新版本 · {BrandInfo.VersionLabel}";
                UpdateUpdateControls();
                if (!automatic)
                {
                    if (compatibilityInstall?.Changed == true)
                    {
                        ShowProductMessage(
                            "页面兼容规则已更新",
                            $"软件仍为 {BrandInfo.VersionLabel}，页面兼容规则已更新到 {compatibilityInstall.ActivePack.PackVersionLabel}。下次应用主题时会自动完成预检，不需要重新下载完整软件。",
                            ProductDialogKind.Information);
                    }
                    else if (compatibilityUpdateError is not null)
                    {
                        ShowProductMessage(
                            "软件已是最新版本",
                            $"你正在使用 {BrandInfo.VersionLabel}。软件版本检查已完成，但页面兼容规则检查暂未完成：\n\n{DescribeUpdateError(compatibilityUpdateError)}",
                            ProductDialogKind.Warning);
                    }
                    else
                    {
                        ShowProductMessage(
                            "当前已是最新版本",
                            $"你正在使用 {BrandInfo.VersionLabel}，软件和页面兼容规则均为最新。",
                            ProductDialogKind.Information);
                    }
                }
                return;
            }

            _availableUpdate = release;
            _updateStatusMessage = $"发现新版本 {release.VersionLabel} · 等待确认";
            UpdateUpdateControls();
            if (automatic)
            {
                if (_uiInitialized && IsVisible)
                {
                    ShowToast($"{release.VersionLabel} 已发布，点击左上角红色更新提示安装");
                }
                return;
            }

            await ConfirmAndInstallUpdateAsync(release);
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleUpdateFailure(exception, showDialog: !automatic);
        }
        finally
        {
            _updateCheckInProgress = false;
            UpdateUpdateControls();
        }
    }

    private void HandleUpdateFailure(Exception exception, bool showDialog)
    {
        LocalLog.Write("Update check or installation failed.", exception);
        var errorMessage = DescribeUpdateError(exception);
        _updateStatusMessage = $"更新失败 · {errorMessage}";
        UpdateUpdateControls();
        if (showDialog)
        {
            ShowProductMessage(
                "无法完成软件更新",
                $"当前版本没有被修改，可以继续使用。\n\n{errorMessage}",
                ProductDialogKind.Error);
        }
    }

    private void UpdateUpdateControls()
    {
        if (!_uiInitialized || AboutPage is null) return;
        AboutPage.RenderUpdateState(BuildAboutUpdateState());
        UpdateAvailableBadge.Visibility = _availableUpdate is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateAvailableBadge.IsEnabled = !_updateCheckInProgress;
        if (_availableUpdate is { } release)
        {
            UpdateAvailableBadge.ToolTip = $"发现 {release.VersionLabel}，点击查看并安装";
            AutomationProperties.SetName(UpdateAvailableBadge, $"发现 {release.VersionLabel}，点击更新");
        }
    }

    private AboutUpdateState BuildAboutUpdateState(
        string? rollbackStatus = null,
        bool? rollbackAvailable = null)
    {
        var available = rollbackAvailable ?? _availableRollback is not null;
        var status = rollbackStatus ?? (_availableRollback is { } rollback
            ? $"可恢复 {rollback.PreviousVersionLabel} · 更新前 EXE 已通过 SHA-256 校验"
            : "当前没有可用恢复点；成功更新后会自动保留上一版本");
        var toolTip = _availableRollback is { } rollbackInfo
            ? $"从 {rollbackInfo.CurrentVersionLabel} 恢复到 {rollbackInfo.PreviousVersionLabel}"
            : "没有可恢复的上一版本";
        return new AboutUpdateState(
            _automaticUpdateChecks,
            _updateCheckInProgress,
            _updateStatusMessage,
            new AboutRollbackState(
                status,
                toolTip,
                available,
                _rollbackInProgress));
    }

    private static string FormatReleaseNotes(string notes)
    {
        var lines = notes
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('#', ' ', '-', '*'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(6)
            .ToArray();
        var summary = lines.Length == 0 ? "此版本包含稳定性与体验改进。" : string.Join("\n", lines);
        return summary.Length <= 700 ? summary : summary[..700] + "…";
    }

    private static string DescribeUpdateError(Exception exception) => exception switch
    {
        HttpRequestException => "无法连接 GitHub 更新服务，请检查网络后重试。",
        TaskCanceledException => "连接 GitHub 更新服务超时，请稍后重试。",
        UnauthorizedAccessException => "无法写入更新文件，请确认 Tessalume 所在文件夹可写。",
        IOException when string.IsNullOrWhiteSpace(exception.Message) => "更新文件处理失败，请稍后重试。",
        _ => exception.Message,
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        var scaled = (double)value;
        while (scaled >= 1024 && unit < units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }
        return $"{scaled:0.#} {units[unit]}";
    }
}
