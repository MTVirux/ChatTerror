using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public sealed class DeviceSession
{
    // Leaves room for the envelope, base64 and frame wrapper under the relay's 64 KiB limit.
    public const int MaxPayloadJsonBytes = 32 * 1024;
    private const int PayloadWrapperBytes = 512;

    private readonly byte[] sharedKey;
    private readonly SeqCounter counter = new();
    private readonly SeqGuard guard;

    public DeviceSession(string deviceId, byte[] sharedKey, IReadOnlyCollection<ChatChannel> muted, long lastSeenSeq = 0)
    {
        DeviceId = deviceId;
        this.sharedKey = sharedKey;
        Muted = new HashSet<ChatChannel>(muted);
        guard = new SeqGuard(lastSeenSeq);
    }

    public string DeviceId { get; }

    public long LastSeenSeq => guard.LastSeen;

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

    // Splits by count and by size so long lines never overflow a frame.
    public static IEnumerable<BacklogPayload> Backlog(IReadOnlyList<ChatItem> items, int maxBytes = MaxPayloadJsonBytes)
    {
        var chunk = new List<ChatItem>();
        var size = 0;
        foreach (var original in items)
        {
            var item = Fit(original, maxBytes);
            var bytes = JsonBytes(item) + 1;
            if (chunk.Count > 0 && (chunk.Count == Limits.BacklogChunk || size + bytes > maxBytes))
            {
                yield return new BacklogPayload(chunk, false);
                chunk = new List<ChatItem>();
                size = 0;
            }

            chunk.Add(item);
            size += bytes;
        }

        yield return new BacklogPayload(chunk, true);
    }

    // Game text is not length capped, so oversized lines are cut until they fit in one frame.
    public static ChatItem Fit(ChatItem item, int maxBytes = MaxPayloadJsonBytes)
    {
        var limit = maxBytes - PayloadWrapperBytes;
        var text = item.Text;
        var fitted = item;
        while (text.Length > 0 && JsonBytes(fitted) > limit)
        {
            var length = text.Length / 2;
            if (length > 0 && char.IsHighSurrogate(text[length - 1]))
                length--;
            text = text[..length];
            fitted = item with { Text = text + "..." };
        }

        return fitted;
    }

    private static int JsonBytes(ChatItem item) => Encoding.UTF8.GetByteCount(ProtocolJson.Serialize(item));
}
