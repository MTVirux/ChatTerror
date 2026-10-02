using System.Collections.Generic;
using System.Linq;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public enum TellSyncStep { Idle, Unregister, Bundle, Character }

public sealed record TellDevice(string DeviceId, string? TellKey, bool TellMuted);

public static class TellSync
{
    // Unregistering is retried until it succeeds. Bundle and character uploads take turns, so a bundle the relay keeps
    // rejecting can't block the character upload.
    public static TellSyncStep Next(bool enabled, bool unregisterPending, bool bundleDue, bool characterDue, TellSyncStep last)
    {
        if (!enabled)
            return unregisterPending ? TellSyncStep.Unregister : TellSyncStep.Idle;
        if (bundleDue && (last != TellSyncStep.Bundle || !characterDue))
            return TellSyncStep.Bundle;
        return characterDue ? TellSyncStep.Character : TellSyncStep.Idle;
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
