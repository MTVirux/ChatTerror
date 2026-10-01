using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public sealed record IncomingChat(ChatChannel Channel, string Sender, string? SenderWorld, string Text, bool Outgoing, long Ts);

public readonly record struct FilterResult(bool Relay, bool Notify);

public static class ChatFilter
{
    public static FilterResult Evaluate(IncomingChat chat, RelaySettings s, string? localName, TimeOnly localTime)
    {
        var channel = s.Channels.GetValueOrDefault(chat.Channel);
        if (channel is not { Relay: true } || IsIgnored(chat, s) || (chat.Outgoing && !s.RelayOwnMessages))
            return new FilterResult(false, false);

        if (chat.Outgoing || QuietHours.IsQuiet(s, localTime))
            return new FilterResult(true, false);

        var notify = channel.Push
            || (s.PushOnTell && chat.Channel == ChatChannel.Tell)
            || (s.PushOnMention && Mentions(chat.Text, localName))
            || HasKeyword(chat.Text, s.PushKeywords);
        return new FilterResult(true, notify);
    }

    private static bool IsIgnored(IncomingChat chat, RelaySettings s)
    {
        var withWorld = chat.SenderWorld is null ? null : $"{chat.Sender}@{chat.SenderWorld}";
        foreach (var entry in s.IgnoredSenders)
        {
            var name = entry.Trim();
            var target = name.Contains('@') ? withWorld : chat.Sender;
            if (target is not null && string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool Mentions(string text, string? localName)
    {
        if (string.IsNullOrWhiteSpace(localName))
            return false;

        var fullName = localName.Trim();
        var firstName = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return ContainsWord(text, firstName) || ContainsWord(text, fullName);
    }

    private static bool ContainsWord(string text, string word) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);

    private static bool HasKeyword(string text, List<string> keywords) =>
        keywords.Any(k => !string.IsNullOrWhiteSpace(k) && text.Contains(k.Trim(), StringComparison.OrdinalIgnoreCase));
}
