using System.IO;
using System.Text.Json;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal static class ArtworkDefaultsExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task ExportAsync(
        string path,
        ThemeArtworkDefaultsDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        ThemeArtworkDefaultsValidator.Validate(document);

        var absolutePath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(absolutePath) ??
                        throw new InvalidDataException("无法确定导出目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = absolutePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(document, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, absolutePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
