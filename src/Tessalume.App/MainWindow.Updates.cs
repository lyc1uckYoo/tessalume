using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;
using Tessalume.Core.Updates;
using Microsoft.Win32;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void AutomaticUpdatesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingAutomaticUpdateSetting) return;
        _automaticUpdateChecks = AutomaticUpdatesCheckBox.IsChecked == true;
        if (_automaticUpdateChecks)
        {
            _lastUpdateCheckAt = null;
            _updateStatusMessage = "自动检查已开启，将从 GitHub Releases 获取最新版本";
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

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(automatic: false);

    internal void SetStartupUpdateResult(PortableUpdateResult? result) =>
        _startupUpdateResult = result;

    private void ShowStartupUpdateResult()
    {
        if (_startupUpdateResult is not { } result) return;
        _startupUpdateResult = null;
        LocalLog.Write(result.Success
            ? $"Update completed: {result.VersionLabel}."
            : $"Update failed: {result.Message}");
        ShowProductMessage(
            result.Success ? "Tessalume 已完成更新" : "自动更新未完成",
            result.Success
                ? $"当前已运行 {result.VersionLabel}。主题、收藏和个性化参数均已保留。"
                : $"当前版本仍可继续使用。\n\n原因：{result.Message}",
            result.Success ? ProductDialogKind.Information : ProductDialogKind.Warning);
        UpdateBootstrapper.DismissResult(_layout);
        _ = UpdateBootstrapper.CleanupArtifactsAsync(_layout, result);
    }

    private void ScheduleAutomaticUpdateCheck(bool forceSoon = false)
    {
        if (!_automaticUpdateChecks || _updateCheckInProgress) return;
        if (!forceSoon && _lastUpdateCheckAt is { } checkedAt &&
            DateTimeOffset.Now - checkedAt < TimeSpan.FromHours(12))
        {
            return;
        }

        _ = RunScheduledUpdateCheckAsync(forceSoon ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5));
    }

    private async Task RunScheduledUpdateCheckAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _updateCancellation.Token);
            await CheckForUpdatesAsync(automatic: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckForUpdatesAsync(bool automatic)
    {
        if (_updateCheckInProgress) return;
        _updateCheckInProgress = true;
        if (_uiInitialized)
        {
            UpdateProgressBar.Value = 0;
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateProgressText.Visibility = Visibility.Collapsed;
        }
        _updateStatusMessage = automatic ? "正在自动检查更新…" : "正在连接 GitHub Releases…";
        UpdateUpdateControls();
        try
        {
            _lastUpdateCheckAt = DateTimeOffset.Now;
            await SavePreferencesAsync();
            var release = await _updateClient.CheckLatestAsync(_updateCancellation.Token);
            if (release is null)
            {
                _updateStatusMessage = $"当前已是最新版本 · {BrandInfo.VersionLabel}";
                UpdateUpdateControls();
                if (!automatic)
                {
                    ShowProductMessage(
                        "当前已是最新版本",
                        $"你正在使用 {BrandInfo.VersionLabel}，暂时没有可安装的新版本。",
                        ProductDialogKind.Information);
                }
                return;
            }

            _updateStatusMessage = $"发现新版本 {release.VersionLabel} · 等待确认";
            UpdateUpdateControls();
            var notes = FormatReleaseNotes(release.ReleaseNotes);
            var confirmed = ShowProductConfirmation(
                $"发现 Tessalume {release.VersionLabel}",
                $"当前版本：{BrandInfo.VersionLabel}\n新版本：{release.VersionLabel}\n下载大小：{FormatBytes(release.DownloadSize)}\n\n{notes}\n\n下载完成后会校验 SHA-256，随后退出、替换程序并自动重新打开。你的主题和 data 数据不会被删除。",
                "下载并安装");
            if (!confirmed)
            {
                _updateStatusMessage = $"{release.VersionLabel} 可用 · 已选择稍后更新";
                UpdateUpdateControls();
                return;
            }

            await DownloadAndInstallUpdateAsync(release);
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LocalLog.Write("Update check or installation failed.", exception);
            var errorMessage = DescribeUpdateError(exception);
            _updateStatusMessage = $"更新失败 · {errorMessage}";
            UpdateUpdateControls();
            if (!automatic || IsVisible)
            {
                ShowProductMessage(
                    "无法完成软件更新",
                    $"当前版本没有被修改，可以继续使用。\n\n{errorMessage}",
                    ProductDialogKind.Error);
            }
        }
        finally
        {
            _updateCheckInProgress = false;
            UpdateUpdateControls();
        }
    }

    private async Task DownloadAndInstallUpdateAsync(ReleaseUpdate release)
    {
        ShowMainInterface();
        ShowInfoPage(RightPane.Settings);
        _updateStatusMessage = $"正在下载 {release.VersionLabel}…";
        if (_uiInitialized)
        {
            UpdateProgressBar.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = 0;
            UpdateProgressText.Visibility = Visibility.Visible;
            UpdateProgressText.Text = $"0% · 0 B / {FormatBytes(release.DownloadSize)}";
        }

        var progress = new Progress<UpdateDownloadProgress>(value =>
        {
            var total = Math.Max(1, value.TotalBytes);
            var percentage = Math.Clamp(value.BytesReceived * 100d / total, 0, 100);
            if (_uiInitialized)
            {
                UpdateProgressBar.Value = percentage;
                UpdateProgressText.Text = $"{percentage:0}% · {FormatBytes(value.BytesReceived)} / {FormatBytes(total)}";
            }
        });
        var downloaded = await _updateClient.DownloadAsync(
            release,
            progress,
            _updateCancellation.Token);
        _updateStatusMessage = "下载与 SHA-256 校验完成，正在准备安全替换…";
        UpdateUpdateControls();
        var helperProcessId = UpdateBootstrapper.StartHelper(_layout, downloaded, release);
        LocalLog.Write($"Update helper {helperProcessId} started for {release.VersionLabel}.");
        _shutdownRequested = true;
        Application.Current.Shutdown();
    }

    private void UpdateUpdateControls()
    {
        if (!_uiInitialized || AutomaticUpdatesCheckBox is null) return;
        _updatingAutomaticUpdateSetting = true;
        AutomaticUpdatesCheckBox.IsChecked = _automaticUpdateChecks;
        _updatingAutomaticUpdateSetting = false;
        CheckForUpdatesButton.IsEnabled = !_updateCheckInProgress;
        UpdateStatusText.Text = _updateStatusMessage;
        if (!_updateCheckInProgress && UpdateProgressBar.Value <= 0)
        {
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateProgressText.Visibility = Visibility.Collapsed;
        }
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
