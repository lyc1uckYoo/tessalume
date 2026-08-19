using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tessalume.Core.Updates.Delta;

namespace Tessalume.Core.Updates;

public sealed record ReleaseUpdate(
    Version Version,
    string VersionLabel,
    string ReleaseNotes,
    Uri ReleasePage,
    Uri DownloadUri,
    long DownloadSize,
    string Sha256)
{
    public ReleaseDeltaAsset? Delta { get; init; }
    public bool UsesDelta => Delta is not null;
    public long PreferredDownloadSize => Delta?.DownloadSize ?? DownloadSize;
}

public enum UpdateDownloadPhase
{
    DownloadingFull,
    DownloadingDelta,
    ApplyingDelta,
    FallingBackToFull,
}

public sealed record UpdateDownloadProgress(
    long BytesReceived,
    long TotalBytes,
    UpdateDownloadPhase Phase = UpdateDownloadPhase.DownloadingFull);

public sealed partial class ReleaseUpdateClient : IDisposable
{
    private const long MaximumExecutableBytes = 512L * 1024L * 1024L;
    private const long MaximumDeltaBytes = 256L * 1024L * 1024L;
    private const int MaximumChecksumTextLength = 128 * 1024;
    private const int MaximumManifestBytes = 256 * 1024;
    private static readonly Regex ChecksumLine = new(
        @"^(?<hash>[a-fA-F0-9]{64})\s+\*?(?<name>[^\r\n]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _latestReleaseUri;
    private readonly string _downloadDirectory;
    private readonly string _currentExecutablePath;
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
            Environment.ProcessPath ?? string.Empty,
            ownsHttpClient: true)
    {
    }

    public ReleaseUpdateClient(
        string repositoryOwner,
        string repositoryName,
        string dataDirectory,
        Version currentVersion,
        string currentExecutablePath)
        : this(
            new HttpClient { Timeout = TimeSpan.FromMinutes(10) },
            repositoryOwner,
            repositoryName,
            dataDirectory,
            currentVersion,
            currentExecutablePath,
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
            Environment.ProcessPath ?? string.Empty,
            ownsHttpClient: false)
    {
    }

    public ReleaseUpdateClient(
        HttpClient httpClient,
        string repositoryOwner,
        string repositoryName,
        string dataDirectory,
        Version currentVersion,
        string currentExecutablePath)
        : this(
            httpClient,
            repositoryOwner,
            repositoryName,
            dataDirectory,
            currentVersion,
            currentExecutablePath,
            ownsHttpClient: false)
    {
    }

    private ReleaseUpdateClient(
        HttpClient httpClient,
        string repositoryOwner,
        string repositoryName,
        string dataDirectory,
        Version currentVersion,
        string currentExecutablePath,
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
        _currentExecutablePath = string.IsNullOrWhiteSpace(currentExecutablePath)
            ? string.Empty
            : Path.GetFullPath(currentExecutablePath);
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

        var assetsByName = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets.EnumerateArray())
        {
            var name = RequiredString(asset, "name");
            if (!assetsByName.TryAdd(name, asset.Clone()))
            {
                throw new InvalidDataException($"GitHub Release 包含重复资产：{name}。");
            }
        }

        if (!assetsByName.TryGetValue(UpdateDeltaManifest.TargetExecutableName, out var executable))
        {
            throw new InvalidDataException("最新版本缺少 Tessalume.exe 更新文件。");
        }

        var downloadUri = RequiredHttpsUri(executable, "browser_download_url");
        var downloadSize = ReadBoundedAssetSize(executable, MaximumExecutableBytes, "更新文件");
        assetsByName.TryGetValue("SHA256SUMS.txt", out var checksumAsset);
        var checksum = checksumAsset.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : checksumAsset;
        var sha256 = await ResolveAssetSha256Async(
            executable,
            checksum,
            UpdateDeltaManifest.TargetExecutableName,
            cancellationToken);

        var release = new ReleaseUpdate(
            version,
            tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tagName : $"v{version}",
            notes,
            releasePage,
            downloadUri,
            downloadSize,
            sha256);
        if (assetsByName.TryGetValue(UpdateDeltaManifest.FileName, out var manifestAsset))
        {
            release = release with
            {
                Delta = await TryResolveDeltaAsync(
                    release,
                    manifestAsset,
                    checksum,
                    assetsByName,
                    cancellationToken),
            };
        }
        return release;
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}
