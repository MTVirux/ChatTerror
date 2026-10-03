using System.Collections.Generic;
using System.Linq;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public enum TellSyncStep { Idle, DeleteBundle, Bundle }

public sealed record TellDevice(string DeviceId, string? TellKey, bool TellMuted);

public static class TellSync
{
    // Deleting the bundle is retried until it succeeds.
    public static TellSyncStep Next(bool enabled, bool deletePending, bool bundleDue)
    {
        if (!enabled)
            return deletePending ? TellSyncStep.DeleteBundle : TellSyncStep.Idle;
        return bundleDue ? TellSyncStep.Bundle : TellSyncStep.Idle;
    }

    // known is the relay's device list; a device it already dropped would make it reject the whole bundle.
    public static List<TellBundleEntry> BundleEntries(string pluginKey, IEnumerable<TellDevice> devices, bool notify, IReadOnlyCollection<string>? known)
    {
        var entries = new List<TellBundleEntry> { new(TellTargets.Plugin, pluginKey, false) };
        foreach (var device in devices.Where(d => d.TellKey != null && (known == null || known.Contains(d.DeviceId))))
            entries.Add(new TellBundleEntry(device.DeviceId, device.TellKey!, notify && !device.TellMuted));
        return entries;
    }
}
