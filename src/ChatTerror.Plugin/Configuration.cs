using System.Collections.Generic;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Configuration;

namespace ChatTerror.Plugin;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public string RelayUrl { get; set; } = ConfigStatic.DefaultRelayUrl;

    public string? InstallId { get; set; }

    public string? InstallToken { get; set; }

    public RelaySettings Settings { get; set; } = new();

    public List<PairedDevice> Devices { get; set; } = new();
}

public sealed class PairedDevice
{
    public string DeviceId { get; set; } = "";

    public string Name { get; set; } = "";

    public string PublicKey { get; set; } = "";

    public List<ChatChannel> MutedChannels { get; set; } = new();

    public long PairedAt { get; set; }
}
