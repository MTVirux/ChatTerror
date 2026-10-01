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
}
