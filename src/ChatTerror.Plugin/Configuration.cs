using System.Collections.Generic;
using ChatTerror.Plugin.Logic;
using ChatTerror.Plugin.Services;
using ChatTerror.Protocol;
using Dalamud.Configuration;
using Newtonsoft.Json;

namespace ChatTerror.Plugin;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

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

    // Decrypts the install token after loading and migrates a plaintext one. Returns false when the stored token
    // could not be decrypted, e.g. a config copied from another machine or Windows user.
    public bool LoadInstallToken()
    {
        Version = 2;
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
}
