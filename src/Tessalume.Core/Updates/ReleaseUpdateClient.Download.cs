using System.Buffers;
using System.Security.Cryptography;
using Tessalume.Core.Updates.Delta;

namespace Tessalume.Core.Updates;

public sealed partial class ReleaseUpdateClient
{
    public async Task<string> DownloadAsync(
        ReleaseUpdate release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(release);
        Directory.CreateDirectory(_downloadDirectory);
        if (release.Delta is not null && File.Exists(_currentExecutablePath))
        {
            try
            {
                return await DownloadAndApplyDeltaAsync(release, progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                progress?.Report(new UpdateDownloadProgress(
                    0,
                    release.DownloadSize,
                    UpdateDownloadPhase.FallingBackToFull));
            }
        }

        return await DownloadFullAsync(release, progress, cancellationToken);
    }

    private async Task<string> DownloadAndApplyDeltaAsync(
        ReleaseUpdate release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var delta = release.Delta ?? throw new InvalidOperationException("没有可用的增量更新描述。");
        var basisHash = await BinaryDeltaCodec.ComputeSha256Async(_currentExecutablePath, cancellationToken);
        if (!string.Equals(basisHash, delta.FromSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("当前 EXE 与增量包基线不一致。");
        }

        var safeFromVersion = NormalizeVersionLabel(delta.FromVersion).Replace('.', '-');
        var safeTargetVersion = NormalizeVersionLabel(release.VersionLabel).Replace('.', '-');
        var deltaPath = Path.Combine(
            _downloadDirectory,
            $"Tessalume-{safeFromVersion}-to-{safeTargetVersion}.delta.download");
        var destination = GetExecutableDestination(release);
        var partialOutput = destination + ".partial";
        File.Delete(partialOutput);
        try
        {
            await DownloadVerifiedAssetAsync(
                delta.DownloadUri,
                delta.DownloadSize,
                delta.Sha256,
                MaximumDeltaBytes,
                deltaPath,
                UpdateDownloadPhase.DownloadingDelta,
                progress,
                cancellationToken);
            progress?.Report(new UpdateDownloadProgress(
                delta.DownloadSize,
                delta.DownloadSize,
                UpdateDownloadPhase.ApplyingDelta));
            await BinaryDeltaCodec.ApplyAsync(
                _currentExecutablePath,
                deltaPath,
                partialOutput,
                cancellationToken);
            var outputInfo = new FileInfo(partialOutput);
            if (outputInfo.Length != release.DownloadSize)
            {
                throw new InvalidDataException("增量重建后的 EXE 大小与正式版本不一致。");
            }
            var outputHash = await BinaryDeltaCodec.ComputeSha256Async(partialOutput, cancellationToken);
            if (!string.Equals(outputHash, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("增量重建后的 EXE 未通过目标 SHA-256 校验。");
            }
            File.Move(partialOutput, destination, overwrite: true);
            return destination;
        }
        finally
        {
            TryDeleteTemporaryFile(deltaPath);
            TryDeleteTemporaryFile(deltaPath + ".partial");
            TryDeleteTemporaryFile(partialOutput);
        }
    }

    private Task<string> DownloadFullAsync(
        ReleaseUpdate release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken) =>
        DownloadVerifiedAssetAsync(
            release.DownloadUri,
            release.DownloadSize,
            release.Sha256,
            MaximumExecutableBytes,
            GetExecutableDestination(release),
            UpdateDownloadPhase.DownloadingFull,
            progress,
            cancellationToken);

    private async Task<string> DownloadVerifiedAssetAsync(
        Uri uri,
        long declaredSize,
        string expectedSha256,
        long maximumBytes,
        string destination,
        UpdateDownloadPhase phase,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (declaredSize <= 0 || declaredSize > maximumBytes || !IsValidSha256(expectedSha256))
        {
            throw new InvalidDataException("更新资产大小或 SHA-256 无效。");
        }

        var partial = destination + ".partial";
        File.Delete(partial);
        try
        {
            using var request = CreateRequest(uri);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength ?? declaredSize;
            if (contentLength <= 0 || contentLength > maximumBytes)
            {
                throw new InvalidDataException("服务器返回的更新资产大小无效或超出安全限制。");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            long received = 0;
            try
            {
                await using var output = new FileStream(
                    partial,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0) break;
                    received += read;
                    if (received > maximumBytes)
                    {
                        throw new InvalidDataException("更新资产超过安全大小限制。");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new UpdateDownloadProgress(received, contentLength, phase));
                }
                await output.FlushAsync(cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (received != declaredSize)
            {
                throw new InvalidDataException("下载资产的实际大小与发布清单不一致。");
            }
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新资产的 SHA-256 校验失败，已拒绝安装。");
            }

            File.Move(partial, destination, overwrite: true);
            progress?.Report(new UpdateDownloadProgress(received, contentLength, phase));
            return destination;
        }
        catch
        {
            TryDeleteTemporaryFile(partial);
            throw;
        }
    }

    private string GetExecutableDestination(ReleaseUpdate release) =>
        Path.Combine(_downloadDirectory, $"Tessalume-{release.Version}.exe.download");

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
