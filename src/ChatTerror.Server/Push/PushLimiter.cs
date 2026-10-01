namespace ChatTerror.Server.Push;

// Caps in-flight pushes globally and per install, so one busy install cannot starve the rest.
public sealed class PushLimiter(int maxTotal, int maxPerInstall)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, int> perInstall = new();
    private int total;

    public bool TryAcquire(string installId)
    {
        lock (gate)
        {
            var current = perInstall.GetValueOrDefault(installId);
            if (total >= maxTotal || current >= maxPerInstall)
                return false;

            perInstall[installId] = current + 1;
            total++;
            return true;
        }
    }

    public void Release(string installId)
    {
        lock (gate)
        {
            total--;
            if (--perInstall[installId] == 0)
                perInstall.Remove(installId);
        }
    }
}
