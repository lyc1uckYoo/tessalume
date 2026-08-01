using System.Net;
using System.Text.Json;

namespace Tessalume.Core.Runtime;

public sealed class LoopbackCdpDiscovery : IDisposable
{
    private readonly HttpClient _client = new(new SocketsHttpHandler { UseProxy = false });

    public async Task<IReadOnlyList<CdpTarget>> DiscoverAsync(int port, CancellationToken cancellationToken = default)
    {
        ValidatePort(port);
        foreach (var host in new[] { "127.0.0.1", "[::1]" })
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
            try
            {
                using var response = await _client.GetAsync(
                    $"http://{host}:{port}/json/list",
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                var targets = await JsonSerializer.DeserializeAsync<CdpTarget[]>(stream, cancellationToken: timeout.Token) ?? [];
                return targets
                    .Where(target =>
                        string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase) &&
                        target.Url.StartsWith("app://", StringComparison.OrdinalIgnoreCase) &&
                        Uri.TryCreate(target.WebSocketDebuggerUrl, UriKind.Absolute, out _))
                    .ToArray();
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return [];
    }

    public void Dispose() => _client.Dispose();

    private static void ValidatePort(int port)
    {
        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
    }
}
