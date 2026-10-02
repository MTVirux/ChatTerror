using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class TellSyncTests
{
    [Fact]
    public void Off_UnregistersUntilItSucceeds()
    {
        Assert.Equal(TellSyncStep.Unregister, TellSync.Next(enabled: false, unregisterPending: true, bundleDue: true, characterDue: true, TellSyncStep.Unregister));
        Assert.Equal(TellSyncStep.Idle, TellSync.Next(enabled: false, unregisterPending: false, bundleDue: true, characterDue: true, TellSyncStep.Idle));
    }

    [Fact]
    public void FailingBundle_DoesNotStarveTheCharacter()
    {
        Assert.Equal(TellSyncStep.Bundle, TellSync.Next(true, false, bundleDue: true, characterDue: true, TellSyncStep.Idle));
        Assert.Equal(TellSyncStep.Character, TellSync.Next(true, false, bundleDue: true, characterDue: true, TellSyncStep.Bundle));
        Assert.Equal(TellSyncStep.Bundle, TellSync.Next(true, false, bundleDue: true, characterDue: false, TellSyncStep.Bundle));
        Assert.Equal(TellSyncStep.Idle, TellSync.Next(true, false, bundleDue: false, characterDue: false, TellSyncStep.Character));
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
