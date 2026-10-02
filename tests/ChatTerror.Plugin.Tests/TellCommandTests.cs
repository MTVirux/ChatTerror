using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class TellCommandTests
{
    [Theory]
    [InlineData("/tell Bob Smith@Lich hello there", "Bob Smith", "Lich", "hello there")]
    [InlineData("/t Bob Smith@Lich hi", "Bob Smith", "Lich", "hi")]
    [InlineData("/TELL Y'shtola Rhul hi", "Y'shtola Rhul", "Twintania", "hi")]
    public void ParseTell_ReadsTargetAndText(string line, string name, string world, string text)
    {
        var parsed = TellCommand.ParseTell(line, "Twintania");

        Assert.Equal((new TellTarget(name, world), text), parsed);
    }

    [Theory]
    [InlineData("/tell Bob hello")]
    [InlineData("/tell Bob Smith@Lich")]
    [InlineData("/p hello")]
    [InlineData("/tellx Bob Smith@Lich hi")]
    public void ParseTell_RejectsOtherLines(string line) => Assert.Null(TellCommand.ParseTell(line, "Lich"));

    [Theory]
    [InlineData("/r hello", "hello")]
    [InlineData("/reply  hi there", "hi there")]
    [InlineData("/r", null)]
    [InlineData("/ready", null)]
    public void ParseReply(string line, string? text) => Assert.Equal(text, TellCommand.ParseReply(line));

    [Fact]
    public void Target_MatchesEchoWithOrWithoutWorld()
    {
        var target = new TellTarget("Bob Smith", "Lich");

        Assert.True(target.Matches("bob smith", null));
        Assert.True(target.Matches("Bob Smith", "lich"));
        Assert.False(target.Matches("Bob Smith", "Twintania"));
        Assert.Equal("Bob Smith@Lich", target.ToString());
    }
}
