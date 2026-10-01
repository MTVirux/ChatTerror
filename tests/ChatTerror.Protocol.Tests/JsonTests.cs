using System.Security.Cryptography;
using System.Text.Json;

namespace ChatTerror.Protocol.Tests;

public class JsonTests
{
    private static ChatItem SampleItem() =>
        new("0123456789abcdef0123456789abcdef", 1700000000000, ChatChannel.FreeCompany, "Y'shtola Rhul", "Twintania", "hello", "Alpha Beta", false);

    [Fact]
    public void Payload_Json_UsesDiscriminatorAndCamelCase()
    {
        Payload payload = new ChatPayload(SampleItem()) { Seq = 42 };

        var json = ProtocolJson.Serialize(payload);

        Assert.Contains("\"type\":\"chat\"", json);
        Assert.Contains("\"channel\":\"freeCompany\"", json);
        Assert.Contains("\"seq\":42", json);
        Assert.Contains("\"senderWorld\":\"Twintania\"", json);
    }

    [Fact]
    public void Payload_Json_OmitsNulls_AndMapsLinkshellNames()
    {
        Payload payload = new SendChatPayload("r1", ChatChannel.CrossLinkshell3, null, "hi") { Seq = 1 };

        var json = ProtocolJson.Serialize(payload);

        Assert.Contains("\"channel\":\"crossLinkshell3\"", json);
        Assert.DoesNotContain("target", json);
    }

    [Fact]
    public void Payload_Json_RoundTrips_AllTypes()
    {
        var payloads = new Payload[]
        {
            new ChatPayload(SampleItem()) { Seq = 1 },
            new BacklogPayload(new[] { SampleItem() }, true) { Seq = 2 },
            new SendResultPayload("r1", false, SendErrors.TooLong) { Seq = 3 },
            new SettingsPayload("Alpha Beta", new[] { ChatChannel.Tell }, new[] { ChatChannel.Party, ChatChannel.Linkshell8 }, 400) { Seq = 4 },
            new HelloPayload(123) { Seq = 5 },
            new SendChatPayload("r2", ChatChannel.Tell, "Y'shtola Rhul@Twintania", "hi") { Seq = 6 },
            new PrefsPayload(new[] { ChatChannel.Say, ChatChannel.Yell }) { Seq = 7 },
        };

        foreach (var payload in payloads)
        {
            var json = ProtocolJson.Serialize(payload);
            var back = ProtocolJson.Deserialize<Payload>(json);

            Assert.NotNull(back);
            Assert.Equal(payload.GetType(), back.GetType());
            Assert.Equal(payload.Seq, back.Seq);
            Assert.Equal(json, ProtocolJson.Serialize(back));
        }
    }

    [Fact]
    public void Payload_Json_AcceptsDiscriminatorNotFirst()
    {
        var back = ProtocolJson.Deserialize<Payload>("{\"seq\":9,\"sinceTs\":5,\"type\":\"hello\"}");

        var hello = Assert.IsType<HelloPayload>(back);
        Assert.Equal(9, hello.Seq);
        Assert.Equal(5, hello.SinceTs);
    }

    [Fact]
    public void RelayFrame_Json_RoundTrips_AllTypes()
    {
        var frames = new (RelayFrame Frame, string Tag)[]
        {
            (new AuthFrame("d.abc.def"), "auth"),
            (new SendFrame("dev1", "payload", true), "send"),
            (new PairDecisionFrame("dev1", true), "pairDecision"),
            (new AuthOkFrame("plugin", "inst1"), "authOk"),
            (new AuthFailFrame(), "authFail"),
            (new MsgFrame("plugin", "payload"), "msg"),
            (new DeviceOnlineFrame("dev1"), "deviceOnline"),
            (new DeviceOfflineFrame("dev1"), "deviceOffline"),
            (new PairRequestFrame("dev1", "Phone", "BPub"), "pairRequest"),
            (new DeviceRevokedFrame("dev1"), "deviceRevoked"),
            (new PluginStatusFrame(true), "pluginStatus"),
            (new PairedFrame(), "paired"),
            (new RevokedFrame(), "revoked"),
            (new ErrorFrame(RelayErrors.TooLarge), "error"),
        };

        foreach (var (frame, tag) in frames)
        {
            var json = ProtocolJson.Serialize(frame);
            var back = ProtocolJson.Deserialize<RelayFrame>(json);

            Assert.StartsWith($"{{\"t\":\"{tag}\"", json);
            Assert.Equal(frame, back);
        }
    }

    [Fact]
    public void RelayFrame_DeviceSend_ParsesWithoutToAndNotify()
    {
        var back = ProtocolJson.Deserialize<RelayFrame>("{\"t\":\"send\",\"payload\":\"abc\"}");

        Assert.Equal(new SendFrame(null, "abc", false), back);
    }

    [Fact]
    public void Payloads_SealOpen_RoundTrip()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var sealedText = Payloads.SealPayload(key, Direction.DeviceToPlugin, new HelloPayload(77) { Seq = 3 });

        var opened = Payloads.OpenPayload(key, Direction.DeviceToPlugin, sealedText);

        var hello = Assert.IsType<HelloPayload>(opened);
        Assert.Equal(77, hello.SinceTs);
        Assert.Equal(3, hello.Seq);
        Assert.ThrowsAny<CryptographicException>(() => Payloads.OpenPayload(key, Direction.PluginToDevice, sealedText));
    }

    [Theory]
    [InlineData("{\"type\":\"chat\",\"seq\":1}")]
    [InlineData("{\"type\":\"chat\",\"seq\":1,\"item\":{\"id\":\"a\",\"ts\":1,\"channel\":\"say\",\"sender\":null,\"text\":\"x\",\"character\":\"c\",\"outgoing\":false}}")]
    [InlineData("{\"type\":\"sendResult\",\"seq\":1,\"ok\":true}")]
    public void Payload_Json_MissingOrNullRequiredField_Throws(string json)
    {
        Assert.Throws<JsonException>(() => ProtocolJson.Deserialize<Payload>(json));
    }

    [Fact]
    public void RelayFrame_Json_MissingRequiredField_Throws()
    {
        Assert.Throws<JsonException>(() => ProtocolJson.Deserialize<RelayFrame>("{\"t\":\"send\"}"));
    }

    [Theory]
    [InlineData("{\"seq\":1}")]
    [InlineData("{\"type\":\"nope\",\"seq\":1}")]
    public void Payload_Json_MissingOrUnknownDiscriminator_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => ProtocolJson.Deserialize<Payload>(json));
    }

    [Fact]
    public void Payload_Json_OptionalFieldsMayBeMissing()
    {
        var result = ProtocolJson.Deserialize<Payload>("{\"type\":\"sendResult\",\"seq\":1,\"requestId\":\"r\",\"ok\":true}");
        var chat = ProtocolJson.Deserialize<Payload>("{\"type\":\"chat\",\"seq\":1,\"item\":{\"id\":\"a\",\"ts\":1,\"channel\":\"say\",\"sender\":\"s\",\"text\":\"x\",\"character\":\"c\",\"outgoing\":false}}");
        var settings = ProtocolJson.Deserialize<Payload>("{\"type\":\"settings\",\"seq\":1,\"relayChannels\":[],\"sendChannels\":[],\"maxLength\":500}");
        var send = ProtocolJson.Deserialize<Payload>("{\"type\":\"sendChat\",\"seq\":1,\"requestId\":\"r\",\"channel\":\"say\",\"text\":\"x\"}");

        Assert.Null(Assert.IsType<SendResultPayload>(result).Error);
        Assert.Null(Assert.IsType<ChatPayload>(chat).Item.SenderWorld);
        Assert.Null(Assert.IsType<SettingsPayload>(settings).Character);
        Assert.Null(Assert.IsType<SendChatPayload>(send).Target);
    }

    [Theory]
    [InlineData("{\"seq\":1}")]
    [InlineData("null")]
    [InlineData("not json")]
    public void Payloads_OpenPayload_BadPlaintext_ThrowsJsonException(string plaintext)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var envelope = Base64Url.Encode(E2eCrypto.Seal(key, Direction.PluginToDevice, System.Text.Encoding.UTF8.GetBytes(plaintext)));

        Assert.Throws<JsonException>(() => Payloads.OpenPayload(key, Direction.PluginToDevice, envelope));
    }
}
