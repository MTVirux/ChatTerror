namespace ChatTerror.Server;

public sealed class RelayOptions
{
    public const string Section = "Relay";

    public string DbPath { get; set; } = "chatterror.db";

    public string VapidPublicKey { get; set; } = "";

    public string VapidPrivateKey { get; set; } = "";

    public string VapidSubject { get; set; } = "mailto:admin@localhost";

    // Lists are comma separated, so each fits in a single environment variable.
    public string AllowedOrigins { get; set; } = "";

    // IPs or CIDRs whose X-Forwarded-* headers are trusted. Empty disables forwarded headers.
    public string TrustedProxies { get; set; } = "";

    public double FramesPerSecond { get; set; } = 20;

    public int FrameBurst { get; set; } = 40;

    public TimeSpan AuthTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public int PairingRequestsPerMinute { get; set; } = 10;

    public int InstallsPerHour { get; set; } = 5;

    public TimeSpan PendingDeviceTtl { get; set; } = TimeSpan.FromHours(1);

    public int MaxConcurrentPushes { get; set; } = 32;

    public int MaxPushesPerInstall { get; set; } = 4;

    public TimeSpan PushTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public long MaxConcurrentConnections { get; set; } = 10000;

    public long MaxConcurrentUpgradedConnections { get; set; } = 5000;

    public long MaxRequestBodyBytes { get; set; } = 16 * 1024;

    public string[] GetAllowedOrigins() => SplitList(AllowedOrigins).Select(origin => origin.TrimEnd('/')).ToArray();

    public string[] GetTrustedProxies() => SplitList(TrustedProxies);

    private static string[] SplitList(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
