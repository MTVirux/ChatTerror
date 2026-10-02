using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Services;

// Fingerprint is null when the request did not arrive during this plugin's active pairing session.
public sealed record PendingPair(string DeviceId, string DeviceName, string DevicePublicKey, string? Fingerprint)
{
    public bool Verified => Fingerprint != null;
}

// Only used from the framework thread; relay events are marshalled there.
public sealed class DeviceHub : IDisposable
{
    private const int MaxPendingPairs = 10;
    private const long SeqSaveIntervalMs = 5_000;
    private const long HistorySaveIntervalMs = 10_000;

    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly KeyStore keys;
    private readonly RelayClient relay;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Func<string?> characterName;
    private readonly string historyPath;
    private readonly Dictionary<string, DeviceSession> sessions = new();
    private readonly List<PendingPair> pendingPairs = new();
    private readonly HashSet<string> onlineDevices = new();
    private readonly PairingGate pairingGate = new();
    private bool seqDirty;
    private long lastSeqSaveAt;
    private long savedHistoryVersion;
    private long lastHistorySaveAt;
    private bool warnedHistoryUnsaved;
    private bool disposed;

    public DeviceHub(Configuration config, Action saveConfig, KeyStore keys, RelayClient relay, IFramework framework, IPluginLog log, Func<string?> characterName, string historyPath)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.keys = keys;
        this.relay = relay;
        this.framework = framework;
        this.log = log;
        this.characterName = characterName;
        this.historyPath = historyPath;
        History = new MessageHistory(config.Settings.HistorySize, LoadHistory());
        savedHistoryVersion = History.Version;

        foreach (var device in config.Devices)
            AddSession(device);

        relay.FrameReceived += OnFrameReceived;
        relay.StateChanged += OnStateChanged;
        framework.Update += OnUpdate;
    }

    public event Action<SendRequest>? SendRequested;

    // Raised on the framework thread each time the relay connection is (re)established.
    public event Action? Connected;

    public event Action<PendingPair>? PairRequested;

    public MessageHistory History { get; }

    public IReadOnlyList<PendingPair> PendingPairs => pendingPairs;

    public IReadOnlySet<string> OnlineDevices => onlineDevices;

    public void Publish(ChatItem item, FilterResult filter)
    {
        item = DeviceSession.Fit(item);
        History.Add(item);
        foreach (var session in sessions.Values)
            SendTo(session, new ChatPayload(item), session.ShouldNotify(item, filter));
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

    public void StartPairing(string secret, long expiresAt) => pairingGate.Start(secret, expiresAt);

    public void CancelPairing() => pairingGate.Cancel();

    public bool Approve(PendingPair pair)
    {
        if (!pair.Verified || !relay.Send(new PairDecisionFrame(pair.DeviceId, true)))
            return false;

        pendingPairs.Remove(pair);
        pairingGate.Forget(pair.DeviceId);
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
        pairingGate.Forget(pair.DeviceId);
        return true;
    }

    public void RemoveDevice(string deviceId)
    {
        pendingPairs.RemoveAll(p => p.DeviceId == deviceId);
        pairingGate.Forget(deviceId);
        sessions.Remove(deviceId);
        onlineDevices.Remove(deviceId);
        if (config.Devices.RemoveAll(d => d.DeviceId == deviceId) > 0)
            saveConfig();
    }

    public void ClearDevices()
    {
        sessions.Clear();
        pendingPairs.Clear();
        pairingGate.Clear();
        onlineDevices.Clear();
        config.Devices.Clear();
        saveConfig();
    }

    public void ClearHistory()
    {
        History.Clear();
        SaveHistory();
    }

    public void Dispose()
    {
        disposed = true;
        relay.FrameReceived -= OnFrameReceived;
        relay.StateChanged -= OnStateChanged;
        framework.Update -= OnUpdate;
        if (seqDirty)
            saveConfig();
        SaveHistory();
    }

    private void OnStateChanged(RelayState state) =>
        framework.RunOnFrameworkThread(() =>
        {
            if (disposed)
                return;
            if (state == RelayState.Connected)
            {
                Connected?.Invoke();
                return;
            }

            onlineDevices.Clear();
            pendingPairs.Clear();
        });

    private void OnFrameReceived(RelayFrame frame) =>
        framework.RunOnFrameworkThread(() =>
        {
            try
            {
                Handle(frame);
            }
            catch (Exception ex)
            {
                log.Error(ex, $"Failed to handle a {frame.GetType().Name} from the relay.");
            }
        });

    private void OnUpdate(IFramework _)
    {
        SaveSeqIfDue();
        var now = Environment.TickCount64;
        if (now - lastHistorySaveAt < HistorySaveIntervalMs)
            return;
        lastHistorySaveAt = now;
        SaveHistory();
    }

    private List<ChatItem> LoadHistory()
    {
        try
        {
            if (HistoryFile.Load(historyPath) is { } items)
                return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        log.Warning("The saved chat history could not be read and was discarded.");
        return [];
    }

    private void SaveHistory()
    {
        var version = History.Version;
        if (version == savedHistoryVersion)
            return;
        savedHistoryVersion = version;
        try
        {
            if (!HistoryFile.Save(historyPath, History.Since(long.MinValue)) && !warnedHistoryUnsaved)
            {
                warnedHistoryUnsaved = true;
                log.Warning("Chat history was not saved: Windows data protection is unavailable.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning(ex, "Failed to save the chat history.");
        }
    }

    private void SaveSeqIfDue()
    {
        var now = Environment.TickCount64;
        if (!seqDirty || now - lastSeqSaveAt < SeqSaveIntervalMs)
            return;
        seqDirty = false;
        lastSeqSaveAt = now;
        saveConfig();
    }

    // Persisted so a reloaded plugin still rejects replays of messages it already acted on.
    private void RememberSeq(DeviceSession session)
    {
        var device = config.Devices.FirstOrDefault(d => d.DeviceId == session.DeviceId);
        if (device == null)
            return;
        device.LastSeenSeq = session.LastSeenSeq;
        seqDirty = true;
        SaveSeqIfDue();
    }

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
        if (payload == null)
        {
            log.Warning($"Dropped an undecryptable or replayed message from device {msg.From}.");
            return;
        }

        switch (payload)
        {
            case HelloPayload hello:
                SendTo(session, BuildSettings(), false);
                foreach (var chunk in DeviceSession.Backlog(History.Since(hello.SinceTs)))
                    SendTo(session, chunk, false);
                break;
            case SendChatPayload send when !config.Enabled:
                RememberSeq(session);
                SendTo(session, new SendResultPayload(send.RequestId, false, SendErrors.Disabled), false);
                break;
            case SendChatPayload send:
                RememberSeq(session);
                SendRequested?.Invoke(new SendRequest(session.DeviceId, send));
                break;
            case PrefsPayload prefs:
                RememberSeq(session);
                UpdatePrefs(session, prefs.MutedChannels, prefs.Channels ?? []);
                break;
        }
    }

    private void HandlePairRequest(PairRequestFrame request)
    {
        if (pendingPairs.Any(p => p.DeviceId == request.DeviceId))
            return;

        if (pendingPairs.Count >= MaxPendingPairs)
        {
            log.Warning($"Rejecting pair request from {request.DeviceName}: too many pending requests.");
            relay.Send(new PairDecisionFrame(request.DeviceId, false));
            return;
        }

        byte[] devicePublic;
        try
        {
            devicePublic = Base64Url.Decode(request.DevicePublicKey);
            P256.ImportPublicRaw(devicePublic).Dispose();
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            log.Warning($"Rejecting pair request with an invalid key from {request.DeviceName}.");
            relay.Send(new PairDecisionFrame(request.DeviceId, false));
            return;
        }

        var secret = pairingGate.Bind(request.DeviceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var fingerprint = secret == null ? null : Fingerprint.Compute(secret, keys.PublicRaw, devicePublic);
        var pair = new PendingPair(request.DeviceId, request.DeviceName, request.DevicePublicKey, fingerprint);
        pendingPairs.Add(pair);
        PairRequested?.Invoke(pair);
    }

    private void UpdatePrefs(DeviceSession session, IReadOnlyList<ChatChannel> muted, IReadOnlyList<ChannelPref> overrides)
    {
        session.Muted.Clear();
        session.Muted.UnionWith(muted);
        session.Overrides.Clear();
        session.Overrides.AddRange(overrides);

        var device = config.Devices.FirstOrDefault(d => d.DeviceId == session.DeviceId);
        if (device == null)
            return;
        device.MutedChannels = muted.Distinct().ToList();
        device.ChannelOverrides = overrides.ToList();
        saveConfig();
    }

    private void AddSession(PairedDevice device)
    {
        try
        {
            var devicePublic = Base64Url.Decode(device.PublicKey);
            var key = E2eCrypto.DeriveKey(keys.Key, devicePublic, keys.PublicRaw, devicePublic);
            sessions[device.DeviceId] = new DeviceSession(device.DeviceId, key, device.MutedChannels, device.LastSeenSeq, device.ChannelOverrides);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            log.Error($"Skipping device {device.Name}: invalid public key.");
        }
    }

    private SettingsPayload BuildSettings()
    {
        var settings = config.Settings;
        var order = settings.OrderedChannels();
        return new SettingsPayload(
            Character: characterName(),
            RelayChannels: order.Where(c => settings.Channels.TryGetValue(c, out var s) && s.Relay).ToList(),
            SendChannels: order.Where(c => settings.Channels.TryGetValue(c, out var s) && s.Send).ToList(),
            MaxLength: Math.Min(config.Settings.MaxLengthBytes, Limits.MaxTextBytes));
    }

    private void SendTo(DeviceSession session, Payload payload, bool notify) =>
        relay.Send(new SendFrame(To: session.DeviceId, Payload: session.Seal(payload), Notify: notify));
}
