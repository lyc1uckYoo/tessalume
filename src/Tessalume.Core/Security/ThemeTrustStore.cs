using System.IO;
using System.Text.Json;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Security;

public sealed class ThemeTrustStore(string dataDirectory, string? sharedTemplatePath = null) : IDisposable
{
    private readonly string _path = Path.Combine(dataDirectory, "trusted-themes.json");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<bool> IsTrustedAsync(ThemePackage package, CancellationToken cancellationToken = default)
    {
        if (!package.IsAdvanced)
        {
            return true;
        }

        var fingerprint = await CalculateFingerprintAsync(package, cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken);
            return entries.TryGetValue(package.Manifest.Id, out var trusted) &&
                string.Equals(trusted, fingerprint, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task TrustAsync(ThemePackage package, CancellationToken cancellationToken = default)
    {
        if (!package.IsAdvanced)
        {
            return;
        }

        var fingerprint = await CalculateFingerprintAsync(package, cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken);
            entries[package.Manifest.Id] = fingerprint;
            var temporaryPath = _path + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var entries = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
                stream,
                cancellationToken: cancellationToken);
            return entries is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private Task<string> CalculateFingerprintAsync(
        ThemePackage package,
        CancellationToken cancellationToken)
    {
        if (!package.Manifest.UsesSharedTemplateV1)
        {
            return ThemeFingerprintCalculator.CalculateAsync(package, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(sharedTemplatePath) || !File.Exists(sharedTemplatePath))
        {
            throw new InvalidOperationException(
                "The shared Template 1.0 stylesheet is required to trust this theme.");
        }

        return ThemeFingerprintCalculator.CalculateEffectiveAsync(
            package,
            sharedTemplatePath,
            cancellationToken);
    }

    public void Dispose() => _lock.Dispose();
}
