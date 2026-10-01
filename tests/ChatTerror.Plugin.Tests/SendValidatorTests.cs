using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class SendValidatorTests
{
    private static SendChatPayload Send(ChatChannel channel, string text, string? target = null) =>
        new("r1", channel, target, text);

    [Fact]
    public void ChannelSendOff_ChannelNotAllowed()
    {
        Assert.Equal(SendErrors.ChannelNotAllowed, SendValidator.Validate(Send(ChatChannel.Shout, "hi"), new RelaySettings()));
    }

    [Fact]
    public void ChannelNotAllowed_CheckedBeforeText()
    {
        Assert.Equal(SendErrors.ChannelNotAllowed, SendValidator.Validate(Send(ChatChannel.Yell, "/logout"), new RelaySettings()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hi\nthere")]
    [InlineData("hi\rthere")]
    [InlineData("/logout")]
    [InlineData("  /logout")]
    [InlineData("hi\u0002there")]
    [InlineData("hi\u0007there")]
    [InlineData("hi\u0085there")]
    [InlineData("hi\u2028there")]
    [InlineData("hi\u2029there")]
    [InlineData("hi\tthere")]
    public void BadText_InvalidText(string text)
    {
        Assert.Equal(SendErrors.InvalidText, SendValidator.Validate(Send(ChatChannel.Party, text), new RelaySettings()));
    }

    [Fact]
    public void Trims_And_BuildsLine()
    {
        var p = Send(ChatChannel.Party, " hi ");

        Assert.Null(SendValidator.Validate(p, new RelaySettings()));
        Assert.Equal("/p hi", SendValidator.BuildLine(p));
    }

    [Theory]
    [InlineData(ChatChannel.Party, "/p x")]
    [InlineData(ChatChannel.Alliance, "/a x")]
    [InlineData(ChatChannel.FreeCompany, "/fc x")]
    [InlineData(ChatChannel.Linkshell1, "/l1 x")]
    [InlineData(ChatChannel.Linkshell3, "/l3 x")]
    [InlineData(ChatChannel.Linkshell8, "/l8 x")]
    [InlineData(ChatChannel.CrossLinkshell1, "/cwl1 x")]
    [InlineData(ChatChannel.CrossLinkshell8, "/cwl8 x")]
    [InlineData(ChatChannel.NoviceNetwork, "/n x")]
    [InlineData(ChatChannel.Say, "/s x")]
    [InlineData(ChatChannel.Shout, "/sh x")]
    [InlineData(ChatChannel.Yell, "/y x")]
    public void Prefixes(ChatChannel channel, string expected)
    {
        Assert.Equal(expected, SendValidator.BuildLine(Send(channel, "x")));
    }

    [Fact]
    public void Tell_BuildsLineWithTarget()
    {
        var p = Send(ChatChannel.Tell, "hey", "Y'shtola Rhul@Twintania");

        Assert.Null(SendValidator.Validate(p, new RelaySettings()));
        Assert.Equal("/tell Y'shtola Rhul@Twintania hey", SendValidator.BuildLine(p));
    }

    [Theory]
    [InlineData("Y'shtola Rhul@Twintania")]
    [InlineData("Jean-Luc Picard@Gilgamesh")]
    public void Tell_ValidTargets(string target)
    {
        Assert.Null(SendValidator.Validate(Send(ChatChannel.Tell, "hey", target), new RelaySettings()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bob@World")]
    [InlineData("A B")]
    [InlineData("Bob Smith")]
    [InlineData("Bob Smith@Tw")]
    [InlineData("Bob Smith@Twin tania")]
    [InlineData("Bob Smith@World\n")]
    public void Tell_InvalidTargets(string? target)
    {
        Assert.Equal(SendErrors.InvalidTarget, SendValidator.Validate(Send(ChatChannel.Tell, "hey", target), new RelaySettings()));
    }

    [Fact]
    public void NonTell_TargetIgnored()
    {
        var p = Send(ChatChannel.Party, "hi", "Bob@World");

        Assert.Null(SendValidator.Validate(p, new RelaySettings()));
        Assert.Equal("/p hi", SendValidator.BuildLine(p));
    }

    [Fact]
    public void Exactly500ByteLine_Ok_501_TooLong()
    {
        var s = new RelaySettings();
        var ok = Send(ChatChannel.Party, new string('a', 500 - "/p ".Length));
        var over = Send(ChatChannel.Party, new string('a', 501 - "/p ".Length));

        Assert.Null(SendValidator.Validate(ok, s));
        Assert.Equal(SendErrors.TooLong, SendValidator.Validate(over, s));
    }

    [Fact]
    public void Multibyte_CountedInBytes()
    {
        var s = new RelaySettings();
        // two bytes per char: 3 + 248*2 = 499, 3 + 249*2 = 501
        Assert.Null(SendValidator.Validate(Send(ChatChannel.Party, new string('é', 248)), s));
        Assert.Equal(SendErrors.TooLong, SendValidator.Validate(Send(ChatChannel.Party, new string('é', 249)), s));
    }

    [Fact]
    public void MaxLengthBytes_LowerLimitApplies()
    {
        var s = new RelaySettings { MaxLengthBytes = 10 };

        Assert.Null(SendValidator.Validate(Send(ChatChannel.Party, "1234567"), s));
        Assert.Equal(SendErrors.TooLong, SendValidator.Validate(Send(ChatChannel.Party, "12345678"), s));
    }

    [Fact]
    public void MaxLengthBytes_CannotRaiseAbove500()
    {
        var s = new RelaySettings { MaxLengthBytes = 2000 };

        Assert.Equal(SendErrors.TooLong, SendValidator.Validate(Send(ChatChannel.Party, new string('a', 600)), s));
    }

    [Fact]
    public void MissingChannelSetting_ChannelNotAllowed()
    {
        var s = new RelaySettings();
        s.Channels.Remove(ChatChannel.Party);

        Assert.Equal(SendErrors.ChannelNotAllowed, SendValidator.Validate(Send(ChatChannel.Party, "hi"), s));
    }
}
