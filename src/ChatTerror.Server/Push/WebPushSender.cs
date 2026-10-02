using System.Net;
using System.Net.Sockets;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;

namespace ChatTerror.Server.Push;

public sealed class WebPushSender : IPushSender, IDisposable
{
    private const int TimeToLiveSeconds = 86400;

    private readonly PushServiceClient client;
    private readonly VapidAuthentication authentication;
    private readonly ILogger<WebPushSender> log;
    private readonly string[] serviceHosts;

    public WebPushSender(VapidKeys keys, IOptions<RelayOptions> options, ILogger<WebPushSender> log)
        : this(keys, options, log, new HttpClient(CreateHandler()))
    {
    }

    // Redirects and DNS answers that change after the endpoint check could otherwise reach internal hosts.
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = ConnectToPublicAddressAsync,
    };

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var address = PushEndpointGuard.PickAddress(await PushEndpointGuard.ResolveAsync(host, ct))
            ?? throw new HttpRequestException($"Push host {host} does not resolve to a public address.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(address, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal WebPushSender(VapidKeys keys, IOptions<RelayOptions> options, ILogger<WebPushSender> log, HttpClient http)
    {
        this.log = log;
        serviceHosts = options.Value.GetPushServiceHosts();
        authentication = new VapidAuthentication(keys.PublicKey, keys.PrivateKey);
        if (keys.Subject != "")
            authentication.Subject = keys.Subject;
        client = new PushServiceClient(http) { DefaultAuthentication = authentication };
    }

    public async Task<PushResult> SendAsync(PushSubscriptionRecord sub, string body, CancellationToken ct)
    {
        if (!Uri.TryCreate(sub.Endpoint, UriKind.Absolute, out var endpoint) || !await IsAllowedAsync(endpoint, ct))
        {
            log.LogWarning("Push endpoint {Host} is not a known push service on a public address, skipped", endpoint?.Host);
            return PushResult.Failed;
        }

        var subscription = new PushSubscription { Endpoint = sub.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, sub.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, sub.Auth);
        var message = new PushMessage(body) { TimeToLive = TimeToLiveSeconds, Urgency = PushMessageUrgency.High };

        try
        {
            await client.RequestPushMessageDeliveryAsync(subscription, message, ct);
            return PushResult.Ok;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushResult.Gone;
        }
        catch (Exception ex) when (ex is PushServiceClientException or HttpRequestException or OperationCanceledException or ArgumentException)
        {
            log.LogWarning(ex, "Push delivery failed");
            return PushResult.Failed;
        }
    }

    private async Task<bool> IsAllowedAsync(Uri endpoint, CancellationToken ct)
    {
        try
        {
            return await PushEndpointGuard.IsAllowedAsync(endpoint, serviceHosts, ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose() => authentication.Dispose();
}
