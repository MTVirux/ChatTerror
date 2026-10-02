using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Api;
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
    public const int PreAuthMaxBytes = 4096;
    // Push services cap the encrypted record at 4096 bytes, which leaves 3993 for the body.
    public const int MaxPushBodyBytes = 3993;

    private readonly PushLimiter pushLimiter = new(options.Value.MaxConcurrentPushes, options.Value.MaxPushesPerInstall);
    private readonly SocketLimiter socketLimiter = new(options.Value.MaxSocketsPerClient);
    private readonly string[] pushServiceHosts = options.Value.GetPushServiceHosts();

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

        var client = RequestLimits.PartitionKey(context.Connection.RemoteIpAddress);
        if (!socketLimiter.TryAcquire(client))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        try
        {
            await RunAsync(context);
        }
        finally
        {
            socketLimiter.Release(client);
        }
    }

    private async Task RunAsync(HttpContext context)
    {
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
        var authDeadline = Task.Delay(relay.AuthTimeout, time);
        var smallBuffer = new byte[PreAuthMaxBytes + 1];

        while (true)
        {
            var maxBytes = conn.IsAuthenticated ? Limits.MaxFrameBytes : PreAuthMaxBytes;
            var receive = ReceiveAsync(socket, smallBuffer, maxBytes, conn.Aborted);
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
            else
            {
                HandleFrame(conn, bucket, message);
            }
        }
    }

    private void HandleFrame(Conn conn, TokenBucket bucket, Incoming message)
    {
        if (conn.Role == RelayRoles.Plugin && conn.BudgetStale)
        {
            conn.BudgetStale = false;
            bucket.SetScale(Math.Max(1, store.CountActiveDevices(conn.InstallId)));
        }

        if (!bucket.TryTake())
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

    // Reads into the small buffer and only rents a full-size one for messages that outgrow it.
    // Reading up to maxBytes + 1 detects an oversize frame without reading it all.
    private static async Task<Incoming> ReceiveAsync(WebSocket socket, byte[] smallBuffer, int maxBytes, CancellationToken ct)
    {
        var buffer = smallBuffer;
        byte[]? rented = null;
        var length = 0;
        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    rented = ArrayPool<byte>.Shared.Rent(maxBytes + 1);
                    smallBuffer.CopyTo(rented, 0);
                    buffer = rented;
                }

                var limit = Math.Min(buffer.Length, maxBytes + 1);
                var result = await socket.ReceiveAsync(buffer.AsMemory(length, limit - length), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return new Incoming(Kind.Closed);

                length += result.Count;
                if (length > maxBytes)
                    return new Incoming(Kind.TooLarge);
                if (result.EndOfMessage)
                {
                    return result.MessageType == WebSocketMessageType.Text
                        ? new Incoming(Kind.Text, Encoding.UTF8.GetString(buffer, 0, length))
                        : new Incoming(Kind.Binary);
                }
            }
        }
        finally
        {
            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
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
            store.TouchInstall(install.Id);
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
            store.TouchInstall(conn.InstallId);
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
                else if (Encode(new MsgFrame(RelayRoles.Plugin, send.Payload)) is not { } msg)
                    plugin.Send(new ErrorFrame(RelayErrors.TooLarge));
                else if (registry.Device(to) is { } online)
                    online.SendEncoded(msg);
                else if (send.Notify && target.Push is { } subscription)
                    _ = PushAsync(target, subscription, send.Payload);
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
                    plugin.BudgetStale = true;
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
        else if (Encode(new MsgFrame(conn.Id, send.Payload)) is not { } msg)
        {
            conn.Send(new ErrorFrame(RelayErrors.TooLarge));
        }
        else
        {
            registry.Plugin(conn.InstallId)?.SendEncoded(msg);
        }
    }

    // Escaping can make the relayed frame larger than the one received, so the cap is checked on the output.
    private static byte[]? Encode(RelayFrame frame)
    {
        var bytes = Conn.Encode(frame);
        return bytes.Length <= Limits.MaxFrameBytes ? bytes : null;
    }

    private DeviceRecord? OwnDevice(Conn plugin, string deviceId) =>
        store.FindDevice(deviceId) is { } device && device.InstallId == plugin.InstallId ? device : null;

    private async Task PushAsync(DeviceRecord device, PushSubscriptionRecord subscription, string payload)
    {
        var body = JsonSerializer.Serialize(new { p = payload, d = device.Id });
        var bodyBytes = Encoding.UTF8.GetByteCount(body);
        if (bodyBytes > MaxPushBodyBytes)
        {
            log.LogWarning("Skipped push to device {DeviceId} of install {InstallId}: body is {Length} bytes", device.Id, device.InstallId, bodyBytes);
            return;
        }
        if (!PushEndpointGuard.IsPushService(subscription.Endpoint, pushServiceHosts))
        {
            log.LogWarning("Skipped push to device {DeviceId} of install {InstallId}: endpoint is not a known push service", device.Id, device.InstallId);
            return;
        }
        if (!pushLimiter.TryAcquire(device.InstallId))
        {
            log.LogWarning("Skipped push to device {DeviceId} of install {InstallId}: too many pushes in flight", device.Id, device.InstallId);
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(options.Value.PushTimeout);
            if (await push.SendAsync(subscription, body, timeout.Token) == PushResult.Gone)
                store.ClearPush(device.Id, subscription.Endpoint);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Push to device {DeviceId} of install {InstallId} failed", device.Id, device.InstallId);
        }
        finally
        {
            pushLimiter.Release(device.InstallId);
        }
    }
}
