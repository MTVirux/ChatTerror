using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public static class SendValidator
{
    // \z rather than $ because $ also matches before a trailing newline.
    private static readonly Regex TellTarget = new(@"^[A-Za-z'\-]{1,15} [A-Za-z'\-]{1,15}@[A-Za-z]{3,16}\z");

    public static string? Validate(SendChatPayload p, RelaySettings s)
    {
        if (!CanSend(p.Channel) || s.Channels.GetValueOrDefault(p.Channel) is not { Send: true })
            return SendErrors.ChannelNotAllowed;

        var text = p.Text.Trim();
        if (text.Length == 0 || text.StartsWith('/') || text.Any(IsLineBreakOrControl))
            return SendErrors.InvalidText;

        var maxBytes = Math.Min(s.MaxLengthBytes, Limits.MaxTextBytes);
        if (Encoding.UTF8.GetByteCount(BuildLine(p)) > maxBytes)
            return SendErrors.TooLong;

        if (p.Channel == ChatChannel.Tell && (p.Target is null || !TellTarget.IsMatch(p.Target)))
            return SendErrors.InvalidTarget;

        return null;
    }

    public static bool IsLineBreakOrControl(char c) => char.IsControl(c) || c is '\u2028' or '\u2029';

    public static string BuildLine(SendChatPayload p) => Prefix(p) + p.Text.Trim();

    public static bool CanSend(ChatChannel channel) => channel <= ChatChannel.Emote;

    private static string Prefix(SendChatPayload p) => p.Channel switch
    {
        ChatChannel.Tell => $"/tell {p.Target} ",
        ChatChannel.Party => "/p ",
        ChatChannel.Alliance => "/a ",
        ChatChannel.FreeCompany => "/fc ",
        >= ChatChannel.Linkshell1 and <= ChatChannel.Linkshell8 => $"/l{p.Channel - ChatChannel.Linkshell1 + 1} ",
        >= ChatChannel.CrossLinkshell1 and <= ChatChannel.CrossLinkshell8 => $"/cwl{p.Channel - ChatChannel.CrossLinkshell1 + 1} ",
        ChatChannel.NoviceNetwork => "/n ",
        ChatChannel.Say => "/s ",
        ChatChannel.Shout => "/sh ",
        ChatChannel.Yell => "/y ",
        ChatChannel.Echo => "/e ",
        ChatChannel.Emote => "/em ",
        _ => throw new ArgumentOutOfRangeException(nameof(p), p.Channel, "Unknown channel."),
    };
}
