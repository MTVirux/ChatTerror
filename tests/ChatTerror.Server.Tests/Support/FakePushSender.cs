using System.Collections.Concurrent;
using ChatTerror.Server.Push;

namespace ChatTerror.Server.Tests.Support;

public sealed class FakePushSender : IPushSender
{
    private readonly SemaphoreSlim signal = new(0);

    public ConcurrentQueue<(PushSubscriptionRecord Subscription, string Body)> Calls { get; } = new();

    public PushResult Result { get; set; } = PushResult.Ok;

    public Task<PushResult> SendAsync(PushSubscriptionRecord sub, string body, CancellationToken ct)
    {
        Calls.Enqueue((sub, body));
        signal.Release();
        return Task.FromResult(Result);
    }

    public async Task WaitForCallAsync()
    {
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)), "Expected a push call.");
    }
}
