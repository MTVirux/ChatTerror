using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class SendQueueTests
{
    private sealed class FakeGate : IGameGate
    {
        public bool IsLoggedIn { get; set; } = true;
        public bool IsBusy { get; set; }
        public bool Reject { get; set; }
        public string? Sanitize(string line) => Reject ? null : line;
    }

    private readonly FakeGate gate = new();
    private readonly RelaySettings settings = new();
    private long now = 100_000;

    private SendQueue NewQueue() => new(() => settings, gate, () => now);

    private static SendRequest Req(string id, string text = "hi", ChatChannel channel = ChatChannel.Party) =>
        new("dev1", new SendChatPayload(id, channel, Text: text));

    [Fact]
    public void Empty_ReturnsNothing()
    {
        var (line, results) = NewQueue().Tick();

        Assert.Null(line);
        Assert.Empty(results);
    }

    [Fact]
    public void Valid_ReturnsLineAndOk()
    {
        var q = NewQueue();
        q.Enqueue(Req("a"));

        var (line, results) = q.Tick();

        Assert.Equal("/p hi", line);
        var (req, result) = Assert.Single(results);
        Assert.Equal("dev1", req.DeviceId);
        Assert.Equal("a", result.RequestId);
        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal(0, result.Seq);
        Assert.Empty(q.Tick().Results);
    }

    [Fact]
    public void ValidationError_ReportedWithoutLine()
    {
        var q = NewQueue();
        q.Enqueue(Req("a", "/logout"));

        var (line, results) = q.Tick();

        Assert.Null(line);
        var (_, result) = Assert.Single(results);
        Assert.False(result.Ok);
        Assert.Equal(SendErrors.InvalidText, result.Error);
    }

    [Fact]
    public void Delay_Respected()
    {
        var q = NewQueue();
        q.Enqueue(Req("a"));
        q.Enqueue(Req("b"));

        Assert.Equal("/p hi", q.Tick().Line);

        now += 999;
        var held = q.Tick();
        Assert.Null(held.Line);
        Assert.Empty(held.Results);

        now += 1;
        var (line, results) = q.Tick();
        Assert.Equal("/p hi", line);
        Assert.Equal("b", Assert.Single(results).Result.RequestId);
    }

    [Fact]
    public void NotLoggedIn_ReportsError()
    {
        gate.IsLoggedIn = false;
        var q = NewQueue();
        q.Enqueue(Req("a"));

        var (line, results) = q.Tick();

        Assert.Null(line);
        Assert.Equal(SendErrors.NotLoggedIn, Assert.Single(results).Result.Error);
    }

    [Fact]
    public void NotLoggedIn_HeldWhenNotRequired_ThenSends()
    {
        gate.IsLoggedIn = false;
        settings.RequireLoggedIn = false;
        var q = NewQueue();
        q.Enqueue(Req("a"));

        var held = q.Tick();
        Assert.Null(held.Line);
        Assert.Empty(held.Results);

        now += 5_000;
        Assert.Empty(q.Tick().Results);

        gate.IsLoggedIn = true;
        var (line, results) = q.Tick();
        Assert.Equal("/p hi", line);
        Assert.True(Assert.Single(results).Result.Ok);
    }

    [Fact]
    public void NotLoggedIn_NotRequired_TimesOutAfter10s()
    {
        gate.IsLoggedIn = false;
        settings.RequireLoggedIn = false;
        var q = NewQueue();
        q.Enqueue(Req("a"));

        now += 9_999;
        Assert.Empty(q.Tick().Results);

        now += 1;
        var (line, results) = q.Tick();
        Assert.Null(line);
        Assert.Equal(SendErrors.NotLoggedIn, Assert.Single(results).Result.Error);
    }

    [Fact]
    public void Busy_HoldsThenSends()
    {
        gate.IsBusy = true;
        var q = NewQueue();
        q.Enqueue(Req("a"));

        var held = q.Tick();
        Assert.Null(held.Line);
        Assert.Empty(held.Results);

        now += 5_000;
        Assert.Empty(q.Tick().Results);

        gate.IsBusy = false;
        var (line, results) = q.Tick();
        Assert.Equal("/p hi", line);
        Assert.True(Assert.Single(results).Result.Ok);
    }

    [Fact]
    public void Busy_TimesOutAfter10s()
    {
        gate.IsBusy = true;
        var q = NewQueue();
        q.Enqueue(Req("a"));

        now += 9_999;
        Assert.Empty(q.Tick().Results);

        now += 1;
        var (line, results) = q.Tick();
        Assert.Null(line);
        Assert.Equal(SendErrors.Busy, Assert.Single(results).Result.Error);
    }

    [Fact]
    public void SanitizeNull_InvalidText()
    {
        gate.Reject = true;
        var q = NewQueue();
        q.Enqueue(Req("a"));

        var (line, results) = q.Tick();

        Assert.Null(line);
        Assert.Equal(SendErrors.InvalidText, Assert.Single(results).Result.Error);
    }

    [Fact]
    public void Overflow_BusyImmediately()
    {
        gate.IsBusy = true;
        var q = NewQueue();
        for (var i = 0; i < 21; i++)
            q.Enqueue(Req($"r{i}"));

        var (line, results) = q.Tick();

        Assert.Null(line);
        var (_, result) = Assert.Single(results);
        Assert.Equal("r20", result.RequestId);
        Assert.Equal(SendErrors.Busy, result.Error);
    }

    [Fact]
    public void ErrorsDoNotBlockFollowingItem()
    {
        var q = NewQueue();
        q.Enqueue(Req("bad", ""));
        q.Enqueue(Req("good"));

        var (line, results) = q.Tick();

        Assert.Equal("/p hi", line);
        Assert.Equal(["bad", "good"], results.Select(r => r.Result.RequestId));
    }

    [Fact]
    public void FailedItem_DoesNotStartDelay()
    {
        gate.Reject = true;
        var q = NewQueue();
        q.Enqueue(Req("a"));
        q.Tick();

        gate.Reject = false;
        q.Enqueue(Req("b"));
        Assert.Equal("/p hi", q.Tick().Line);
    }
}
