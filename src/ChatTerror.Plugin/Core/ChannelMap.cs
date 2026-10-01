using ChatTerror.Protocol;
using Dalamud.Game.Text;

namespace ChatTerror.Plugin.Core;

public static class ChannelMap
{
    public static ChatChannel? FromXivChatType(XivChatType t) => t switch
    {
        XivChatType.TellIncoming or XivChatType.TellOutgoing => ChatChannel.Tell,
        XivChatType.Party or XivChatType.CrossParty => ChatChannel.Party,
        XivChatType.Alliance => ChatChannel.Alliance,
        XivChatType.FreeCompany => ChatChannel.FreeCompany,
        XivChatType.Ls1 => ChatChannel.Linkshell1,
        XivChatType.Ls2 => ChatChannel.Linkshell2,
        XivChatType.Ls3 => ChatChannel.Linkshell3,
        XivChatType.Ls4 => ChatChannel.Linkshell4,
        XivChatType.Ls5 => ChatChannel.Linkshell5,
        XivChatType.Ls6 => ChatChannel.Linkshell6,
        XivChatType.Ls7 => ChatChannel.Linkshell7,
        XivChatType.Ls8 => ChatChannel.Linkshell8,
        XivChatType.CrossLinkShell1 => ChatChannel.CrossLinkshell1,
        XivChatType.CrossLinkShell2 => ChatChannel.CrossLinkshell2,
        XivChatType.CrossLinkShell3 => ChatChannel.CrossLinkshell3,
        XivChatType.CrossLinkShell4 => ChatChannel.CrossLinkshell4,
        XivChatType.CrossLinkShell5 => ChatChannel.CrossLinkshell5,
        XivChatType.CrossLinkShell6 => ChatChannel.CrossLinkshell6,
        XivChatType.CrossLinkShell7 => ChatChannel.CrossLinkshell7,
        XivChatType.CrossLinkShell8 => ChatChannel.CrossLinkshell8,
        XivChatType.NoviceNetwork => ChatChannel.NoviceNetwork,
        XivChatType.Say => ChatChannel.Say,
        XivChatType.Shout => ChatChannel.Shout,
        XivChatType.Yell => ChatChannel.Yell,
        _ => null,
    };

    public static bool IsOutgoing(XivChatType t) => t == XivChatType.TellOutgoing;

    public static string DisplayName(ChatChannel c) => c switch
    {
        ChatChannel.FreeCompany => "Free Company",
        ChatChannel.NoviceNetwork => "Novice Network",
        >= ChatChannel.Linkshell1 and <= ChatChannel.Linkshell8 => $"Linkshell {c - ChatChannel.Linkshell1 + 1}",
        >= ChatChannel.CrossLinkshell1 and <= ChatChannel.CrossLinkshell8 => $"Cross-world Linkshell {c - ChatChannel.CrossLinkshell1 + 1}",
        _ => c.ToString(),
    };
}
