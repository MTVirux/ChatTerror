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
        XivChatType.Echo => ChatChannel.Echo,
        XivChatType.CustomEmote or XivChatType.StandardEmote => ChatChannel.Emote,
        XivChatType.PvPTeam => ChatChannel.PvpTeam,
        XivChatType.SystemError or XivChatType.ErrorMessage => ChatChannel.Error,
        XivChatType.RetainerSale => ChatChannel.Sales,
        XivChatType.LootNotice or XivChatType.LootRoll => ChatChannel.Loot,
        XivChatType.Progress => ChatChannel.Progress,
        XivChatType.Crafting => ChatChannel.Crafting,
        XivChatType.Gathering or XivChatType.GatheringSystemMessage => ChatChannel.Gathering,
        XivChatType.NPCDialogue or XivChatType.NPCDialogueAnnouncements => ChatChannel.NpcDialogue,
        XivChatType.FreeCompanyAnnouncement or XivChatType.FreeCompanyLoginLogout or XivChatType.PvpTeamAnnouncement
            or XivChatType.PvpTeamLoginLogout or XivChatType.NoviceNetworkSystem => ChatChannel.Announcements,
        XivChatType.RandomNumber => ChatChannel.RandomNumber,
        XivChatType.Damage or XivChatType.Miss or XivChatType.Action or XivChatType.Item or XivChatType.Healing
            or XivChatType.GainBuff or XivChatType.GainDebuff or XivChatType.LoseBuff or XivChatType.LoseDebuff => ChatChannel.Battle,
        >= XivChatType.GmTell and <= XivChatType.GmNoviceNetwork => ChatChannel.Gm,
        XivChatType.None => null,
        // Notices, alarms, recruitment and any type without its own channel.
        _ => ChatChannel.System,
    };

    public static bool IsOutgoing(XivChatType t) => t == XivChatType.TellOutgoing;

    public static string DisplayName(ChatChannel c) => c switch
    {
        ChatChannel.FreeCompany => "Free Company",
        ChatChannel.NoviceNetwork => "Novice Network",
        ChatChannel.PvpTeam => "PvP Team",
        ChatChannel.Sales => "Retainer Sales",
        ChatChannel.NpcDialogue => "NPC Dialogue",
        ChatChannel.RandomNumber => "Random Number",
        ChatChannel.Gm => "GM",
        >= ChatChannel.Linkshell1 and <= ChatChannel.Linkshell8 => $"Linkshell {c - ChatChannel.Linkshell1 + 1}",
        >= ChatChannel.CrossLinkshell1 and <= ChatChannel.CrossLinkshell8 => $"Cross-world Linkshell {c - ChatChannel.CrossLinkshell1 + 1}",
        _ => c.ToString(),
    };
}
