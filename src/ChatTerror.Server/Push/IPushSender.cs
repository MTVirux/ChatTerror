namespace ChatTerror.Server.Push;

public enum PushResult
{
    Ok,
    Gone,
    Failed,
}

public sealed record PushSubscriptionRecord(string Endpoint, string P256dh, string Auth);

public interface IPushSender
{
    Task<PushResult> SendAsync(PushSubscriptionRecord sub, string body, CancellationToken ct);
}
