using System.Security.Cryptography;
using System.Text.Json;
using Tessalume.Core.Updates.Delta;

namespace Tessalume.Core.Updates;

public sealed partial class ReleaseUpdateClient
{
    private async Task<ReleaseDeltaAsset?> TryResolveDeltaAsync(
        ReleaseUpdate release,
        JsonElement manifestAsset,
        JsonElement? checksumAsset,
        Dictionary<string, JsonElement> assetsByName,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifestSize = ReadBoundedAssetSize(manifestAsset, MaximumManifestBytes, "增量更新清单");
            var manifestSha256 = await ResolveAssetSha256Async(
                manifestAsset,
                checksumAsset,
                UpdateDeltaManifest.FileName,
                cancellationToken);
            var manifestBytes = await DownloadSmallVerifiedAssetAsync(
                RequiredHttpsUri(manifestAsset, "browser_download_url"),
                manifestSize,
                manifestSha256,
                cancellationToken);
            var manifest = JsonSerializer.Deserialize<UpdateDeltaManifest>(
                manifestBytes,
                UpdateDeltaManifest.JsonOptions);
            if (manifest is null || manifest.SchemaVersion != UpdateDeltaManifest.CurrentSchemaVersion ||
                !string.Equals(
                    NormalizeVersionLabel(manifest.TargetVersion),
                    NormalizeVersionLabel(release.VersionLabel),
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    manifest.TargetFileName,
                    UpdateDeltaManifest.TargetExecutableName,
                    StringComparison.Ordinal) ||
                manifest.TargetSize != release.DownloadSize ||
                !string.Equals(manifest.TargetSha256, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var currentVersion = NormalizeVersionLabel(_currentVersion.ToString());
            var matches = manifest.Deltas
                .Where(entry => string.Equals(
                    NormalizeVersionLabel(entry.FromVersion),
                    currentVersion,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1) return null;
            var match = matches[0];
            if (!string.Equals(match.Algorithm, UpdateDeltaEntry.SupportedAlgorithm, StringComparison.Ordinal) ||
                !IsValidSha256(match.FromSha256) ||
                !IsValidSha256(match.AssetSha256) ||
                match.AssetSize <= 0 ||
                match.AssetSize >= release.DownloadSize ||
                match.AssetSize > MaximumDeltaBytes ||
                !IsSafeAssetName(match.AssetName) ||
                !assetsByName.TryGetValue(match.AssetName, out var deltaAsset) ||
                ReadBoundedAssetSize(deltaAsset, MaximumDeltaBytes, "增量更新资产") != match.AssetSize)
            {
                return null;
            }
            var publishedDeltaHash = await ResolveAssetSha256Async(
                deltaAsset,
                checksumAsset,
                match.AssetName,
                cancellationToken);
            if (!string.Equals(publishedDeltaHash, match.AssetSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new ReleaseDeltaAsset(
                NormalizeVersionLabel(match.FromVersion),
                match.FromSha256.ToUpperInvariant(),
                match.Algorithm,
                match.AssetName,
                RequiredHttpsUri(deltaAsset, "browser_download_url"),
                match.AssetSize,
                match.AssetSha256.ToUpperInvariant());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<byte[]> DownloadSmallVerifiedAssetAsync(
        Uri uri,
        long declaredSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumManifestBytes)
        {
            throw new InvalidDataException("增量更新清单超出安全大小限制。");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.LongLength != declaredSize || bytes.Length > MaximumManifestBytes ||
            !string.Equals(
                Convert.ToHexString(SHA256.HashData(bytes)),
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("增量更新清单大小或 SHA-256 校验失败。");
        }
        return bytes;
    }

    private async Task<string> ResolveAssetSha256Async(
        JsonElement asset,
        JsonElement? checksumAsset,
        string assetName,
        CancellationToken cancellationToken)
    {
        var digest = TryReadAssetDigest(asset);
        if (digest is not null) return digest;
        if (checksumAsset is not { } checksum)
        {
            throw new InvalidDataException($"最新版本没有提供 {assetName} 的 SHA-256 校验信息。");
        }
        return await DownloadChecksumAsync(
            RequiredHttpsUri(checksum, "browser_download_url"),
            assetName,
            cancellationToken);
    }

    private async Task<string> DownloadChecksumAsync(
        Uri uri,
        string assetName,
        CancellationToken cancellationToken)
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
            if (match.Success && string.Equals(
                    match.Groups["name"].Value,
                    assetName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["hash"].Value.ToUpperInvariant();
            }
        }

        throw new InvalidDataException($"SHA256SUMS.txt 中没有 {assetName} 的校验值。");
    }

    private static long ReadBoundedAssetSize(JsonElement asset, long maximumBytes, string displayName)
    {
        var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var value)
            ? value
            : 0;
        if (size <= 0 || size > maximumBytes)
        {
            throw new InvalidDataException($"{displayName}大小无效或超出安全限制。");
        }
        return size;
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
        return IsValidSha256(hash) ? hash.ToUpperInvariant() : null;
    }

    private static bool TryParseVersion(string value, out Version version) =>
        Version.TryParse(NormalizeVersionLabel(value), out version!);

    private static string NormalizeVersionLabel(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)) normalized = normalized[1..];
        var separator = normalized.IndexOfAny(['-', '+']);
        if (separator >= 0) normalized = normalized[..separator];
        if (!Version.TryParse(normalized, out var version)) return string.Empty;
        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private static bool IsSafeAssetName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
        !value.Contains("..", StringComparison.Ordinal) &&
        value.EndsWith(".delta", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

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
}
