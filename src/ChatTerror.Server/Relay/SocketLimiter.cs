namespace ChatTerror.Server.Relay;

// Caps open sockets per client, so one address cannot take every WebSocket slot.
public sealed class SocketLimiter(int maxPerClient)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, int> open = new();

    public bool TryAcquire(string client)
    {
        lock (gate)
        {
            var current = open.GetValueOrDefault(client);
            if (current >= maxPerClient)
                return false;

            open[client] = current + 1;
            return true;
        }
    }

    public void Release(string client)
    {
        lock (gate)
        {
            if (--open[client] == 0)
                open.Remove(client);
        }
    }
}
