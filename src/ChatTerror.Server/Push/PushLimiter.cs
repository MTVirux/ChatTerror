namespace ChatTerror.Server.Push;

// Caps in-flight pushes globally and per install, so one busy install cannot starve the rest.
// Urgent pushes have their own per install allowance, so an alert isn't lost behind chat pushes.
public sealed class PushLimiter(int maxTotal, int maxPerInstall)
{
    private readonly Lock gate = new();
    private readonly Dictionary<(string InstallId, bool Urgent), int> perInstall = new();
    private int total;

    public bool TryAcquire(string installId, bool urgent = false)
    {
        lock (gate)
        {
            var key = (installId, urgent);
            var current = perInstall.GetValueOrDefault(key);
            if (total >= maxTotal || current >= maxPerInstall)
                return false;

            perInstall[key] = current + 1;
            total++;
            return true;
        }
    }

    public void Release(string installId, bool urgent = false)
    {
        lock (gate)
        {
            var key = (installId, urgent);
            total--;
            if (--perInstall[key] == 0)
                perInstall.Remove(key);
        }
    }
}
