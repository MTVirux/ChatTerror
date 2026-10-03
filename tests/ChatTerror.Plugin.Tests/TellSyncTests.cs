using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class TellSyncTests
{
    [Fact]
    public void Off_DeletesTheBundleUntilItSucceeds()
    {
        Assert.Equal(TellSyncStep.DeleteBundle, TellSync.Next(enabled: false, deletePending: true, bundleDue: true));
        Assert.Equal(TellSyncStep.Idle, TellSync.Next(enabled: false, deletePending: false, bundleDue: true));
    }

    [Fact]
    public void On_UploadsTheBundleWhenItChanged()
    {
        Assert.Equal(TellSyncStep.Bundle, TellSync.Next(enabled: true, deletePending: false, bundleDue: true));
        Assert.Equal(TellSyncStep.Idle, TellSync.Next(enabled: true, deletePending: false, bundleDue: false));
    }

    [Fact]
    public void BundleEntries_LeaveOutDevicesTheRelayDoesNotKnow()
    {
        var devices = new[] { new TellDevice("d1", "k1", false), new TellDevice("d2", "k2", true), new TellDevice("d3", null, false) };

        var entries = TellSync.BundleEntries("pk", devices, notify: true, known: ["d1", "d2"]);

        Assert.Equal([new TellBundleEntry(TellTargets.Plugin, "pk", false), new TellBundleEntry("d1", "k1", true), new TellBundleEntry("d2", "k2", false)], entries);
        Assert.Equal(3, TellSync.BundleEntries("pk", devices, notify: false, known: null).Count);
    }
}
