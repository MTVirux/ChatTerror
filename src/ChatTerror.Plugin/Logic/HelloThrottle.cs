using System;
using System.Collections.Generic;

namespace ChatTerror.Plugin.Logic;

// Serves at most one hello per device per cooldown. A hello inside the cooldown is held and served
// once the cooldown ends, so a phone that reconnects quickly still gets its backlog.
public sealed class HelloThrottle
{
    public const long CooldownMs = 5_000;

    private readonly Dictionary<string, long> lastServedAt = new();
    private readonly Dictionary<string, long> pendingSince = new();

    public bool TryServe(string deviceId, long sinceTs, long now)
    {
        if (lastServedAt.TryGetValue(deviceId, out var last) && now - last < CooldownMs)
        {
            pendingSince[deviceId] = pendingSince.TryGetValue(deviceId, out var held) ? Math.Min(held, sinceTs) : sinceTs;
            return false;
        }

        lastServedAt[deviceId] = now;
        pendingSince.Remove(deviceId);
        return true;
    }

    public List<(string DeviceId, long SinceTs)> TakeDue(long now)
    {
        var due = new List<(string DeviceId, long SinceTs)>();
        foreach (var (deviceId, sinceTs) in pendingSince)
        {
            if (now - lastServedAt[deviceId] >= CooldownMs)
                due.Add((deviceId, sinceTs));
        }

        foreach (var (deviceId, _) in due)
        {
            pendingSince.Remove(deviceId);
            lastServedAt[deviceId] = now;
        }

        return due;
    }

    public void Forget(string deviceId)
    {
        lastServedAt.Remove(deviceId);
        pendingSince.Remove(deviceId);
    }

    public void Clear()
    {
        lastServedAt.Clear();
        pendingSince.Clear();
    }
}
