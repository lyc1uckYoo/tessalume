using System.Windows;
using Tessalume.App.Features.About;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Updates;

namespace Tessalume.App;

public partial class MainWindow
{
    private async Task ConfirmAndInstallUpdateAsync(ReleaseUpdate release)
    {
        var notes = FormatReleaseNotes(release.ReleaseNotes);
        var transferDescription = release.Delta is { } delta
            ? $"增量更新：{FormatBytes(delta.DownloadSize)}（完整包 {FormatBytes(release.DownloadSize)}，基线不匹配时自动切换）"
            : $"完整更新：{FormatBytes(release.DownloadSize)}";
        var confirmed = ShowProductConfirmation(
            $"发现 Tessalume {release.VersionLabel}",
            $"当前版本：{BrandInfo.VersionLabel}\n新版本：{release.VersionLabel}\n{transferDescription}\n\n{notes}\n\n下载完成后会校验 SHA-256，随后退出、替换程序并自动重新打开。你的主题和 data 数据不会被删除。",
            "下载并安装");
        if (!confirmed)
        {
            _updateStatusMessage = $"{release.VersionLabel} 可用 · 已选择稍后更新";
            UpdateUpdateControls();
            return;
        }

        await DownloadAndInstallUpdateAsync(release);
    }

    private async Task DownloadAndInstallUpdateAsync(ReleaseUpdate release)
    {
        ShowMainInterface();
        AboutPage.ShowSection(AboutSection.DataAndUpdates);
        NavigateTo(Features.Navigation.AppRoute.DataAndUpdates);
        _updateStatusMessage = $"正在下载 {release.VersionLabel}…";
        var initialDownloadSize = release.PreferredDownloadSize;
        if (_uiInitialized)
        {
            AboutPage.SetUpdateProgress(
                0,
                $"0% · 0 B / {FormatBytes(initialDownloadSize)}");
        }

        var progress = new Progress<UpdateDownloadProgress>(value =>
        {
            if (value.Phase == UpdateDownloadPhase.ApplyingDelta)
            {
                _updateStatusMessage = "增量包已校验，正在重建完整 EXE…";
                if (_uiInitialized)
                {
                    AboutPage.SetUpdateProgress(100, "增量下载完成 · 正在执行本地合成与目标校验");
                }
                UpdateUpdateControls();
                return;
            }
            if (value.Phase == UpdateDownloadPhase.FallingBackToFull)
            {
                _updateStatusMessage = "当前文件不适用增量包，正在自动切换完整更新…";
                if (_uiInitialized)
                {
                    AboutPage.SetUpdateProgress(
                        0,
                        $"已安全切换完整包 · 0 B / {FormatBytes(release.DownloadSize)}");
                }
                UpdateUpdateControls();
                return;
            }
            var total = Math.Max(1, value.TotalBytes);
            var percentage = Math.Clamp(value.BytesReceived * 100d / total, 0, 100);
            var phaseLabel = value.Phase == UpdateDownloadPhase.DownloadingDelta
                ? "增量包"
                : "完整包";
            if (_uiInitialized)
            {
                AboutPage.SetUpdateProgress(
                    percentage,
                    $"{phaseLabel} {percentage:0}% · {FormatBytes(value.BytesReceived)} / " +
                    FormatBytes(total));
            }
        });
        var downloaded = await _aboutUpdateService.DownloadApplicationAsync(
            release,
            progress,
            _updateCancellation.Token);
        _updateStatusMessage = "下载与 SHA-256 校验完成，正在准备安全替换…";
        UpdateUpdateControls();
        var helperProcessId = await UpdateBootstrapper.StartHelperAsync(
            _layout,
            downloaded,
            release,
            _updateCancellation.Token);
        LocalLog.Write($"Update helper {helperProcessId} started for {release.VersionLabel}.");
        _shutdownRequested = true;
        Application.Current.Shutdown();
    }
}
