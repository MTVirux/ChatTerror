using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using ChatTerror.Protocol;
using ChatTerror.Server.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChatTerror.Server.Tests;

public class PushSenderTests
{
    [Fact]
    public void GeneratedVapidKeys_ArePersisted_AndUsableByWebPushSender()
    {
        var directory = Path.Combine(Path.GetTempPath(), "chatterror-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new RelayOptions { DbPath = Path.Combine(directory, "relay.db") });

            var first = new VapidKeys(options, NullLogger<VapidKeys>.Instance);
            var second = new VapidKeys(options, NullLogger<VapidKeys>.Instance);
            using var sender = new WebPushSender(first, NullLogger<WebPushSender>.Instance);

            Assert.True(File.Exists(Path.Combine(directory, "vapid.json")));
            Assert.Equal(first.PublicKey, second.PublicKey);
            Assert.Equal(first.PrivateKey, second.PrivateKey);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
    [Fact]
    public async Task HangingPushService_GivesUpWhenCancelled()
    {
        var result = await SendWithHandlerAsync(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        Assert.Equal(PushResult.Failed, result);
    }

    [Fact]
    public async Task RetryAfter_DoesNotOutliveTheCancellation()
    {
        var result = await SendWithHandlerAsync((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
            return Task.FromResult(response);
        });

        Assert.Equal(PushResult.Failed, result);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task ExpiredSubscription_IsGone(HttpStatusCode status)
    {
        var result = await SendWithHandlerAsync((_, _) => Task.FromResult(new HttpResponseMessage(status)));

        Assert.Equal(PushResult.Gone, result);
    }

    private static async Task<PushResult> SendWithHandlerAsync(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var directory = Path.Combine(Path.GetTempPath(), "chatterror-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new RelayOptions { DbPath = Path.Combine(directory, "relay.db") });
            var keys = new VapidKeys(options, NullLogger<VapidKeys>.Instance);
            using var http = new HttpClient(new StubHandler(respond));
            using var sender = new WebPushSender(keys, NullLogger<WebPushSender>.Instance, http);
            using var deviceKey = P256.Generate();
            var subscription = new PushSubscriptionRecord(
                "https://push.example.test/sub",
                Base64Url.Encode(P256.PublicRaw(deviceKey)),
                Base64Url.Encode(RandomNumberGenerator.GetBytes(16)));

            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var watch = Stopwatch.StartNew();
            var result = await sender.SendAsync(subscription, "{\"p\":\"x\"}", timeout.Token);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Push took {watch.Elapsed}.");
            return result;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }
}
