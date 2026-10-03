namespace ChatTerror.Plugin.Logic;

public static class ConfigMigration
{
    // Version 3 replaced trust on first use with friend pairing.
    public const int CurrentVersion = 3;

    // Before version 3 turning tells off only unregistered characters, so the bundle may still be on the relay.
    public static bool NeedsBundleDelete(int version, bool tellsEnabled) => version < 3 && !tellsEnabled;
}
