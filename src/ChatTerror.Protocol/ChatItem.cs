namespace ChatTerror.Protocol;

public sealed record ChatItem(
    string Id,
    long Ts,
    ChatChannel Channel,
    string Sender,
    string? SenderWorld,
    string Text,
    string Character,
    bool Outgoing);
