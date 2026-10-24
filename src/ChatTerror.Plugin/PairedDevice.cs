using System.Collections.Generic;
using ChatTerror.Protocol;
using Newtonsoft.Json;

namespace ChatTerror.Plugin;

public sealed class PairedDevice
{
    public string DeviceId { get; set; } = "";

    public string Name { get; set; } = "";

    public string PublicKey { get; set; } = "";

    public List<ChatChannel> MutedChannels { get; set; } = new();

    public List<ChannelPref> ChannelOverrides { get; set; } = new();

    // Newtonsoft would otherwise load into the shared default instance in place.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public AlertPrefs Alerts { get; set; } = AlertPrefs.Default;

    public long PairedAt { get; set; }

    public long LastSeenSeq { get; set; }

    // The phone's key for relayed tells, separate from the pairing key.
    public string? TellKey { get; set; }
}
