using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Services;

public sealed record PendingPair(string DeviceId, string DeviceName, string DevicePublicKey, string Fingerprint);

// Only used from the framework thread; relay events are marshalled there.
public sealed class DeviceHub : IDisposable
{
    // Leaves room for the envelope, base64 and frame wrapper under the relay's 64 KiB limit.
    private const int MaxPayloadJsonBytes = 32 * 1024;
    private const int MaxItemJsonBytes = MaxPayloadJsonBytes - 512;

    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly KeyStore keys;
    private readonly RelayClient relay;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Func<string?> characterName;
    private readonly Dictionary<string, DeviceSession> sessions = new();
    private readonly List<PendingPair> pendingPairs = new();
    private readonly HashSet<string> onlineDevices = new();
    private bool disposed;

    public DeviceHub(Configuration config, Action saveConfig, KeyStore keys, RelayClient relay, IFramework framework, IPluginLog log, Func<string?> characterName)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.keys = keys;
        this.relay = relay;
        this.framework = framework;
        this.log = log;
        this.characterName = characterName;
        History = new MessageHistory(config.Settings.HistorySize);

        foreach (var device in config.Devices)
            AddSession(device);

        relay.FrameReceived += OnFrameReceived;
        relay.StateChanged += OnStateChanged;
    }

    public event Action<SendRequest>? SendRequested;

    public event Action<PendingPair>? PairRequested;

    public MessageHistory History { get; }

    public IReadOnlyList<PendingPair> PendingPairs => pendingPairs;

    public IReadOnlySet<string> OnlineDevices => onlineDevices;

    public void Publish(ChatItem item, bool filterNotify)
    {
        item = Fit(item);
        History.Add(item);
        foreach (var session in sessions.Values)
            SendTo(session, new ChatPayload(item), session.ShouldNotify(item, filterNotify));
    }

    public void SendResult(string deviceId, SendResultPayload result)
    {
        if (sessions.TryGetValue(deviceId, out var session))
            SendTo(session, result, false);
    }

    public void BroadcastSettings()
    {
        foreach (var session in sessions.Values)
            SendTo(session, BuildSettings(), false);
    }

    public bool Approve(PendingPair pair)
    {
        if (!relay.Send(new PairDecisionFrame(pair.DeviceId, true)))
            return false;

        pendingPairs.Remove(pair);
        var device = new PairedDevice
        {
            DeviceId = pair.DeviceId,
            Name = pair.DeviceName,
            PublicKey = pair.DevicePublicKey,
            PairedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        config.Devices.RemoveAll(d => d.DeviceId == device.DeviceId);
        config.Devices.Add(device);
        saveConfig();
        AddSession(device);
        return true;
    }

    public bool Reject(PendingPair pair)
    {
        if (!relay.Send(new PairDecisionFrame(pair.DeviceId, false)))
            return false;

        pendingPairs.Remove(pair);
        return true;
    }

    public void RemoveDevice(string deviceId)
    {
        pendingPairs.RemoveAll(p => p.DeviceId == deviceId);
        sessions.Remove(deviceId);
        onlineDevices.Remove(deviceId);
        if (config.Devices.RemoveAll(d => d.DeviceId == deviceId) > 0)
            saveConfig();
    }

    public void ClearDevices()
    {
        sessions.Clear();
        pendingPairs.Clear();
        onlineDevices.Clear();
        config.Devices.Clear();
        saveConfig();
    }

    public void Dispose()
    {
        disposed = true;
        relay.FrameReceived -= OnFrameReceived;
        relay.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(RelayState state)
    {
        if (state != RelayState.Connected)
            framework.RunOnFrameworkThread(() =>
            {
                if (!disposed)
                    onlineDevices.Clear();
            });
    }

    private void OnFrameReceived(RelayFrame frame) => framework.RunOnFrameworkThread(() => Handle(frame));

    private void Handle(RelayFrame frame)
    {
        if (disposed)
            return;

        switch (frame)
        {
            case MsgFrame msg:
                HandleMessage(msg);
                break;
            case PairRequestFrame request:
                HandlePairRequest(request);
                break;
            case DeviceRevokedFrame revoked:
                RemoveDevice(revoked.DeviceId);
                break;
            case DeviceOnlineFrame online:
                onlineDevices.Add(online.DeviceId);
                break;
            case DeviceOfflineFrame offline:
                onlineDevices.Remove(offline.DeviceId);
                break;
            case ErrorFrame error:
                log.Warning($"Relay error: {error.Code}");
                break;
        }
    }

    private void HandleMessage(MsgFrame msg)
    {
        if (!sessions.TryGetValue(msg.From, out var session))
            return;

        var payload = session.Open(msg.Payload);
        switch (payload)
        {
            case HelloPayload hello:
                SendTo(session, BuildSettings(), false);
                foreach (var chunk in Backlog(History.Since(hello.SinceTs)))
                    SendTo(session, chunk, false);
                break;
            case SendChatPayload send when !config.Enabled:
                SendTo(session, new SendResultPayload(send.RequestId, false, SendErrors.Disabled), false);
                break;
            case SendChatPayload send:
                SendRequested?.Invoke(new SendRequest(session.DeviceId, send));
                break;
            case PrefsPayload prefs:
                UpdateMuted(session, prefs.MutedChannels);
                break;
        }
    }

    private void HandlePairRequest(PairRequestFrame request)
    {
        if (pendingPairs.Any(p => p.DeviceId == request.DeviceId))
            return;

        string fingerprint;
        try
        {
            var devicePublic = Base64Url.Decode(request.DevicePublicKey);
            P256.ImportPublicRaw(devicePublic).Dispose();
            fingerprint = Fingerprint.Compute(keys.PublicRaw, devicePublic);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            log.Warning($"Rejecting pair request with an invalid key from {request.DeviceName}.");
            relay.Send(new PairDecisionFrame(request.DeviceId, false));
            return;
        }

        var pair = new PendingPair(request.DeviceId, request.DeviceName, request.DevicePublicKey, fingerprint);
        pendingPairs.Add(pair);
        PairRequested?.Invoke(pair);
    }

    private void UpdateMuted(DeviceSession session, IReadOnlyList<ChatChannel> muted)
    {
        session.Muted.Clear();
        session.Muted.UnionWith(muted);

        var device = config.Devices.FirstOrDefault(d => d.DeviceId == session.DeviceId);
        if (device == null)
            return;
        device.MutedChannels = muted.Distinct().ToList();
        saveConfig();
    }

    private void AddSession(PairedDevice device)
    {
        try
        {
            var devicePublic = Base64Url.Decode(device.PublicKey);
            var key = E2eCrypto.DeriveKey(keys.Key, devicePublic, keys.PublicRaw, devicePublic);
            sessions[device.DeviceId] = new DeviceSession(device.DeviceId, key, device.MutedChannels);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            log.Error($"Skipping device {device.Name}: invalid public key.");
        }
    }

    private SettingsPayload BuildSettings()
    {
        var channels = config.Settings.Channels;
        return new SettingsPayload(
            Character: characterName(),
            RelayChannels: channels.Where(c => c.Value.Relay).Select(c => c.Key).Order().ToList(),
            SendChannels: channels.Where(c => c.Value.Send).Select(c => c.Key).Order().ToList(),
            MaxLength: Math.Min(config.Settings.MaxLengthBytes, Limits.MaxTextBytes));
    }

    // Like DeviceSession.Backlog, but also splits by size so long lines never overflow a frame.
    private static IEnumerable<BacklogPayload> Backlog(IReadOnlyList<ChatItem> items)
    {
        var chunk = new List<ChatItem>();
        var size = 0;
        foreach (var item in items)
        {
            var bytes = JsonBytes(item) + 1;
            if (chunk.Count > 0 && (chunk.Count == Limits.BacklogChunk || size + bytes > MaxPayloadJsonBytes))
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
    private static ChatItem Fit(ChatItem item)
    {
        var text = item.Text;
        var fitted = item;
        while (text.Length > 0 && JsonBytes(fitted) > MaxItemJsonBytes)
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

    private void SendTo(DeviceSession session, Payload payload, bool notify) =>
        relay.Send(new SendFrame(To: session.DeviceId, Payload: session.Seal(payload), Notify: notify));
}
