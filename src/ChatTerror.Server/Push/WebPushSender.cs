using System.Net;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace ChatTerror.Server.Push;

public sealed class WebPushSender : IPushSender, IDisposable
{
    private const int TimeToLiveSeconds = 86400;

    private readonly PushServiceClient client;
    private readonly VapidAuthentication authentication;
    private readonly ILogger<WebPushSender> log;

    public WebPushSender(VapidKeys keys, ILogger<WebPushSender> log)
    {
        this.log = log;
        authentication = new VapidAuthentication(keys.PublicKey, keys.PrivateKey);
        if (keys.Subject != "")
            authentication.Subject = keys.Subject;
        client = new PushServiceClient { DefaultAuthentication = authentication };
    }

    public async Task<PushResult> SendAsync(PushSubscriptionRecord sub, string body, CancellationToken ct)
    {
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

    public void Dispose() => authentication.Dispose();
}
