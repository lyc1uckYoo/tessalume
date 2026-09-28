using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.ArtworkLibrary.Presentation;
using Tessalume.App.Infrastructure;

namespace Tessalume.App;

public partial class MainWindow
{
    internal Func<ArtworkLibraryItem, Task<bool>>? ArtworkLibraryDeleteConfirmation { get; set; }

    private async void ArtworkLibrary_DeleteRequested(object? sender, ArtworkLibraryDeleteEventArgs e) =>
        await DeleteArtworkLibraryItemAsync(e.Item);

    internal Task DeleteArtworkLibraryItemAsync(ArtworkLibraryItem item)
    {
        if (_artworkLibraryService is null || !item.CanDelete) return Task.CompletedTask;
        return RunArtworkLibraryOperationAsync(async () =>
        {
            var current = ArtworkLibraryPage.Items.FirstOrDefault(candidate => candidate.Id == item.Id);
            if (current is not { CanDelete: true }) return;
            var confirmed = ArtworkLibraryDeleteConfirmation is { } confirm
                ? await confirm(current)
                : ShowProductConfirmation("从图库删除图片",
                    $"从图库删除这张导入图片？\n\n{current.Name}\n\n已应用到主题的位置不受影响，原始文件不会删除。",
                    "从图库删除", dangerous: true);
            if (!confirmed) return;
            try
            {
                await _artworkLibraryService.DeleteImportedImageAsync(current.Id, _personalizationCancellation.Token);
            }
            catch (OperationCanceledException) when (_personalizationCancellation.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                LocalLog.Write("Artwork library imported image deletion failed.", exception);
                ArtworkLibraryPage.SetStatus($"删除未完成：{exception.Message}。图片仍保留在图库，可以重试。");
                ShowToast("图片未删除，可以重试", warning: true);
                return;
            }
            await ArtworkLibraryPage.CompleteImportedImageDeletionAsync(current);
            ArtworkLibraryPage.SetStatus("已从图库删除。已应用的主题图片保持不变。");
            ShowToast("已从图库删除");
        });
    }
}
