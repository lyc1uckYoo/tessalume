using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Backup;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void BackupUserData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "备份 Tessalume 用户数据",
            Filter = "Tessalume 备份 (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"Tessalume-backup-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.zip",
        };
        if (dialog.ShowDialog(this) != true) return;

        SetDataOperationBusy(true, "正在校验并备份用户数据…");
        try
        {
            await SavePreferencesAsync();
            var result = await _backupService.CreateAsync(
                dialog.FileName,
                new PortableBackupOptions
                {
                    IncludeImportedThemes = IncludeImportedThemesCheckBox.IsChecked == true,
                },
                _backupCancellation.Token);
            DataBackupStatusText.Text =
                $"最近备份：{result.Summary.DataFileCount} 个数据文件 · " +
                $"{result.Summary.ImportedThemes.Count} 个用户主题 · {FormatBackupBytes(result.CompressedBytes)}";
            ShowProductMessage(
                "用户数据已安全备份",
                $"设置与运行状态：{result.Summary.DataFileCount} 个文件\n" +
                $"用户导入主题：{result.Summary.ImportedThemes.Count} 个\n" +
                $"备份大小：{FormatBackupBytes(result.CompressedBytes)}\n" +
                $"SHA-256：{result.Sha256}\n\n{result.ArchivePath}",
                ProductDialogKind.Information);
        }
        catch (OperationCanceledException) when (_backupCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Creating a user-data backup failed.", exception);
            DataBackupStatusText.Text = "上次备份未完成，原目标文件未被破坏";
            ShowProductMessage("无法备份用户数据", exception.Message, ProductDialogKind.Error);
        }
        finally
        {
            SetDataOperationBusy(false, null);
        }
    }

    private async void RestoreUserData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Tessalume 用户数据备份",
            Filter = "Tessalume 备份 (*.zip)|*.zip",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        SetDataOperationBusy(true, "正在验证备份内容…");
        try
        {
            var summary = await PortableBackupService.InspectAsync(
                dialog.FileName,
                _backupCancellation.Token);
            var themes = summary.ImportedThemes.Count == 0
                ? "不包含用户主题"
                : summary.ImportedThemes.Count <= 4
                    ? string.Join("、", summary.ImportedThemes.Select(theme => theme.DisplayName))
                    : string.Join("、", summary.ImportedThemes.Take(4).Select(theme => theme.DisplayName)) +
                      $" 等 {summary.ImportedThemes.Count} 个";
            var confirmed = ShowProductConfirmation(
                "恢复这份用户数据？",
                $"备份时间：{summary.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
                $"设置与运行状态：{summary.DataFileCount} 个文件\n" +
                $"用户主题：{themes}\n" +
                $"解压后大小：{FormatBackupBytes(summary.TotalBytes)}\n\n" +
                "恢复前会先自动备份当前状态。恢复只添加或替换备份中声明的文件，不会删除其他用户主题。完成后 Tessalume 将关闭，请重新打开软件。",
                "确认恢复并关闭",
                dangerous: true);
            if (!confirmed)
            {
                DataBackupStatusText.Text = "已取消恢复，当前数据没有变化";
                return;
            }

            await SavePreferencesAsync();
            var result = await _backupService.RestoreAsync(dialog.FileName, _backupCancellation.Token);
            _userDataRestoreCompleted = true;
            DataBackupStatusText.Text = "恢复完成，等待重新启动 Tessalume";
            ShowProductMessage(
                "用户数据恢复完成",
                $"已恢复 {result.Summary.DataFileCount} 个数据文件和 " +
                $"{result.Summary.ImportedThemes.Count} 个用户主题。\n\n" +
                $"恢复前的自动快照保存在：\n{result.AutomaticSnapshotPath}\n\n" +
                "Tessalume 现在将关闭。重新打开后，恢复的收藏、图像参数和主题状态会完整生效。",
                ProductDialogKind.Information);
            Close();
        }
        catch (OperationCanceledException) when (_backupCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Restoring a user-data backup failed.", exception);
            DataBackupStatusText.Text = "恢复失败，当前数据已保持或自动回滚";
            ShowProductMessage(
                "无法恢复用户数据",
                $"当前数据没有被替换，或已经自动回滚。\n\n{exception.Message}",
                ProductDialogKind.Error);
        }
        finally
        {
            if (!_userDataRestoreCompleted)
            {
                SetDataOperationBusy(false, null);
            }
        }
    }

    private void SetDataOperationBusy(bool busy, string? status)
    {
        BackupUserDataButton.IsEnabled = !busy;
        RestoreUserDataButton.IsEnabled = !busy;
        IncludeImportedThemesCheckBox.IsEnabled = !busy;
        if (status is not null)
        {
            DataBackupStatusText.Text = status;
        }
    }

    private static string FormatBackupBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GiB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MiB",
        >= 1024L => $"{bytes / 1024d:0.0} KiB",
        _ => $"{bytes} B",
    };
}
