using System.Collections.Generic;
using ChatTerror.Plugin.Logic;
using ChatTerror.Plugin.Services;
using ChatTerror.Protocol;
using Dalamud.Configuration;
using Newtonsoft.Json;

namespace ChatTerror.Plugin;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = ConfigMigration.CurrentVersion;

    public bool Enabled { get; set; } = true;

    public string RelayUrl { get; set; } = ConfigStatic.DefaultRelayUrl;

    public string? InstallId { get; set; }

    // DPAPI encrypted, base64.
    public string? InstallTokenProtected { get; set; }

    // Version 1 stored the token here in plaintext, it is still used when DPAPI is unavailable.
    [JsonProperty("InstallToken")]
    private string? plainInstallToken;

    private string? installToken;

    [JsonIgnore]
    public string? InstallToken
    {
        get => installToken;
        set
        {
            installToken = value;
            InstallTokenProtected = value == null ? null : Secrets.ProtectString(value);
            plainInstallToken = InstallTokenProtected == null ? value : null;
        }
    }

    public RelaySettings Settings { get; set; } = new();

    public List<PairedDevice> Devices { get; set; } = new();

    public bool TellsEnabled { get; set; } = true;

    // Set when tells were turned off and the relay has not confirmed deleting this install's bundle yet.
    public bool TellBundleDeletePending { get; set; }

    public List<TellCharacter> TellCharacters { get; set; } = new();

    public List<PairedFriend> PairedFriends { get; set; } = new();

    public List<OwnFriendInvite> FriendInvites { get; set; } = new();

    public List<PendingFriend> PendingFriends { get; set; } = new();

    // Recently received relayed tell ids, oldest first, so a replay after a restart is still caught.
    public List<string> SeenTellIds { get; set; } = new();

    // Decrypts the install token after loading and migrates a plaintext one. Returns false when the stored token
    // could not be decrypted, e.g. a config copied from another machine or Windows user.
    public bool LoadInstallToken()
    {
        // Version 2 trusted friends' keys on first use, those pins and registrations are not carried over.
        if (ConfigMigration.NeedsBundleDelete(Version, TellsEnabled))
            TellBundleDeletePending = true;
        Version = ConfigMigration.CurrentVersion;
        if (plainInstallToken != null || InstallTokenProtected == null)
        {
            InstallToken = plainInstallToken;
            return true;
        }

        installToken = Secrets.UnprotectString(InstallTokenProtected);
        if (installToken != null)
            return true;
        InstallTokenProtected = null;
        return false;
    }
}

public sealed class PairedDevice
{
    public string DeviceId { get; set; } = "";

    public string Name { get; set; } = "";

    public string PublicKey { get; set; } = "";

    public List<ChatChannel> MutedChannels { get; set; } = new();

    public List<ChannelPref> ChannelOverrides { get; set; } = new();

    public long PairedAt { get; set; }

    public long LastSeenSeq { get; set; }

    // The phone's key for relayed tells, separate from the pairing key.
    public string? TellKey { get; set; }
}

// A friend code we created. The secret never goes to the relay.
public sealed class OwnFriendInvite
{
    public string Id { get; set; } = "";

    // DPAPI encrypted, base64.
    public string? SecretProtected { get; set; }

    // Only used when DPAPI is unavailable.
    public string? PlainSecret { get; set; }

    public string Scope { get; set; } = FriendScopes.Account;

    public long ExpiresAt { get; set; }

    private string? secret;

    [JsonIgnore]
    public string? Secret
    {
        get => secret ??= PlainSecret ?? (SecretProtected is { } value ? Secrets.UnprotectString(value) : null);
        set
        {
            secret = value;
            SecretProtected = value == null ? null : Secrets.ProtectString(value);
            PlainSecret = SecretProtected == null ? value : null;
        }
    }
}
