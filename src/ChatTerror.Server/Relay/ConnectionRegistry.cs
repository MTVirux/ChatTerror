using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using ChatTerror.Protocol;

namespace ChatTerror.Server.Relay;

public sealed class Conn
{
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);

    private readonly WebSocket socket;
    private readonly Channel<string> outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(1024) { SingleReader = true });
    private readonly CancellationTokenSource aborted = new();
    private readonly Lock closeLock = new();
    private (WebSocketCloseStatus Status, string Reason)? close;

    public Conn(WebSocket socket)
    {
        this.socket = socket;
        Writer = Task.Run(WriteLoopAsync);
    }

    public string Role { get; set; } = "";

    public string Id { get; set; } = "";

    public string InstallId { get; set; } = "";

    public bool IsAuthenticated => Role != "";

    public bool IsClosing => close != null;

    public Task Writer { get; }

    // Cancelled when the socket is dead or the peer ignored our close for too long.
    public CancellationToken Aborted => aborted.Token;

    public void Send(RelayFrame frame)
    {
        if (!outbox.Writer.TryWrite(ProtocolJson.Serialize(frame)))
            Close(WebSocketCloseStatus.PolicyViolation, "Too slow");
    }

    // Queued frames are still delivered before the close frame.
    public void Close(WebSocketCloseStatus status, string reason)
    {
        lock (closeLock)
        {
            if (close != null)
                return;
            close = (status, reason);
        }
        outbox.Writer.TryComplete();
        aborted.CancelAfter(CloseGrace);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var text in outbox.Reader.ReadAllAsync(aborted.Token))
                await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, aborted.Token);

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(close!.Value.Status, close.Value.Reason, aborted.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            aborted.Cancel();
        }
    }
}

public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, Conn> plugins = new();
    private readonly ConcurrentDictionary<string, Conn> devices = new();

    public Conn? Plugin(string installId) => plugins.GetValueOrDefault(installId);

    public Conn? Device(string deviceId) => devices.GetValueOrDefault(deviceId);

    public IEnumerable<Conn> DevicesOf(string installId) => devices.Values.Where(conn => conn.InstallId == installId);

    public void Add(Conn conn)
    {
        Conn? replaced = null;
        MapFor(conn).AddOrUpdate(conn.Id, conn, (_, existing) =>
        {
            replaced = existing;
            return conn;
        });
        replaced?.Close(WebSocketCloseStatus.PolicyViolation, "Replaced by a newer connection");
    }

    // False when a newer connection already took this id.
    public bool Remove(Conn conn) => MapFor(conn).TryRemove(KeyValuePair.Create(conn.Id, conn));

    public void Revoke(string deviceId, string installId, bool notifyPlugin)
    {
        if (Device(deviceId) is { } device)
        {
            device.Send(new RevokedFrame());
            device.Close(WebSocketCloseStatus.NormalClosure, "Revoked");
        }
        if (notifyPlugin)
            Plugin(installId)?.Send(new DeviceRevokedFrame(deviceId));
    }

    private ConcurrentDictionary<string, Conn> MapFor(Conn conn) => conn.Role == RelayRoles.Plugin ? plugins : devices;
}
