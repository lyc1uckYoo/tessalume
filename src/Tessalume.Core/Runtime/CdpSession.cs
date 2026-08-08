using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tessalume.Core.Runtime;

public sealed class CdpSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions MessageJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _receiveTask;
    private int _nextId;

    public async Task ConnectAsync(string webSocketDebuggerUrl, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(webSocketDebuggerUrl, UriKind.Absolute);
        if (!uri.IsLoopback)
        {
            throw new InvalidOperationException("CDP connections are restricted to the local computer.");
        }

        await _socket.ConnectAsync(uri, cancellationToken);
        _receiveTask = ReceiveLoopAsync(_lifetime.Token);
    }

    public async Task<JsonElement> EvaluateAsync(string expression, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync(
            "Runtime.evaluate",
            new
            {
                expression,
                awaitPromise = true,
                returnByValue = true,
                userGesture = false,
            },
            cancellationToken);

        if (result.TryGetProperty("exceptionDetails", out var exceptionDetails))
        {
            throw new InvalidOperationException($"Codex renderer rejected the theme: {exceptionDetails}");
        }

        return result.GetProperty("result").TryGetProperty("value", out var value)
            ? value.Clone()
            : default;
    }

    internal Task<JsonElement> SendCommandAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default) =>
        SendAsync(method, parameters, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
        }

        _socket.Dispose();
        _sendLock.Dispose();
        _lifetime.Dispose();
    }

    private async Task<JsonElement> SendAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Could not reserve a CDP request id.");
        }

        var message = JsonSerializer.SerializeToUtf8Bytes(
            new { id, method, @params = parameters },
            MessageJsonOptions);
        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await _socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }

            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    FailPending(new WebSocketException("The Codex renderer closed the CDP connection."));
                    return;
                }

                await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }
            while (!result.EndOfMessage);

            if (!message.TryGetBuffer(out var messageBuffer))
            {
                throw new InvalidOperationException("Could not read the CDP response buffer.");
            }

            using var document = JsonDocument.Parse(messageBuffer.AsMemory());
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) || !_pending.TryRemove(idElement.GetInt32(), out var completion))
            {
                continue;
            }

            if (root.TryGetProperty("error", out var error))
            {
                completion.TrySetException(new InvalidOperationException($"CDP error: {error}"));
            }
            else if (root.TryGetProperty("result", out var response))
            {
                completion.TrySetResult(response.Clone());
            }
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }

        _pending.Clear();
    }
}
