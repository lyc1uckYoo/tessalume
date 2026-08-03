using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tessalume.Core.Updates;

public sealed record ReleaseUpdate(
    Version Version,
    string VersionLabel,
    string ReleaseNotes,
    Uri ReleasePage,
    Uri DownloadUri,
    long DownloadSize,
    string Sha256);

public sealed record UpdateDownloadProgress(long BytesReceived, long TotalBytes);

public sealed class ReleaseUpdateClient : IDisposable
{
    private const long MaximumExecutableBytes = 512L * 1024L * 1024L;
    private const int MaximumChecksumTextLength = 128 * 1024;
    private static readonly Regex ChecksumLine = new(
        @"^(?<hash>[a-fA-F0-9]{64})\s+\*?(?<name>[^\r\n]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _latestReleaseUri;
    private readonly string _downloadDirectory;
    private readonly Version _currentVersion;
    private readonly string _userAgent;
    private bool _disposed;

    public ReleaseUpdateClient(
        string repositoryOwner,
        string repositoryName,
        string dataDirectory,
        Version currentVersion)
        : this(
            new HttpClient { Timeout = TimeSpan.FromMinutes(10) },
            repositoryOwner,
            repositoryName,
            dataDirectory,
            currentVersion,
            ownsHttpClient: true)
    {
    }

    public ReleaseUpdateClient(
        HttpClient httpClient,
        string repositoryOwner,
        string repositoryName,
        string dataDirectory,
        Version currentVersion)
        : this(
            httpClient,
            repositoryOwner,
            repositoryName,
            dataDirectory,
            currentVersion,
            ownsHttpClient: false)
    {
    }

    private ReleaseUpdateClient(
        HttpClient httpClient,
        string repositoryOwner,
        string repositoryName,
        string dataDirectory,
        Version currentVersion,
        bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(currentVersion);

        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _latestReleaseUri = new Uri(
            $"https://api.github.com/repos/{Uri.EscapeDataString(repositoryOwner)}/{Uri.EscapeDataString(repositoryName)}/releases/latest");
        _downloadDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "updates", "downloads");
        _currentVersion = currentVersion;
        _userAgent = $"Tessalume/{currentVersion}";
    }

    public async Task<ReleaseUpdate?> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var request = CreateRequest(_latestReleaseUri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if ((response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) &&
            response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0")
        {
            throw new InvalidOperationException("GitHub 更新检查请求过于频繁，请稍后再试。");
        }

        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
            root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
        {
            return null;
        }

        var tagName = RequiredString(root, "tag_name");
        if (!TryParseVersion(tagName, out var version) || version <= _currentVersion)
        {
            return null;
        }

        var releasePage = RequiredHttpsUri(root, "html_url");
        var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
            ? body.GetString() ?? string.Empty
            : string.Empty;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("GitHub Release 没有提供可下载的更新文件。");
        }

        JsonElement? executableAsset = null;
        JsonElement? checksumAsset = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = RequiredString(asset, "name");
            if (string.Equals(name, "Tessalume.exe", StringComparison.OrdinalIgnoreCase))
            {
                executableAsset = asset.Clone();
            }
            else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                checksumAsset = asset.Clone();
            }
        }

        if (executableAsset is not { } executable)
        {
            throw new InvalidDataException("最新版本缺少 Tessalume.exe 更新文件。");
        }

        var downloadUri = RequiredHttpsUri(executable, "browser_download_url");
        var downloadSize = executable.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var size)
            ? size
            : 0;
        if (downloadSize <= 0 || downloadSize > MaximumExecutableBytes)
        {
            throw new InvalidDataException("更新文件大小无效或超出安全限制。");
        }

        var sha256 = TryReadAssetDigest(executable);
        if (sha256 is null)
        {
            if (checksumAsset is not { } checksum)
            {
                throw new InvalidDataException("最新版本没有提供 SHA-256 校验信息。");
            }

            sha256 = await DownloadChecksumAsync(
                RequiredHttpsUri(checksum, "browser_download_url"),
                cancellationToken);
        }

        return new ReleaseUpdate(
            version,
            tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tagName : $"v{version}",
            notes,
            releasePage,
            downloadUri,
            downloadSize,
            sha256);
    }

    public async Task<string> DownloadAsync(
        ReleaseUpdate release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(release);
        Directory.CreateDirectory(_downloadDirectory);
        var destination = Path.Combine(_downloadDirectory, $"Tessalume-{release.Version}.exe.download");
        var partial = destination + ".partial";
        File.Delete(partial);

        try
        {
            using var request = CreateRequest(release.DownloadUri);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength ?? release.DownloadSize;
            if (contentLength <= 0 || contentLength > MaximumExecutableBytes)
            {
                throw new InvalidDataException("服务器返回的更新文件大小无效或超出安全限制。");
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
                    if (received > MaximumExecutableBytes)
                    {
                        throw new InvalidDataException("更新文件超过安全大小限制。");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new UpdateDownloadProgress(received, contentLength));
                }

                await output.FlushAsync(cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualHash, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新文件的 SHA-256 校验失败，已拒绝安装。");
            }

            File.Move(partial, destination, overwrite: true);
            progress?.Report(new UpdateDownloadProgress(received, contentLength));
            return destination;
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    private HttpRequestMessage CreateRequest(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新地址必须使用 HTTPS。");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse(_userAgent));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private async Task<string> DownloadChecksumAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumChecksumTextLength)
        {
            throw new InvalidDataException("SHA-256 校验文件超出安全大小限制。");
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > MaximumChecksumTextLength)
        {
            throw new InvalidDataException("SHA-256 校验文件超出安全大小限制。");
        }

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ChecksumLine.Match(line);
            if (match.Success && string.Equals(match.Groups["name"].Value, "Tessalume.exe", StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["hash"].Value.ToUpperInvariant();
            }
        }

        throw new InvalidDataException("SHA256SUMS.txt 中没有 Tessalume.exe 的校验值。");
    }

    private static string? TryReadAssetDigest(JsonElement asset)
    {
        if (!asset.TryGetProperty("digest", out var digestElement) || digestElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var digest = digestElement.GetString();
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hash = digest[prefix.Length..];
        return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash.ToUpperInvariant() : null;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var separator = normalized.IndexOfAny(['-', '+']);
        if (separator >= 0)
        {
            normalized = normalized[..separator];
        }

        return Version.TryParse(normalized, out version!);
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"GitHub Release 缺少 {propertyName} 字段。");
        }

        return property.GetString()!;
    }

    private static Uri RequiredHttpsUri(JsonElement element, string propertyName)
    {
        var value = RequiredString(element, propertyName);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GitHub Release 的 {propertyName} 不是安全的 HTTPS 地址。");
        }

        return uri;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
