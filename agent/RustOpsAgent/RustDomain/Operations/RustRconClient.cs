using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

internal sealed class RustRconClient : IRconClient
{
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _receiveLoopCts;

    public event Action<string>? UnsolicitedMessage;

    public async Task ConnectAsync(Uri uri, string password, CancellationToken cancellationToken = default)
    {
        await _socket.ConnectAsync(uri, cancellationToken);
        _receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReceiveLoopAsync(_receiveLoopCts.Token), _receiveLoopCts.Token);
        await SendRawAsync($"auth:{password}", cancellationToken);
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("n");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;
        await SendRawAsync($"{correlationId}:{command}", cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
        using var _ = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));
        return await tcs.Task;
    }

    private async Task SendRawAsync(string message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            var segment = new ArraySegment<byte>(buffer);
            var result = await _socket.ReceiveAsync(segment, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var split = message.Split(':', 2);
            if (split.Length == 2 && _pending.TryRemove(split[0], out var tcs))
            {
                tcs.TrySetResult(split[1]);
                continue;
            }

            UnsolicitedMessage?.Invoke(message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _receiveLoopCts?.Cancel();
        if (_socket.State == WebSocketState.Open)
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None);
        _socket.Dispose();
    }
}
