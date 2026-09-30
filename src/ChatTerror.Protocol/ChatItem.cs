using System.Runtime.InteropServices;

namespace ChatTerror.Protocol;

public sealed record ChatItem(
    string Id,
    long Ts,
    ChatChannel Channel,
    string Sender,
    [Optional, DefaultParameterValue(null)] string? SenderWorld,
    string Text,
    string Character,
    bool Outgoing);
