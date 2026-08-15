using System.IO;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void ArtworkWorkbench_ExportArtworkDefaultsRequested(
        object? sender,
        EventArgs e)
    {
        var theme = GetVisualAdjustmentTheme();
        var package = theme?.CatalogItem.Package;
        if (package is null)
        {
            ShowProductMessage(
                "无法导出主题推荐值",
                "请先选择一个有效主题。",
                ProductDialogKind.Warning);
            return;
        }

        _themeArtworkDefaults.TryGetValue(package.Manifest.Id, out var currentDefaults);
        var defaultsVersion = SuggestNextArtworkDefaultsVersion(
            currentDefaults?.DefaultsVersion);
        ThemeArtworkDefaultsDocument document;
        try
        {
            document = ArtworkDefaultsExporter.Create(
                package.Manifest.Id,
                defaultsVersion,
                GetVisualSettings(package.Manifest.Id));
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or InvalidDataException)
        {
            ShowProductMessage(
                "暂时不能导出主题推荐值",
                exception.Message,
                ProductDialogKind.Warning);
            return;
        }

        var path = ArtworkWorkbenchFileDialogs.ChooseArtworkDefaultsExportPath(
            this,
            package.RootDirectory);
        if (path is null) return;
        try
        {
            await ArtworkDefaultsExportService.ExportAsync(
                path,
                document,
                _personalizationCancellation.Token);
            LocalLog.Write(
                $"Exported artwork defaults candidate {defaultsVersion} for " +
                $"'{package.Manifest.Id}' to {path}.");
            ShowToast($"已导出主题推荐值候选 {defaultsVersion}");
        }
        catch (OperationCanceledException) when (_personalizationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Exporting artwork defaults failed.", exception);
            ShowProductMessage(
                "无法导出主题推荐值",
                exception.Message,
                ProductDialogKind.Error);
        }
    }

    private static string SuggestNextArtworkDefaultsVersion(string? current)
    {
        if (!Version.TryParse(current, out var version)) return "1.0.0";
        var build = version.Build < 0 ? 0 : version.Build;
        return new Version(version.Major, version.Minor, build + 1).ToString(3);
    }
}
