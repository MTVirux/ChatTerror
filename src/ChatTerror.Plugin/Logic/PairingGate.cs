using System.Collections.Generic;

namespace ChatTerror.Plugin.Logic;

// Holds the secret of the one active pairing session. The first pair request while it is active takes
// the secret; requests without one are unverified and must not be approved.
public sealed class PairingGate
{
    private readonly Dictionary<string, string> bound = new();
    private string? secret;
    private long expiresAt;

    public void Start(string secret, long expiresAt)
    {
        this.secret = secret;
        this.expiresAt = expiresAt;
    }

    public void Cancel() => secret = null;

    public bool Active(long now) => secret != null && now < expiresAt;

    public string? Bind(string deviceId, long now)
    {
        if (bound.TryGetValue(deviceId, out var existing))
            return existing;
        if (!Active(now))
            return null;

        bound[deviceId] = secret!;
        secret = null;
        return bound[deviceId];
    }

    public void Forget(string deviceId) => bound.Remove(deviceId);

    public void Clear()
    {
        secret = null;
        bound.Clear();
    }
}
