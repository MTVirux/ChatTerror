using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class DeviceSessionTests
{
    private readonly byte[] pluginKey;
    private readonly byte[] deviceKey;

    public DeviceSessionTests()
    {
        using var plugin = P256.Generate();
        using var device = P256.Generate();
        var pluginPub = P256.PublicRaw(plugin);
        var devicePub = P256.PublicRaw(device);
        pluginKey = E2eCrypto.DeriveKey(plugin, devicePub, pluginPub, devicePub);
        deviceKey = E2eCrypto.DeriveKey(device, pluginPub, pluginPub, devicePub);
    }

    private static ChatItem Item(long ts, ChatChannel channel = ChatChannel.Say, string text = "hi") =>
        new(Guid.NewGuid().ToString("N"), ts, channel, "Bob Smith", Text: text, Character: "Alex Doe", Outgoing: false);

    private static int JsonBytes(ChatItem item) => System.Text.Encoding.UTF8.GetByteCount(ProtocolJson.Serialize(item));

    [Fact]
    public void Seal_DeviceCanOpen_WithIncreasingSeq()
    {
        var session = new DeviceSession("dev1", pluginKey, []);

        var first = Payloads.OpenPayload(deviceKey, Direction.PluginToDevice, session.Seal(new ChatPayload(Item(1))));
        var second = Payloads.OpenPayload(deviceKey, Direction.PluginToDevice, session.Seal(new SendResultPayload("r1", true)));

        Assert.IsType<ChatPayload>(first);
        Assert.True(first.Seq > 0);
        Assert.True(second.Seq > first.Seq);
        Assert.Equal("dev1", session.DeviceId);
    }

    [Fact]
    public void Open_DevicePayload()
    {
        var session = new DeviceSession("dev1", pluginKey, []);
        var envelope = Payloads.SealPayload(deviceKey, Direction.DeviceToPlugin, new HelloPayload(42) { Seq = 10 });

        var opened = Assert.IsType<HelloPayload>(session.Open(envelope));

        Assert.Equal(42, opened.SinceTs);
    }

    [Fact]
    public void Open_Replay_Rejected()
    {
        var session = new DeviceSession("dev1", pluginKey, []);
        var envelope = Payloads.SealPayload(deviceKey, Direction.DeviceToPlugin, new HelloPayload(0) { Seq = 10 });
        var older = Payloads.SealPayload(deviceKey, Direction.DeviceToPlugin, new HelloPayload(0) { Seq = 9 });

        Assert.NotNull(session.Open(envelope));
        Assert.Null(session.Open(envelope));
        Assert.Null(session.Open(older));
    }

    [Fact]
    public void Open_LastSeenSeqFromConstructor_RejectsOlder()
    {
        var session = new DeviceSession("dev1", pluginKey, [], lastSeenSeq: 100);
        var older = Payloads.SealPayload(deviceKey, Direction.DeviceToPlugin, new HelloPayload(0) { Seq = 99 });
        var same = Payloads.SealPayload(deviceKey, Direction.DeviceToPlugin, new HelloPayload(0) { Seq = 100 });
        var newer = Payloads.SealPayload(deviceKey, Direction.DeviceToPlugin, new HelloPayload(0) { Seq = 101 });

        Assert.Equal(100, session.LastSeenSeq);
        Assert.Null(session.Open(older));
        Assert.Null(session.Open(same));
        Assert.NotNull(session.Open(newer));
        Assert.Equal(101, session.LastSeenSeq);
    }

    [Fact]
    public void Open_WrongDirectionOrGarbage_Null()
    {
        var session = new DeviceSession("dev1", pluginKey, []);
        var wrongDirection = Payloads.SealPayload(deviceKey, Direction.PluginToDevice, new HelloPayload(0) { Seq = 10 });

        Assert.Null(session.Open(wrongDirection));
        Assert.Null(session.Open("not base64!"));
        Assert.Null(session.Open(""));
    }

    [Fact]
    public void Open_NonPayloadJson_Null()
    {
        var session = new DeviceSession("dev1", pluginKey, []);
        var envelope = Base64Url.Encode(E2eCrypto.Seal(deviceKey, Direction.DeviceToPlugin, "{\"type\":\"nope\"}"u8.ToArray()));

        Assert.Null(session.Open(envelope));
    }

    [Fact]
    public void Mute_BlocksNotify()
    {
        var session = new DeviceSession("dev1", pluginKey, [ChatChannel.FreeCompany]);

        Assert.False(session.ShouldNotify(Item(1, ChatChannel.FreeCompany), true));
        Assert.True(session.ShouldNotify(Item(1, ChatChannel.Tell), true));
        Assert.False(session.ShouldNotify(Item(1, ChatChannel.Tell), false));

        session.Muted.Add(ChatChannel.Tell);
        Assert.False(session.ShouldNotify(Item(1, ChatChannel.Tell), true));
    }

    [Fact]
    public void Muted_IsCopyOfInput()
    {
        var input = new List<ChatChannel> { ChatChannel.Say };
        var session = new DeviceSession("dev1", pluginKey, input);
        input.Add(ChatChannel.Tell);

        Assert.Equal([ChatChannel.Say], session.Muted);
    }

    [Theory]
    [InlineData(0, new[] { 0 })]
    [InlineData(50, new[] { 50 })]
    [InlineData(51, new[] { 50, 1 })]
    [InlineData(120, new[] { 50, 50, 20 })]
    public void Backlog_Chunks(int count, int[] sizes)
    {
        var items = Enumerable.Range(1, count).Select(i => Item(i)).ToList();

        var chunks = DeviceSession.Backlog(items).ToList();

        Assert.Equal(sizes, chunks.Select(c => c.Items.Count));
        Assert.All(chunks[..^1], c => Assert.False(c.Done));
        Assert.True(chunks[^1].Done);
        Assert.Equal(items, chunks.SelectMany(c => c.Items));
    }

    [Fact]
    public void Backlog_SplitsBySize()
    {
        var items = Enumerable.Range(1, 10).Select(i => Item(i, text: new string('x', 900))).ToList();
        var budget = 3 * (JsonBytes(items[0]) + 1);

        var chunks = DeviceSession.Backlog(items, budget).ToList();

        Assert.Equal([3, 3, 3, 1], chunks.Select(c => c.Items.Count));
        Assert.All(chunks[..^1], c => Assert.False(c.Done));
        Assert.True(chunks[^1].Done);
        Assert.Equal(items, chunks.SelectMany(c => c.Items));
    }

    [Fact]
    public void Backlog_DefaultBudget_KeepsChunksUnderFrameLimit()
    {
        var items = Enumerable.Range(1, 50).Select(i => Item(i, text: new string('x', 2000))).ToList();

        var chunks = DeviceSession.Backlog(items).ToList();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Items.Sum(i => JsonBytes(i) + 1) <= DeviceSession.MaxPayloadJsonBytes));
        Assert.Equal(items, chunks.SelectMany(c => c.Items));
    }

    [Fact]
    public void Backlog_OversizedItem_Truncated()
    {
        var huge = Item(1, text: new string('x', 100_000));

        var chunk = Assert.Single(DeviceSession.Backlog([huge]));

        var item = Assert.Single(chunk.Items);
        Assert.True(chunk.Done);
        Assert.EndsWith("...", item.Text);
        Assert.True(JsonBytes(item) <= DeviceSession.MaxPayloadJsonBytes);
    }

    [Fact]
    public void Fit_SmallItem_Unchanged()
    {
        var item = Item(1);

        Assert.Same(item, DeviceSession.Fit(item));
    }

    [Fact]
    public void Fit_KeepsSurrogatePairsWhole()
    {
        var item = Item(1, text: string.Concat(Enumerable.Repeat("😀", 20_000)));

        var fitted = DeviceSession.Fit(item);

        Assert.True(JsonBytes(fitted) <= DeviceSession.MaxPayloadJsonBytes);
        var body = fitted.Text[..^3];
        Assert.False(char.IsHighSurrogate(body[^1]));
    }
}
