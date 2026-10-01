using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ChatTerror.Protocol;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Services;

public enum RelayState { Disconnected, Connecting, Connected, AuthFailed }

public sealed class RelayClient : IDisposable
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly IPluginLog log;
    private readonly Lock gate = new();
    private CancellationTokenSource? cts;
    private Task? loop;
    private Channel<string>? outgoing;
    private bool disposed;

    public RelayClient(IPluginLog log)
    {
        this.log = log;
    }

    public event Action<RelayFrame>? FrameReceived;

    public event Action<RelayState>? StateChanged;

    public RelayState State { get; private set; } = RelayState.Disconnected;

    public void Start(string relayUrl, string token)
    {
        Stop();
        var uri = SocketUri(relayUrl);
        lock (gate)
        {
            if (disposed)
                return;
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            loop = Task.Run(() => Run(uri, token, ct), ct);
        }
    }

    public void Stop()
    {
        Task? running;
        lock (gate)
        {
            cts?.Cancel();
            cts = null;
            running = loop;
            loop = null;
            outgoing = null;
        }

        try
        {
            running?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        SetState(RelayState.Disconnected);
    }

    // Frames are dropped while disconnected; devices catch up with hello and backlog.
    public bool Send(RelayFrame frame)
    {
        var json = ProtocolJson.Serialize(frame);
        if (Encoding.UTF8.GetByteCount(json) > Limits.MaxFrameBytes)
        {
            log.Error($"Dropping a {frame.GetType().Name} that exceeds the relay frame limit.");
            return false;
        }

        Channel<string>? channel;
        lock (gate)
            channel = State == RelayState.Connected ? outgoing : null;
        return channel != null && channel.Writer.TryWrite(json);
    }

    public void Dispose()
    {
        lock (gate)
            disposed = true;
        Stop();
    }

    private static Uri SocketUri(string relayUrl)
    {
        var builder = new UriBuilder(relayUrl.Trim());
        builder.Scheme = builder.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        builder.Path = builder.Path.TrimEnd('/') + "/ws";
        return builder.Uri;
    }

    private async Task Run(Uri uri, string token, CancellationToken ct)
    {
        var backoff = MinBackoff;
        while (!ct.IsCancellationRequested)
        {
            SetState(RelayState.Connecting, ct);
            var authFailed = false;
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(uri, ct);
                await SendText(socket, ProtocolJson.Serialize<RelayFrame>(new AuthFrame(token)), ct);

                var first = await Receive(socket, ct);
                if (first is AuthOkFrame)
                {
                    backoff = MinBackoff;
                    await RunConnected(socket, ct);
                }
                else
                {
                    authFailed = first is AuthFailFrame;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.Warning($"Relay connection error: {ex.Message}");
            }

            if (authFailed)
            {
                log.Warning("Relay rejected the install token.");
                SetState(RelayState.AuthFailed, ct);
                return;
            }

            SetState(RelayState.Disconnected, ct);
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
            try
            {
                await Task.Delay(backoff + jitter, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    private async Task RunConnected(ClientWebSocket socket, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        lock (gate)
        {
            if (ct.IsCancellationRequested)
                return;
            outgoing = channel;
            State = RelayState.Connected;
        }

        StateChanged?.Invoke(RelayState.Connected);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var writer = WriteLoop(socket, channel.Reader, linked.Token);
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var frame = await Receive(socket, ct);
                if (frame == null || ct.IsCancellationRequested)
                    break;
                FrameReceived?.Invoke(frame);
            }
        }
        finally
        {
            lock (gate)
            {
                if (outgoing == channel)
                    outgoing = null;
            }

            channel.Writer.TryComplete();
            linked.Cancel();
            try
            {
                await writer;
            }
            catch (Exception)
            {
            }
        }
    }

    private static async Task WriteLoop(ClientWebSocket socket, ChannelReader<string> reader, CancellationToken ct)
    {
        await foreach (var text in reader.ReadAllAsync(ct))
            await SendText(socket, text, ct);
    }

    private static Task SendText(ClientWebSocket socket, string text, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);

    // Returns null when the socket closed. Undecodable frames are skipped.
    private async Task<RelayFrame?> Receive(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (message.Length + result.Count > Limits.MaxFrameBytes)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, null, ct);
                    return null;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            try
            {
                var frame = ProtocolJson.Deserialize<RelayFrame>(Encoding.UTF8.GetString(message.ToArray()));
                if (frame != null)
                    return frame;
            }
            catch (JsonException ex)
            {
                log.Warning($"Ignoring bad relay frame: {ex.Message}");
            }
        }
    }

    private void SetState(RelayState state, CancellationToken ct = default)
    {
        lock (gate)
        {
            if (ct.IsCancellationRequested || State == state)
                return;
            State = state;
        }

        StateChanged?.Invoke(state);
    }
}
