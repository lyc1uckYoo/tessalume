using System.IO;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;
using Tessalume.App.Infrastructure;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void ArtworkWorkbench_ChooseImageRequested(
        object? sender,
        ArtworkChooseImageEventArgs e)
    {
        var sourcePath = ArtworkWorkbenchFileDialogs.ChooseImage(
            this,
            GetArtworkRegionDisplayName(e.Region),
            GetArtworkModeDisplayName(e.Mode));
        if (sourcePath is null) return;

        ArtworkWorkbench.SetApplyState(ArtworkApplyState.Loading, "正在校验并导入本地图片");
        string storedPath;
        try
        {
            storedPath = await _personalImageStore.ImportAsync(
                sourcePath,
                _personalizationCancellation.Token);
        }
        catch (OperationCanceledException) when (_personalizationCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException)
        {
            LocalLog.Write("Importing a personal artwork image failed.", exception);
            if (string.Equals(
                    e.ThemeId,
                    _artworkContextThemeId,
                    StringComparison.OrdinalIgnoreCase))
            {
                ArtworkWorkbench.SetApplyState(ArtworkApplyState.ImportFailed, exception.Message);
            }
            ShowProductMessage("无法使用这张图片", exception.Message, ProductDialogKind.Error);
            return;
        }

        var currentTarget = ArtworkSettingsAccessor.GetAdjustment(
            GetVisualSettings(e.ThemeId),
            e.Mode,
            e.Region);
        if (string.Equals(
                currentTarget.CustomImagePath,
                storedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    e.ThemeId,
                    _artworkContextThemeId,
                    StringComparison.OrdinalIgnoreCase))
            {
                var isApplied = string.Equals(
                    e.ThemeId,
                    _activeThemeId,
                    StringComparison.OrdinalIgnoreCase);
                if (isApplied)
                {
                    ArtworkWorkbench.SetApplyState(
                        ArtworkApplyState.Pending,
                        "图片未变化，正在确认当前同步状态");
                    ScheduleVisualSettingsUpdate();
                }
                else
                {
                    ArtworkWorkbench.SetApplyState(
                        _artworkCodexConnectionVerified
                            ? ArtworkApplyState.Connected
                            : ArtworkApplyState.Disconnected,
                        "当前图片已保存，应用主题后生效");
                }
            }
            ShowToast($"{GetArtworkRegionDisplayName(e.Region)}已在使用这张本地图片");
            return;
        }

        // The target is captured by the request. An asynchronous import must
        // never follow a later theme/region/mode selection made by the user.
        if (ArtworkWorkbench.TrySetCustomImagePath(e.ThemeId, e.Mode, e.Region, storedPath))
        {
            ShowToast($"已为{GetArtworkRegionDisplayName(e.Region)}使用本地图片");
            return;
        }

        var existing = GetVisualSettings(e.ThemeId);
        SetResolvedVisualSettings(
            e.ThemeId,
            ArtworkSettingsReducer.UpdateAdjustment(
                existing,
                e.Mode,
                e.Region,
                adjustment => adjustment with { CustomImagePath = storedPath }));
        var preferencesRevision = MarkPreferencesDirty();
        try
        {
            await SavePreferencesAsync();
            MarkPreferencesPersisted(preferencesRevision);
            ShowToast(
                $"图片已保存到原目标 · {GetArtworkRegionDisplayName(e.Region)} · " +
                GetArtworkModeDisplayName(e.Mode));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LocalLog.Write("Saving an imported personal artwork image failed.", exception);
            if (string.Equals(
                    e.ThemeId,
                    _artworkContextThemeId,
                    StringComparison.OrdinalIgnoreCase))
            {
                ArtworkWorkbench.SetApplyState(
                    ArtworkApplyState.SaveFailed,
                    "图片已导入，但目标设置未写入磁盘");
            }
            ShowProductMessage("图片设置未保存", exception.Message, ProductDialogKind.Error);
        }
    }

    private static string GetArtworkRegionDisplayName(ArtworkRegion region) => region switch
    {
        ArtworkRegion.Sidebar => "左栏图片",
        ArtworkRegion.Chat => "聊天背景",
        _ => "首页横幅",
    };

    private static string GetArtworkModeDisplayName(ArtworkColorMode mode) =>
        mode == ArtworkColorMode.Dark ? "暗色" : "亮色";
}
