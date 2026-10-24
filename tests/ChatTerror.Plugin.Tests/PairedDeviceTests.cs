using ChatTerror.Protocol;
using Newtonsoft.Json;

namespace ChatTerror.Plugin.Tests;

public class PairedDeviceTests
{
    // The settings Dalamud saves and loads plugin configs with.
    private static readonly JsonSerializerSettings ConfigJson = new()
    {
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        TypeNameHandling = TypeNameHandling.Objects,
    };

    private static PairedDevice RoundTrip(PairedDevice device) =>
        JsonConvert.DeserializeObject<PairedDevice>(JsonConvert.SerializeObject(device, ConfigJson), ConfigJson)!;

    [Fact]
    public void Load_WithoutAlerts_GetsDefault()
    {
        var device = JsonConvert.DeserializeObject<PairedDevice>("{\"DeviceId\":\"dev1\",\"Name\":\"Phone\",\"LastSeenSeq\":5}", ConfigJson)!;

        Assert.Equal("dev1", device.DeviceId);
        Assert.Equal(5, device.LastSeenSeq);
        Assert.Equal(AlertPrefs.Default, device.Alerts);
    }

    [Fact]
    public void Alerts_RoundTrip()
    {
        var loaded = RoundTrip(new PairedDevice { DeviceId = "dev1", Alerts = new AlertPrefs(false, true, 30) });

        Assert.Equal(new AlertPrefs(false, true, 30), loaded.Alerts);
    }

    [Fact]
    public void Load_LeavesSharedDefaultAlone()
    {
        RoundTrip(new PairedDevice { Alerts = new AlertPrefs(false, false, 0) });

        Assert.Equal(new AlertPrefs(true, true, Limits.DefaultWaitSeconds), AlertPrefs.Default);
        Assert.Equal(new AlertPrefs(true, true, Limits.DefaultWaitSeconds), new PairedDevice().Alerts);
    }
}
