using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using ChatTerror.Server.Tests.Support;

namespace ChatTerror.Server.Tests;

public class EndToEndTests
{
    private const string Character = "Alpha Beta";

    private sealed class FakeGate : IGameGate
    {
        public bool IsLoggedIn => true;
        public bool IsBusy => false;
        public string? Sanitize(string line) => line;
    }

    // The phone side of the protocol, as the PWA implements it.
    private sealed class Phone(byte[] key)
    {
        private readonly SeqCounter counter = new();
        private readonly SeqGuard guard = new();

        public string Seal(Payload p) => Payloads.SealPayload(key, Direction.DeviceToPlugin, p with { Seq = counter.Next() });

        public T Open<T>(string envelope) where T : Payload
        {
            var payload = Payloads.OpenPayload(key, Direction.PluginToDevice, envelope);
            Assert.True(guard.Accept(payload.Seq));
            return Assert.IsType<T>(payload);
        }
    }

    private static ChatItem Item(long ts, ChatChannel channel, string text) =>
        new(Guid.NewGuid().ToString("N"), ts, channel, "Y'shtola Rhul", "Twintania", text, Character, false);

    [Fact]
    public async Task PairChatPushAndSend()
    {
        using var app = new RelayApp();
        var settings = new RelaySettings();
        var history = new MessageHistory(settings.HistorySize);

        using var pluginKey = P256.Generate();
        var pluginPub = P256.PublicRaw(pluginKey);
        var installResponse = await app.Client().PostAsJsonAsync("/api/installs", new { publicKey = Base64Url.Encode(pluginPub) });
        var install = (await installResponse.Content.ReadFromJsonAsync<InstallResponse>())!;
        await using var plugin = await app.ConnectAsync(install.InstallToken);

        // Pairing: the phone scans the code, both sides derive the same key and show the same fingerprint.
        var code = await app.CreatePairingAsync(install.InstallToken);
        var info = (await app.Client().GetFromJsonAsync<PairingInfo>($"/api/pairings/{code}"))!;
        Assert.Equal(install.InstallId, info.InstallId);

        using var deviceKey = P256.Generate();
        var devicePub = P256.PublicRaw(deviceKey);
        var phoneShared = E2eCrypto.DeriveKey(deviceKey, Base64Url.Decode(info.PluginPublicKey), Base64Url.Decode(info.PluginPublicKey), devicePub);
        var claimResponse = await app.Client().PostAsJsonAsync($"/api/pairings/{code}/claim",
            new { devicePublicKey = Base64Url.Encode(devicePub), deviceName = "Phone" });
        var claim = (await claimResponse.Content.ReadFromJsonAsync<ClaimResponse>())!;

        var request = await plugin.ReceiveAsync<PairRequestFrame>();
        Assert.Equal(claim.DeviceId, request.DeviceId);
        var requestPub = Base64Url.Decode(request.DevicePublicKey);
        var pluginShared = E2eCrypto.DeriveKey(pluginKey, requestPub, pluginPub, requestPub);
        var secret = PairingSecret.Generate();
        Assert.Equal(Fingerprint.Compute(secret, Base64Url.Decode(info.PluginPublicKey), devicePub), Fingerprint.Compute(secret, pluginPub, requestPub));

        var session = new DeviceSession(claim.DeviceId, pluginShared, []);
        var phone = new Phone(phoneShared);

        var earlier = Item(1_000, ChatChannel.FreeCompany, "before the phone connected");
        history.Add(earlier);

        await using (var socket = await app.ConnectAsync(claim.DeviceToken))
        {
            Assert.True((await socket.ReceiveAsync<PluginStatusFrame>()).Online);
            await plugin.SendAsync(new PairDecisionFrame(claim.DeviceId, true));
            await socket.ReceiveAsync<PairedFrame>();

            // hello: the plugin answers with settings, then the backlog newer than sinceTs.
            await socket.SendAsync(new SendFrame(Payload: phone.Seal(new HelloPayload(0))));
            var helloFrame = await plugin.ReceiveAsync<MsgFrame>();
            Assert.Equal(claim.DeviceId, helloFrame.From);
            var hello = Assert.IsType<HelloPayload>(session.Open(helloFrame.Payload));

            var sendChannels = settings.Channels.Where(c => c.Value.Send).Select(c => c.Key).ToList();
            var relayChannels = settings.Channels.Where(c => c.Value.Relay).Select(c => c.Key).ToList();
            await plugin.SendAsync(new SendFrame(claim.DeviceId, session.Seal(new SettingsPayload(Character, relayChannels, sendChannels, settings.MaxLengthBytes))));
            foreach (var chunk in DeviceSession.Backlog(history.Since(hello.SinceTs)))
                await plugin.SendAsync(new SendFrame(claim.DeviceId, session.Seal(chunk)));

            var received = phone.Open<SettingsPayload>((await socket.ReceiveAsync<MsgFrame>()).Payload);
            Assert.Equal(Character, received.Character);
            Assert.Contains(ChatChannel.Tell, received.SendChannels);
            var backlog = phone.Open<BacklogPayload>((await socket.ReceiveAsync<MsgFrame>()).Payload);
            Assert.True(backlog.Done);
            Assert.Equal(earlier, Assert.Single(backlog.Items));

            var subscribe = await app.Client(claim.DeviceToken).PutAsJsonAsync("/api/devices/me/push",
                new { endpoint = "https://1.1.1.1/sub", keys = new { p256dh = "p256", auth = "secret" } });
            Assert.Equal(HttpStatusCode.NoContent, subscribe.StatusCode);
        }
        await plugin.ReceiveAsync<DeviceOfflineFrame>();

        // A tell arrives while the phone is closed, so the relay pushes it.
        var tell = Item(2_000, ChatChannel.Tell, "are you around?");
        var filter = ChatFilter.Evaluate(new IncomingChat(tell.Channel, tell.Sender, tell.SenderWorld, tell.Text, false, tell.Ts), settings, Character, new TimeOnly(12, 0));
        Assert.True(filter.Relay);
        history.Add(tell);
        var notify = session.ShouldNotify(tell, filter.Notify);
        Assert.True(notify);
        await plugin.SendAsync(new SendFrame(claim.DeviceId, session.Seal(new ChatPayload(tell)), notify));

        await app.Push.WaitForCallAsync();
        Assert.True(app.Push.Calls.TryDequeue(out var push));
        var pushJson = JsonDocument.Parse(push.Body).RootElement;
        var pushBody = pushJson.GetProperty("p").GetString()!;
        Assert.Equal(claim.DeviceId, pushJson.GetProperty("d").GetString());
        Assert.Equal(tell, phone.Open<ChatPayload>(pushBody).Item);

        // The phone answers the tell; the plugin turns it into a single chat line.
        var queue = new SendQueue(() => settings, new FakeGate(), () => 100_000);
        await using (var socket = await app.ConnectAsync(claim.DeviceToken))
        {
            await plugin.ReceiveAsync<DeviceOnlineFrame>();
            var sendChat = new SendChatPayload("req-1", ChatChannel.Tell, "Y'shtola Rhul@Twintania", "on my way");
            await socket.SendAsync(new SendFrame(Payload: phone.Seal(sendChat)));

            var frame = await plugin.ReceiveAsync<MsgFrame>();
            var opened = Assert.IsType<SendChatPayload>(session.Open(frame.Payload));
            Assert.Null(session.Open(frame.Payload));
            queue.Enqueue(new SendRequest(frame.From, opened));
            var (line, results) = queue.Tick();
            Assert.Equal("/tell Y'shtola Rhul@Twintania on my way", line);

            var (_, result) = Assert.Single(results);
            await plugin.SendAsync(new SendFrame(claim.DeviceId, session.Seal(result)));
            var answer = phone.Open<SendResultPayload>((await socket.ReceiveAsync<MsgFrame>()).Payload);
            Assert.Equal(new SendResultPayload("req-1", true) { Seq = answer.Seq }, answer);
        }
    }

    [Fact]
    public async Task TamperedEnvelope_IsRejectedByThePlugin()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var session = new DeviceSession("dev", key, []);
        var phone = new Phone(key);
        var envelope = Base64Url.Decode(phone.Seal(new HelloPayload(0)));
        envelope[^1] ^= 1;

        Assert.Null(session.Open(Base64Url.Encode(envelope)));
    }
}
