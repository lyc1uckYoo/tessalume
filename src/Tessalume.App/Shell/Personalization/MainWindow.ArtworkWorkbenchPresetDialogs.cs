using System.IO;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void ArtworkWorkbench_SavePresetRequested(
        object? sender,
        ArtworkPresetNameEventArgs e)
    {
        var name = ArtworkPresetLibraryService.NormalizeName(e.Name, _artworkPresets.Count);
        var preset = ArtworkWorkbench.CreateCurrentPreset(name).Normalize();
        var result = ArtworkPresetLibraryService.Upsert(_artworkPresets, preset);
        if (result == ArtworkPresetUpsertResult.CapacityReached)
        {
            ShowProductMessage(
                "无法保存个人方案",
                $"最多可以保存 {ArtworkPresetLibraryService.MaximumPresetCount} 个个人图像方案，请先删除不再使用的方案。",
                ProductDialogKind.Warning);
            return;
        }

        var index = ArtworkPresetLibraryService.FindIndex(_artworkPresets, preset.Name);
        ArtworkWorkbench.SelectPreset(index >= 0 ? _artworkPresets[index] : null);
        if (await TrySaveArtworkPresetLibraryAsync("保存个人图像方案"))
        {
            ShowToast($"已保存个人方案“{preset.Name}”");
        }
    }

    private async void ArtworkWorkbench_ImportPresetRequested(object? sender, EventArgs e)
    {
        var path = ArtworkWorkbenchFileDialogs.ChoosePresetToImport(this);
        if (path is null) return;
        try
        {
            var preset = await ThemeArtworkPresetExchange.ImportAsync(path);
            var existingIndex = ArtworkPresetLibraryService.FindIndex(_artworkPresets, preset.Name);
            if (existingIndex >= 0 && !ShowProductConfirmation(
                    "替换同名图像方案？",
                    $"本机已经保存了“{preset.Name}”。导入只替换这份方案，不会改变主题参数或图片来源。",
                    "替换并导入")) return;

            var result = ArtworkPresetLibraryService.Upsert(_artworkPresets, preset);
            if (result == ArtworkPresetUpsertResult.CapacityReached)
            {
                ShowProductMessage(
                    "无法导入图像方案",
                    $"本机最多保存 {ArtworkPresetLibraryService.MaximumPresetCount} 个个人图像方案，请先删除不再使用的方案。",
                    ProductDialogKind.Warning);
                return;
            }

            var index = ArtworkPresetLibraryService.FindIndex(_artworkPresets, preset.Name);
            ArtworkWorkbench.SelectPreset(index >= 0 ? _artworkPresets[index] : null);
            if (!await TrySaveArtworkPresetLibraryAsync("导入图像方案")) return;
            LocalLog.Write($"Imported artwork preset '{preset.Name}' from {path}.");
            ShowToast($"已导入图像方案“{preset.Name}”");
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Importing an artwork preset failed.", exception);
            ArtworkWorkbench.SetApplyState(ArtworkApplyState.ImportFailed, exception.Message);
            ShowProductMessage("无法导入图像方案", exception.Message, ProductDialogKind.Error);
        }
    }

    private async void ArtworkWorkbench_ExportPresetRequested(object? sender, EventArgs e)
    {
        if (ArtworkWorkbench.SelectedPreset is not { } preset) return;
        var path = ArtworkWorkbenchFileDialogs.ChoosePresetExportPath(this, preset);
        if (path is null) return;
        try
        {
            await ThemeArtworkPresetExchange.ExportAsync(path, preset);
            LocalLog.Write($"Exported artwork preset '{preset.Name}' to {path}.");
            ShowToast($"已导出图像方案“{preset.Name}”");
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Exporting an artwork preset failed.", exception);
            ArtworkWorkbench.SetApplyState(ArtworkApplyState.Failed, exception.Message);
            ShowProductMessage("无法导出图像方案", exception.Message, ProductDialogKind.Error);
        }
    }

    private async void ArtworkWorkbench_DeletePresetRequested(object? sender, EventArgs e)
    {
        if (ArtworkWorkbench.SelectedPreset is not { } preset ||
            !ShowProductConfirmation(
                "删除个人图像方案",
                $"将删除“{preset.Name}”。已经应用到主题的参数和图片来源不会改变。",
                "删除方案",
                dangerous: true)) return;
        _artworkPresets.Remove(preset);
        ArtworkWorkbench.SelectPreset(null);
        if (await TrySaveArtworkPresetLibraryAsync("删除个人图像方案"))
        {
            ShowToast("个人图像方案已删除");
        }
    }

    private async Task<bool> TrySaveArtworkPresetLibraryAsync(string operation)
    {
        var preferencesRevision = MarkPreferencesDirty();
        try
        {
            await SavePreferencesAsync();
            MarkPreferencesPersisted(preferencesRevision);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LocalLog.Write($"{operation} failed.", exception);
            ArtworkWorkbench.SetApplyState(
                ArtworkApplyState.SaveFailed,
                "个人图像方案未写入磁盘");
            ShowProductMessage($"{operation}失败", exception.Message, ProductDialogKind.Error);
            return false;
        }
    }
}
