using System;
using System.Collections.Generic;
using System.Linq;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public sealed class ChannelSetting
{
    public bool Relay { get; set; }
    public bool Push { get; set; }
    public bool Send { get; set; }
}

public sealed class RelaySettings
{
    private static readonly HashSet<ChatChannel> SendOffByDefault =
        [ChatChannel.Alliance, ChatChannel.NoviceNetwork];

    public Dictionary<ChatChannel, ChannelSetting> Channels { get; set; } = Defaults();
    public List<ChatChannel> ChannelOrder { get; set; } = new();
    public bool PushOnTell { get; set; } = true;
    public bool PushOnMention { get; set; } = true;
    public List<string> PushKeywords { get; set; } = new();
    public bool QuietHoursEnabled { get; set; }
    public int QuietStartMinutes { get; set; } = 23 * 60;
    public int QuietEndMinutes { get; set; } = 7 * 60;
    public List<string> IgnoredSenders { get; set; } = new();
    public bool RelayOwnMessages { get; set; } = true;
    public int HistorySize { get; set; } = Limits.DefaultHistory;
    public int SendDelayMs { get; set; } = 1000;
    public bool RequireLoggedIn { get; set; } = true;
    public int MaxLengthBytes { get; set; } = Limits.MaxTextBytes;

    // Drops duplicates and unknown values, then appends channels missing from the saved order.
    public List<ChatChannel> OrderedChannels()
    {
        var order = ChannelOrder.Where(Enum.IsDefined).Distinct().ToList();
        order.AddRange(Enum.GetValues<ChatChannel>().Except(order));
        return order;
    }

    public static Dictionary<ChatChannel, ChannelSetting> Defaults() =>
        Enum.GetValues<ChatChannel>().ToDictionary(
            channel => channel,
            channel => new ChannelSetting
            {
                Relay = true,
                Push = channel == ChatChannel.Tell,
                Send = !SendOffByDefault.Contains(channel),
            });
}
