using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public sealed class DeviceSession
{
    private readonly byte[] sharedKey;
    private readonly SeqCounter counter = new();
    private readonly SeqGuard guard = new();

    public DeviceSession(string deviceId, byte[] sharedKey, IReadOnlyCollection<ChatChannel> muted)
    {
        DeviceId = deviceId;
        this.sharedKey = sharedKey;
        Muted = new HashSet<ChatChannel>(muted);
    }

    public string DeviceId { get; }

    public HashSet<ChatChannel> Muted { get; }

    public string Seal(Payload p) =>
        Payloads.SealPayload(sharedKey, Direction.PluginToDevice, p with { Seq = counter.Next() });

    public Payload? Open(string envelope)
    {
        Payload payload;
        try
        {
            payload = Payloads.OpenPayload(sharedKey, Direction.DeviceToPlugin, envelope);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or NotSupportedException)
        {
            return null;
        }

        return guard.Accept(payload.Seq) ? payload : null;
    }

    public bool ShouldNotify(ChatItem item, bool filterNotify) => filterNotify && !Muted.Contains(item.Channel);

    public static IEnumerable<BacklogPayload> Backlog(IReadOnlyList<ChatItem> items)
    {
        if (items.Count == 0)
        {
            yield return new BacklogPayload([], true);
            yield break;
        }

        var chunks = items.Chunk(Limits.BacklogChunk).ToList();
        for (var i = 0; i < chunks.Count; i++)
            yield return new BacklogPayload(chunks[i], i == chunks.Count - 1);
    }
}
