namespace ChatTerror.Protocol;

public static class Limits
{
    public const int MaxFrameBytes = 65536;
    public const int MaxTextBytes = 500;
    public const int MaxDevices = 10;
    public const int BacklogChunk = 50;
    public const int DefaultHistory = 500;
    public const int MaxTellEnvelopeChars = 2048;
    public const int MaxTellCharacters = 40;
    public const int MaxQueuedTells = 200;
    public const int MaxPairedFriends = 100;
    public const int MaxFriendInvites = 10;
    public const int MaxFriendEnvelopeChars = 4096;
    public const long TellEchoTimeoutMs = 3000;
    public static readonly TimeSpan TellTtl = TimeSpan.FromDays(7);
    public static readonly TimeSpan FriendInviteTtl = TimeSpan.FromHours(24);
    public static readonly TimeSpan PairingTtl = TimeSpan.FromMinutes(10);
}
