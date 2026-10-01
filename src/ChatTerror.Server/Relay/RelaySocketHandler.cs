using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Data;
using ChatTerror.Server.Push;
using Microsoft.Extensions.Options;

namespace ChatTerror.Server.Relay;

public sealed class RelaySocketHandler(
    RelayStore store,
    ConnectionRegistry registry,
    IPushSender push,
    IOptions<RelayOptions> options,
    TimeProvider time,
    ILogger<RelaySocketHandler> log)
{
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(30);

    private enum Kind
    {
        Text,
        Binary,
        TooLarge,
        Closed,
    }

    private readonly record struct Incoming(Kind Kind, string Text = "");

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var conn = new Conn(socket);
        try
        {
            await ReceiveLoopAsync(conn, socket);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        finally
        {
            if (conn.IsAuthenticated && registry.Remove(conn))
                OnDisconnected(conn);
            conn.Close(WebSocketCloseStatus.NormalClosure, "");
            await conn.Writer;
        }
    }

    private async Task ReceiveLoopAsync(Conn conn, WebSocket socket)
    {
        var relay = options.Value;
        var bucket = new TokenBucket(relay.FramesPerSecond, relay.FrameBurst, time);
        var buffer = new byte[Limits.MaxFrameBytes + 1];
        var authDeadline = Task.Delay(relay.AuthTimeout, time);

        while (true)
        {
            var receive = ReceiveAsync(socket, buffer, conn.Aborted);
            if (!conn.IsAuthenticated && !conn.IsClosing && await Task.WhenAny(receive, authDeadline) == authDeadline)
                conn.Close(WebSocketCloseStatus.PolicyViolation, "Auth timeout");

            var message = await receive;
            if (message.Kind == Kind.Closed)
                return;
            if (conn.IsClosing)
                continue;

            if (message.Kind == Kind.TooLarge)
            {
                conn.Send(new ErrorFrame(RelayErrors.TooLarge));
                conn.Close(WebSocketCloseStatus.MessageTooBig, "Frame too large");
            }
            else if (!conn.IsAuthenticated)
            {
                Authenticate(conn, message);
            }
            else if (!bucket.TryTake())
            {
                conn.Send(new ErrorFrame(RelayErrors.RateLimited));
            }
            else if (Parse(message) is not { } frame)
            {
                conn.Send(new ErrorFrame(RelayErrors.BadFrame));
            }
            else if (conn.Role == RelayRoles.Plugin)
            {
                HandlePluginFrame(conn, frame);
            }
            else
            {
                HandleDeviceFrame(conn, frame);
            }
        }
    }

    private static async Task<Incoming> ReceiveAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        var length = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(length), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return new Incoming(Kind.Closed);

            length += result.Count;
            if (length > Limits.MaxFrameBytes)
                return new Incoming(Kind.TooLarge);
            if (result.EndOfMessage)
            {
                return result.MessageType == WebSocketMessageType.Text
                    ? new Incoming(Kind.Text, Encoding.UTF8.GetString(buffer, 0, length))
                    : new Incoming(Kind.Binary);
            }
        }
    }

    private static RelayFrame? Parse(Incoming message)
    {
        if (message.Kind != Kind.Text)
            return null;
        try
        {
            return ProtocolJson.Deserialize<RelayFrame>(message.Text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Authenticate(Conn conn, Incoming message)
    {
        var token = Parse(message) is AuthFrame auth ? auth.Token : "";
        if (store.FindInstallByToken(token) is { } install)
        {
            conn.Role = RelayRoles.Plugin;
            conn.Id = install.Id;
            conn.InstallId = install.Id;
            conn.Send(new AuthOkFrame(conn.Role, conn.Id));
            registry.Add(conn);
            OnPluginConnected(conn);
        }
        else if (store.FindDeviceByToken(token) is { } device)
        {
            conn.Role = RelayRoles.Device;
            conn.Id = device.Id;
            conn.InstallId = device.InstallId;
            conn.Send(new AuthOkFrame(conn.Role, conn.Id));
            registry.Add(conn);
            OnDeviceConnected(conn, device);
        }
        else
        {
            conn.Send(new AuthFailFrame());
            conn.Close(WebSocketCloseStatus.PolicyViolation, "Auth failed");
        }
    }

    private void OnPluginConnected(Conn plugin)
    {
        foreach (var device in registry.DevicesOf(plugin.InstallId))
            device.Send(new PluginStatusFrame(true));

        foreach (var device in store.ListDevices(plugin.InstallId))
        {
            if (device.Status == DeviceStatus.Pending)
                plugin.Send(new PairRequestFrame(device.Id, device.Name, device.PublicKey));
            else if (registry.Device(device.Id) != null)
                plugin.Send(new DeviceOnlineFrame(device.Id));
        }
    }

    private void OnDeviceConnected(Conn conn, DeviceRecord device)
    {
        store.TouchLastSeen(device.Id);
        var plugin = registry.Plugin(device.InstallId);
        conn.Send(new PluginStatusFrame(plugin != null));
        if (device.Status == DeviceStatus.Active)
            plugin?.Send(new DeviceOnlineFrame(device.Id));
    }

    private void OnDisconnected(Conn conn)
    {
        if (conn.Role == RelayRoles.Plugin)
        {
            foreach (var device in registry.DevicesOf(conn.InstallId))
                device.Send(new PluginStatusFrame(false));
            return;
        }

        store.TouchLastSeen(conn.Id);
        registry.Plugin(conn.InstallId)?.Send(new DeviceOfflineFrame(conn.Id));
    }

    private void HandlePluginFrame(Conn plugin, RelayFrame frame)
    {
        switch (frame)
        {
            case SendFrame { To: { } to } send:
                var target = OwnDevice(plugin, to);
                if (target == null)
                    plugin.Send(new ErrorFrame(RelayErrors.UnknownDevice));
                else if (target.Status != DeviceStatus.Active)
                    plugin.Send(new ErrorFrame(RelayErrors.NotApproved));
                else if (registry.Device(to) is { } online)
                    online.Send(new MsgFrame(RelayRoles.Plugin, send.Payload));
                else if (send.Notify && target.Push is { } subscription)
                    _ = PushAsync(target.Id, subscription, send.Payload);
                break;

            case PairDecisionFrame decision:
                var device = OwnDevice(plugin, decision.DeviceId);
                if (device == null)
                {
                    plugin.Send(new ErrorFrame(RelayErrors.UnknownDevice));
                }
                else if (decision.Approved)
                {
                    store.SetDeviceStatus(device.Id, DeviceStatus.Active);
                    if (registry.Device(device.Id) is { } online)
                    {
                        online.Send(new PairedFrame());
                        plugin.Send(new DeviceOnlineFrame(device.Id));
                    }
                }
                else
                {
                    store.DeleteDevice(device.Id);
                    registry.Revoke(device.Id, device.InstallId, notifyPlugin: false);
                }
                break;

            default:
                plugin.Send(new ErrorFrame(RelayErrors.BadFrame));
                break;
        }
    }

    private void HandleDeviceFrame(Conn conn, RelayFrame frame)
    {
        if (frame is not SendFrame send)
        {
            conn.Send(new ErrorFrame(RelayErrors.BadFrame));
            return;
        }

        var device = store.FindDevice(conn.Id);
        if (device == null)
        {
            conn.Send(new RevokedFrame());
            conn.Close(WebSocketCloseStatus.NormalClosure, "Revoked");
        }
        else if (device.Status != DeviceStatus.Active)
        {
            conn.Send(new ErrorFrame(RelayErrors.NotApproved));
        }
        else
        {
            registry.Plugin(conn.InstallId)?.Send(new MsgFrame(conn.Id, send.Payload));
        }
    }

    private DeviceRecord? OwnDevice(Conn plugin, string deviceId) =>
        store.FindDevice(deviceId) is { } device && device.InstallId == plugin.InstallId ? device : null;

    private async Task PushAsync(string deviceId, PushSubscriptionRecord subscription, string payload)
    {
        try
        {
            using var timeout = new CancellationTokenSource(PushTimeout);
            var body = JsonSerializer.Serialize(new { p = payload });
            if (await push.SendAsync(subscription, body, timeout.Token) == PushResult.Gone)
                store.ClearPush(deviceId, subscription.Endpoint);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Push to device {DeviceId} failed", deviceId);
        }
    }
}
