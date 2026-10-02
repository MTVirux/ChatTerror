using System.Collections.Generic;
using System.Linq;

namespace ChatTerror.Plugin.Logic;

public sealed class SeenIds(int capacity)
{
    private readonly HashSet<string> set = new();
    private readonly Queue<string> order = new();

    public SeenIds(int capacity, IEnumerable<string> ids) : this(capacity)
    {
        foreach (var id in ids)
            Add(id);
    }

    // Oldest first.
    public List<string> Ids => order.ToList();

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
