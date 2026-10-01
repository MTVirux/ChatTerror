namespace ChatTerror.Server;

public sealed class RelayOptions
{
    public const string Section = "Relay";

    public string DbPath { get; set; } = "chatterror.db";

    public string VapidPublicKey { get; set; } = "";

    public string VapidPrivateKey { get; set; } = "";

    public string VapidSubject { get; set; } = "mailto:admin@localhost";

    // Comma separated, so it fits in a single environment variable.
    public string AllowedOrigins { get; set; } = "";

    public double FramesPerSecond { get; set; } = 20;

    public int FrameBurst { get; set; } = 40;

    public TimeSpan AuthTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public int PairingRequestsPerMinute { get; set; } = 10;

    public TimeSpan PendingDeviceTtl { get; set; } = TimeSpan.FromHours(1);

    public string[] GetAllowedOrigins() =>
        AllowedOrigins.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(origin => origin.TrimEnd('/'))
            .ToArray();
}
