namespace ChatTerror.Plugin;

public static class ConfigStatic
{
    public const string CommandName = "/chatterror";

    public const string CommandAlias = "/ct";

    // To be replaced with the hosted instance.
    public const string DefaultRelayUrl = "http://localhost:5000";

    public const int MinHistorySize = 50;

    public const int MaxHistorySize = 5000;

    public const int MinSendDelayMs = 250;

    public const int MaxSendDelayMs = 10000;
}
