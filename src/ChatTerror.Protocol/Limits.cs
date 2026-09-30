namespace ChatTerror.Protocol;

public static class Limits
{
    public const int MaxFrameBytes = 65536;
    public const int MaxTextBytes = 500;
    public const int MaxDevices = 10;
    public const int BacklogChunk = 50;
    public const int DefaultHistory = 500;
    public static readonly TimeSpan PairingTtl = TimeSpan.FromMinutes(10);
}
