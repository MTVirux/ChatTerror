using System.Collections.Generic;

namespace ChatTerror.Plugin.Logic;

public sealed class SeenIds(int capacity)
{
    private readonly HashSet<string> set = new();
    private readonly Queue<string> order = new();

    public bool Add(string id)
    {
        if (!set.Add(id))
            return false;
        order.Enqueue(id);
        if (order.Count > capacity)
            set.Remove(order.Dequeue());
        return true;
    }
}
