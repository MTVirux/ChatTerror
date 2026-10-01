using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class ChatFilterTests
{
    private static readonly TimeOnly Noon = new(12, 0);

    private static IncomingChat Chat(ChatChannel channel, string text = "hello", string sender = "Bob Smith", string? world = "Twintania", bool outgoing = false) =>
        new(channel, sender, world, text, outgoing, 1000);

    [Fact]
    public void RelayOffChannel_NotRelayed()
    {
        var s = new RelaySettings();
        s.Channels[ChatChannel.Say].Relay = false;

        var r = ChatFilter.Evaluate(Chat(ChatChannel.Say), s, "Alex Doe", Noon);

        Assert.False(r.Relay);
        Assert.False(r.Notify);
    }

    [Fact]
    public void IgnoredSender_WithoutWorld_MatchesAnyWorld()
    {
        var s = new RelaySettings { IgnoredSenders = ["bob smith"] };

        Assert.False(ChatFilter.Evaluate(Chat(ChatChannel.Tell), s, null, Noon).Relay);
        Assert.False(ChatFilter.Evaluate(Chat(ChatChannel.Tell, world: "Gilgamesh"), s, null, Noon).Relay);
        Assert.True(ChatFilter.Evaluate(Chat(ChatChannel.Tell, sender: "Bob Jones"), s, null, Noon).Relay);
    }

    [Fact]
    public void IgnoredSender_WithWorld_MatchesOnlyThatWorld()
    {
        var s = new RelaySettings { IgnoredSenders = ["Bob Smith@TWINTANIA"] };

        Assert.False(ChatFilter.Evaluate(Chat(ChatChannel.Tell), s, null, Noon).Relay);
        Assert.True(ChatFilter.Evaluate(Chat(ChatChannel.Tell, world: "Gilgamesh"), s, null, Noon).Relay);
        Assert.True(ChatFilter.Evaluate(Chat(ChatChannel.Tell, world: null), s, null, Noon).Relay);
    }

    [Fact]
    public void OwnMessages_Toggle()
    {
        var s = new RelaySettings();
        var own = Chat(ChatChannel.Tell, outgoing: true);

        var on = ChatFilter.Evaluate(own, s, "Alex Doe", Noon);
        Assert.True(on.Relay);
        Assert.False(on.Notify);

        s.RelayOwnMessages = false;
        Assert.False(ChatFilter.Evaluate(own, s, "Alex Doe", Noon).Relay);
    }

    [Fact]
    public void Tell_Notifies()
    {
        var r = ChatFilter.Evaluate(Chat(ChatChannel.Tell), new RelaySettings(), "Alex Doe", Noon);

        Assert.True(r.Relay);
        Assert.True(r.Notify);
    }

    [Fact]
    public void PushOnTell_NotifiesEvenWhenChannelPushOff()
    {
        var s = new RelaySettings();
        s.Channels[ChatChannel.Tell].Push = false;

        Assert.True(ChatFilter.Evaluate(Chat(ChatChannel.Tell), s, null, Noon).Notify);

        s.PushOnTell = false;
        Assert.False(ChatFilter.Evaluate(Chat(ChatChannel.Tell), s, null, Noon).Notify);
    }

    [Fact]
    public void ChannelPushOn_Notifies()
    {
        var s = new RelaySettings();
        s.Channels[ChatChannel.Party].Push = true;

        Assert.True(ChatFilter.Evaluate(Chat(ChatChannel.Party), s, null, Noon).Notify);
    }

    [Fact]
    public void Party_WithoutTriggers_RelaysWithoutNotify()
    {
        var r = ChatFilter.Evaluate(Chat(ChatChannel.Party), new RelaySettings(), "Alex Doe", Noon);

        Assert.True(r.Relay);
        Assert.False(r.Notify);
    }

    [Theory]
    [InlineData("Hi Alex", true)]
    [InlineData("hi alex!", true)]
    [InlineData("where is alex doe?", true)]
    [InlineData("is that Alex's?", true)]
    [InlineData("Alexander is here", false)]
    [InlineData("xAlex", false)]
    public void Mention_WholeWord(string text, bool notify)
    {
        var r = ChatFilter.Evaluate(Chat(ChatChannel.Party, text), new RelaySettings(), "Alex Doe", Noon);

        Assert.Equal(notify, r.Notify);
    }

    [Fact]
    public void Mention_NameWithApostrophe()
    {
        var r = ChatFilter.Evaluate(Chat(ChatChannel.Party, "thanks y'shtola"), new RelaySettings(), "Y'shtola Rhul", Noon);

        Assert.True(r.Notify);
    }

    [Fact]
    public void Mention_Disabled_DoesNotNotify()
    {
        var s = new RelaySettings { PushOnMention = false };

        Assert.False(ChatFilter.Evaluate(Chat(ChatChannel.Party, "Hi Alex"), s, "Alex Doe", Noon).Notify);
    }

    [Fact]
    public void Mention_NoLocalName_DoesNotNotify()
    {
        Assert.False(ChatFilter.Evaluate(Chat(ChatChannel.Party, "Hi Alex"), new RelaySettings(), null, Noon).Notify);
    }

    [Fact]
    public void Keyword_ContainedCaseInsensitive_Notifies()
    {
        var s = new RelaySettings { PushKeywords = ["raid", ""] };

        Assert.True(ChatFilter.Evaluate(Chat(ChatChannel.FreeCompany, "RAIDING tonight"), s, null, Noon).Notify);
        Assert.False(ChatFilter.Evaluate(Chat(ChatChannel.FreeCompany, "maps tonight"), s, null, Noon).Notify);
    }

    [Fact]
    public void QuietHours_SuppressNotify_ButStillRelay()
    {
        var s = new RelaySettings { QuietHoursEnabled = true };

        var r = ChatFilter.Evaluate(Chat(ChatChannel.Tell), s, "Alex Doe", new TimeOnly(2, 0));

        Assert.True(r.Relay);
        Assert.False(r.Notify);
    }
}

public class QuietHoursTests
{
    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(22, 59, false)]
    public void WrapsPastMidnight(int hour, int minute, bool quiet)
    {
        var s = new RelaySettings { QuietHoursEnabled = true };

        Assert.Equal(quiet, QuietHours.IsQuiet(s, new TimeOnly(hour, minute)));
    }

    [Theory]
    [InlineData(8, 59, false)]
    [InlineData(9, 0, true)]
    [InlineData(16, 59, true)]
    [InlineData(17, 0, false)]
    public void SameDayWindow(int hour, int minute, bool quiet)
    {
        var s = new RelaySettings { QuietHoursEnabled = true, QuietStartMinutes = 9 * 60, QuietEndMinutes = 17 * 60 };

        Assert.Equal(quiet, QuietHours.IsQuiet(s, new TimeOnly(hour, minute)));
    }

    [Fact]
    public void StartEqualsEnd_NeverQuiet()
    {
        var s = new RelaySettings { QuietHoursEnabled = true, QuietStartMinutes = 600, QuietEndMinutes = 600 };

        Assert.False(QuietHours.IsQuiet(s, new TimeOnly(10, 0)));
        Assert.False(QuietHours.IsQuiet(s, new TimeOnly(3, 0)));
    }

    [Fact]
    public void OutOfRangeMinutes_AreNormalized()
    {
        var s = new RelaySettings { QuietHoursEnabled = true, QuietStartMinutes = 1440 + 9 * 60, QuietEndMinutes = -7 * 60 };

        Assert.True(QuietHours.IsQuiet(s, new TimeOnly(12, 0)));
        Assert.False(QuietHours.IsQuiet(s, new TimeOnly(18, 0)));
    }

    [Fact]
    public void Disabled_NeverQuiet()
    {
        Assert.False(QuietHours.IsQuiet(new RelaySettings(), new TimeOnly(2, 0)));
    }
}

public class RelaySettingsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var d = RelaySettings.Defaults();
        var sendOff = new[] { ChatChannel.Alliance, ChatChannel.NoviceNetwork, ChatChannel.Shout, ChatChannel.Yell };

        Assert.Equal(Enum.GetValues<ChatChannel>().Length, d.Count);
        foreach (var channel in Enum.GetValues<ChatChannel>())
        {
            Assert.True(d[channel].Relay);
            Assert.Equal(channel == ChatChannel.Tell, d[channel].Push);
            Assert.Equal(!sendOff.Contains(channel), d[channel].Send);
        }
    }

    [Fact]
    public void Defaults_ReturnsFreshInstances()
    {
        var a = RelaySettings.Defaults();
        a[ChatChannel.Say].Relay = false;

        Assert.True(RelaySettings.Defaults()[ChatChannel.Say].Relay);
    }

    [Fact]
    public void Defaults_PropertyValues()
    {
        var s = new RelaySettings();

        Assert.True(s.PushOnTell);
        Assert.True(s.PushOnMention);
        Assert.False(s.QuietHoursEnabled);
        Assert.Equal(23 * 60, s.QuietStartMinutes);
        Assert.Equal(7 * 60, s.QuietEndMinutes);
        Assert.True(s.RelayOwnMessages);
        Assert.Equal(500, s.HistorySize);
        Assert.Equal(1000, s.SendDelayMs);
        Assert.True(s.RequireLoggedIn);
        Assert.Equal(500, s.MaxLengthBytes);
    }
}
