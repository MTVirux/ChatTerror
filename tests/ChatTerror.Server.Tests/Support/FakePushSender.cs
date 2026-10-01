using System.Collections.Concurrent;
using ChatTerror.Server.Push;

namespace ChatTerror.Server.Tests.Support;

public sealed class FakePushSender : IPushSender
{
    private readonly SemaphoreSlim signal = new(0);

    public ConcurrentQueue<(PushSubscriptionRecord Subscription, string Body)> Calls { get; } = new();

    public PushResult Result { get; set; } = PushResult.Ok;

    // Lets a test keep chosen pushes in flight.
    public Func<PushSubscriptionRecord, Task> Hold { get; set; } = _ => Task.CompletedTask;

    public async Task<PushResult> SendAsync(PushSubscriptionRecord sub, string body, CancellationToken ct)
    {
        Calls.Enqueue((sub, body));
        signal.Release();
        await Hold(sub);
        return Result;
    }

    public async Task WaitForCallAsync()
    {
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)), "Expected a push call.");
    }
}
