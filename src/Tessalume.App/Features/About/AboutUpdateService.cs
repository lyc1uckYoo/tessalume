using System.IO;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Compatibility;
using Tessalume.Core.Updates;

namespace Tessalume.App.Features.About;

internal sealed class AboutUpdateService : IDisposable
{
    private readonly ReleaseUpdateClient _applicationClient;
    private readonly CompatibilityUpdateClient _compatibilityClient;
    private readonly CompatibilityPackStore _compatibilityPacks;
    private readonly UpdateRollbackStore _rollbackStore;

    public AboutUpdateService(
        PortableLayout layout,
        CompatibilityPackStore compatibilityPacks,
        Version currentVersion,
        string? processPath = null)
    {
        var executablePath = ResolveExecutablePath(layout, processPath ?? Environment.ProcessPath);
        var executableName = Path.GetFileName(executablePath);
        _compatibilityPacks = compatibilityPacks;
        _applicationClient = new ReleaseUpdateClient(
            BrandInfo.RepositoryOwner,
            BrandInfo.RepositoryName,
            layout.DataDirectory,
            currentVersion,
            executablePath);
        _compatibilityClient = new CompatibilityUpdateClient(
            BrandInfo.RepositoryOwner,
            BrandInfo.RepositoryName,
            layout.DataDirectory,
            currentVersion);
        _rollbackStore = new UpdateRollbackStore(
            layout.RootDirectory,
            layout.DataDirectory,
            executableName);
    }

    internal static string ResolveExecutablePath(PortableLayout layout, string? processPath)
    {
        var applicationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.RootDirectory));
        var fallback = Path.Combine(applicationRoot, $"{BrandInfo.ProductName}.exe");
        if (string.IsNullOrWhiteSpace(processPath)) return fallback;

        try
        {
            var candidate = Path.GetFullPath(processPath);
            var candidateDirectory = Path.GetDirectoryName(candidate) is { } directory
                ? Path.TrimEndingDirectorySeparator(directory)
                : null;
            if (string.Equals(candidateDirectory, applicationRoot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException)
        {
        }

        return fallback;
    }

    public async Task<AboutUpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        CompatibilityPackInstallResult? compatibilityUpdate = null;
        Exception? compatibilityError = null;
        var compatibilityTask = CheckCompatibilityAsync(cancellationToken);
        var applicationTask = _applicationClient.CheckLatestAsync(cancellationToken);
        try
        {
            compatibilityUpdate = await compatibilityTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            compatibilityError = exception;
        }

        var applicationUpdate = await applicationTask;
        return new AboutUpdateCheckResult(
            applicationUpdate,
            compatibilityUpdate,
            compatibilityError);
    }

    public Task<string> DownloadApplicationAsync(
        ReleaseUpdate release,
        IProgress<UpdateDownloadProgress> progress,
        CancellationToken cancellationToken) =>
        _applicationClient.DownloadAsync(release, progress, cancellationToken);

    public Task<UpdateRollbackInfo?> LoadRollbackAsync(CancellationToken cancellationToken) =>
        _rollbackStore.LoadAsync(cancellationToken);

    public Task<UpdateRollbackInfo> SaveRollbackAsync(
        string currentVersionLabel,
        string previousVersionLabel,
        string executablePath,
        string dataSnapshotId,
        CancellationToken cancellationToken) =>
        _rollbackStore.SaveAsync(
            currentVersionLabel,
            previousVersionLabel,
            executablePath,
            dataSnapshotId,
            cancellationToken);

    private async Task<CompatibilityPackInstallResult?> CheckCompatibilityAsync(
        CancellationToken cancellationToken)
    {
        var currentPack = _compatibilityPacks.Resolve();
        var release = await _compatibilityClient.CheckLatestAsync(
            currentPack.PackVersion,
            cancellationToken);
        if (release is null) return null;

        var downloaded = await _compatibilityClient.DownloadAsync(
            release,
            cancellationToken: cancellationToken);
        try
        {
            return await _compatibilityPacks.InstallAsync(
                downloaded,
                release.Sha256,
                cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(downloaded);
            }
            catch (IOException exception)
            {
                LocalLog.Write("Cleaning the downloaded compatibility package failed.", exception);
            }
        }
    }

    public void Dispose()
    {
        _applicationClient.Dispose();
        _compatibilityClient.Dispose();
    }
}
