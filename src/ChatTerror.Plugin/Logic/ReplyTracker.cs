namespace ChatTerror.Plugin.Logic;

// The game's own /r target is never set by relayed tells, so /r is rewritten while one of those is the latest.
public sealed class ReplyTracker
{
    private TellTarget? last;
    private bool lastRelayed;

    public void Incoming(TellTarget from, bool relayed)
    {
        last = from;
        lastRelayed = relayed;
    }

    public string? Rewrite(string line) =>
        lastRelayed && last != null && TellCommand.ParseReply(line) is { } text ? $"/tell {last} {text}" : null;
}
