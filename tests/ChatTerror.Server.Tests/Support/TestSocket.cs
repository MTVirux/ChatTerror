using System.Net.WebSockets;
using System.Text;
using ChatTerror.Protocol;

namespace ChatTerror.Server.Tests.Support;

public sealed class TestSocket(WebSocket socket) : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private readonly byte[] buffer = new byte[Limits.MaxFrameBytes * 2];

    public bool IsClosed { get; private set; }

    public Task SendAsync(RelayFrame frame) => SendRawAsync(ProtocolJson.Serialize(frame));

    public Task SendRawAsync(string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    // Returns null once the socket is closed.
    public async Task<RelayFrame?> NextAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        var length = 0;
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(length), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    IsClosed = true;
                    if (socket.State == WebSocketState.CloseReceived)
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    return null;
                }

                length += result.Count;
                if (result.EndOfMessage)
                    return ProtocolJson.Deserialize<RelayFrame>(Encoding.UTF8.GetString(buffer, 0, length));
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException("No frame received in time.");
        }
        catch (WebSocketException)
        {
            IsClosed = true;
            return null;
        }
    }

    // Skips unrelated frames, but an unexpected error or authFail fails fast.
    public async Task<T> ReceiveAsync<T>(TimeSpan? timeout = null) where T : RelayFrame
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException($"No {typeof(T).Name} received in time.");

            var frame = await NextAsync(remaining);
            switch (frame)
            {
                case null:
                    throw new InvalidOperationException($"Socket closed while waiting for {typeof(T).Name}.");
                case T match:
                    return match;
                case ErrorFrame or AuthFailFrame:
                    throw new InvalidOperationException($"Unexpected {frame} while waiting for {typeof(T).Name}.");
            }
        }
    }

    public async Task<List<RelayFrame>> ExpectClosedAsync(TimeSpan? timeout = null)
    {
        var frames = new List<RelayFrame>();
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("Socket was not closed in time.");

            var frame = await NextAsync(remaining);
            if (frame is null)
                return frames;
            frames.Add(frame);
        }
    }

    // Every frame is handled in order, so a reply to a bad frame proves earlier frames were processed.
    public async Task<List<RelayFrame>> BarrierAsync()
    {
        var skipped = new List<RelayFrame>();
        await SendRawAsync("{}");
        while (true)
        {
            var frame = await NextAsync();
            if (frame is ErrorFrame { Code: RelayErrors.BadFrame })
                return skipped;
            skipped.Add(frame ?? throw new InvalidOperationException("Socket closed during barrier."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            }
        }
        catch (Exception)
        {
            socket.Abort();
        }
        socket.Dispose();
    }
}
