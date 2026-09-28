using System.IO;
using System.Windows;
using Microsoft.Win32;
using Tessalume.App.Features.ArtworkLibrary.Application;
using Tessalume.App.Features.ArtworkLibrary.Presentation;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private ArtworkLibraryService? _artworkLibraryService;
    private readonly SemaphoreSlim _artworkLibraryOperation = new(1, 1);
    private string? _personalizationThemeOverride;

    private void InitializeArtworkLibrary()
    {
        _artworkLibraryService = new ArtworkLibraryService(_layout.DataDirectory);
        ArtworkLibraryPage.ApplyRequested += ArtworkLibrary_ApplyRequested;
        ArtworkLibraryPage.ImportRequested += ArtworkLibrary_ImportRequested;
        ArtworkLibraryPage.DeleteRequested += ArtworkLibrary_DeleteRequested;
        ArtworkLibraryPage.RestoreRequested += ArtworkLibrary_RestoreRequested;
        ArtworkLibraryPage.UndoRequested += ArtworkLibrary_UndoRequested;
        ArtworkLibraryPage.WorkbenchRequested += ArtworkLibrary_WorkbenchRequested;
        ArtworkWorkbench.ChooseLibraryImageRequested += ArtworkWorkbench_ChooseLibraryImageRequested;
    }

    private async void ArtworkLibrary_Click(object sender, RoutedEventArgs e) =>
        await OpenArtworkLibraryAsync();

    private async void ThemeDetailPanel_ArtworkLibraryRequested(object? sender, EventArgs e) =>
        await OpenArtworkLibraryAsync(ThemeDetailPanel.Theme?.ThemeId);

    private async void ArtworkWorkbench_ChooseLibraryImageRequested(object? sender, ArtworkChooseImageEventArgs e) =>
        await OpenArtworkLibraryAsync(e.ThemeId, e.Region, e.Mode, preserveEntryTarget: true);

    private async Task OpenArtworkLibraryAsync(
        string? themeId = null,
        ArtworkRegion region = ArtworkRegion.Hero,
        ArtworkColorMode? mode = null, bool preserveEntryTarget = false)
    {
        if (_artworkLibraryService is null || Volatile.Read(ref _disposeStarted) != 0) return;
        var initialCharacterThemeId = themeId;
        themeId ??= GetVisualAdjustmentTheme()?.ThemeId;
        var packages = _themes.Where(theme => theme.IsValid && theme.CatalogItem.Package is not null)
            .Select(theme => theme.CatalogItem.Package!).ToArray();
        NavigateTo(AppRoute.ArtworkLibrary);
        ArtworkLibraryPage.Configure(_artworkLibraryService, packages, CaptureLibrarySettings(),
            themeId ?? string.Empty, region,
            mode ?? (_codexDarkMode ?? _darkMode ? ArtworkColorMode.Dark : ArtworkColorMode.Light),
            preserveEntryTarget, initialCharacterThemeId);
        await ArtworkLibraryPage.RefreshAsync();
        ArtworkLibraryPage.SetUndoAvailable(_artworkLibraryUndo is not null);
    }

    private Dictionary<string, ThemeVisualSettings> CaptureLibrarySettings() =>
        _themes.Where(theme => theme.IsValid && !string.IsNullOrEmpty(theme.ThemeId))
            .ToDictionary(theme => theme.ThemeId!, theme => GetVisualSettings(theme.ThemeId!), StringComparer.OrdinalIgnoreCase);

    private async void ArtworkLibrary_ImportRequested(object? sender, ArtworkLibraryImportEventArgs e)
    {
        if (_artworkLibraryService is null) return;
        var dialog = new OpenFileDialog
        {
            Title = $"导入图片 · {e.CharacterName}",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        await RunArtworkLibraryOperationAsync(async () =>
        {
            var imported = 0;
            var failures = new List<string>();
            foreach (var path in dialog.FileNames)
            {
                _personalizationCancellation.Token.ThrowIfCancellationRequested();
                ArtworkLibraryPage.SetStatus($"正在导入 {imported + failures.Count + 1}/{dialog.FileNames.Length}…");
                try
                {
                    await _artworkLibraryService.ImportAsync(path, e.CharacterId, e.CharacterName,
                        e.Region, e.Mode, _personalizationCancellation.Token);
                    imported++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                    InvalidDataException or NotSupportedException)
                {
                    failures.Add($"{Path.GetFileName(path)}：{exception.Message}");
                }
            }
            await ArtworkLibraryPage.RefreshAsync();
            var message = $"已导入 {imported} 张图片，选择预览后即可应用。";
            if (failures.Count > 0) message += $" {failures.Count} 张未导入：{string.Join("；", failures.Take(3))}";
            ArtworkLibraryPage.SetStatus(message);
            ShowToast($"已导入 {imported} 张图片" + (failures.Count > 0 ? $"，{failures.Count} 张未导入" : string.Empty));
        });
    }

    private void ArtworkLibrary_WorkbenchRequested(object? sender, ArtworkLibraryTargetEventArgs e)
    {
        if (!_themes.Any(theme => theme.IsValid && theme.ThemeId == e.ThemeId)) return;
        _personalizationThemeOverride = e.ThemeId;
        _editingVisualDarkMode = e.Mode == ArtworkColorMode.Dark;
        NavigateTo(AppRoute.ArtworkStudio);
        UpdateVisualAdjustmentControls();
        ArtworkWorkbench.SelectEditingTarget(e.Region, e.Mode);
    }

    private async Task RunArtworkLibraryOperationAsync(Func<Task> operation)
    {
        if (Volatile.Read(ref _disposeStarted) != 0 || !await _artworkLibraryOperation.WaitAsync(0)) return;
        ArtworkLibraryPage.SetBusy(true);
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_personalizationCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            LocalLog.Write("Artwork library operation failed.", exception);
            ArtworkLibraryPage.SetStatus($"操作未完成：{exception.Message}。已导入的原图会保留，可以重试。");
            ShowToast("图库操作未完成，请查看提示后重试", warning: true);
        }
        finally
        {
            ArtworkLibraryPage.SetBusy(false);
            ArtworkLibraryPage.SetUndoAvailable(_artworkLibraryUndo is not null);
            _artworkLibraryOperation.Release();
        }
    }

    private async Task DisposeArtworkLibraryAsync()
    {
        ArtworkLibraryPage?.Dispose();
        await _artworkLibraryOperation.WaitAsync();
        _artworkLibraryOperation.Release();
        if (_artworkLibraryService is not null) await _artworkLibraryService.DisposeAsync();
        _artworkLibraryOperation.Dispose();
    }
}
