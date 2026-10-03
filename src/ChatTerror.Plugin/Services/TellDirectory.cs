using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Plugin.Services;
using World = Lumina.Excel.Sheets.World;

namespace ChatTerror.Plugin.Services;

// Reads this install's characters and friend lists locally and keeps its tell bundle on the relay. Framework thread only.
public sealed class TellDirectory : IDisposable
{
    private const long ScanIntervalMs = 10_000;

    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly KeyStore keys;
    private readonly RelayApi api;
    private readonly DeviceHub hub;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly IDataManager data;
    private readonly IPluginLog log;
    private string? uploadedBundle;
    private long lastScan = long.MinValue / 2;
    private bool busy;
    private bool wasEnabled;

    public TellDirectory(Configuration config, Action saveConfig, KeyStore keys, RelayApi api, DeviceHub hub, IFramework framework,
        IPlayerState playerState, IDataManager data, IPluginLog log)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.keys = keys;
        this.api = api;
        this.hub = hub;
        this.framework = framework;
        this.playerState = playerState;
        this.data = data;
        this.log = log;
        wasEnabled = config.TellsEnabled;
        hub.Connected += Reset;
        framework.Update += OnUpdate;
    }

    public string? CurrentHash => playerState.IsLoaded && playerState.ContentId != 0 ? TellHash.Compute(playerState.ContentId) : null;

    public string? Status { get; private set; }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        hub.Connected -= Reset;
    }

    // Re-uploads the bundle, e.g. after reconnecting or re-registering.
    private void Reset()
    {
        uploadedBundle = null;
        lastScan = long.MinValue / 2;
    }

    private void OnUpdate(IFramework unused)
    {
        if (busy || config.InstallToken is not { } token)
            return;

        // Persisted, so turning tells off while the relay is unreachable still deletes the bundle later.
        if (wasEnabled != config.TellsEnabled)
        {
            wasEnabled = config.TellsEnabled;
            config.TellBundleDeletePending = !config.TellsEnabled;
            saveConfig();
            Reset();
        }

        var now = Environment.TickCount64;
        if (now - lastScan < ScanIntervalMs)
            return;
        lastScan = now;

        if (config.TellsEnabled)
            ScanCharacter();

        var bundleKey = ProtocolJson.Serialize(BundleEntries(null));
        switch (TellSync.Next(config.TellsEnabled, config.TellBundleDeletePending, bundleKey != uploadedBundle))
        {
            case TellSyncStep.DeleteBundle:
                Run(async () =>
                {
                    await api.DeleteTellBundle(token);
                    await framework.RunOnFrameworkThread(() =>
                    {
                        config.TellBundleDeletePending = false;
                        saveConfig();
                    });
                });
                break;
            case TellSyncStep.Bundle:
                Run(async () =>
                {
                    var known = (await api.ListDevices(token)).Select(d => d.DeviceId).ToList();
                    var entries = await framework.RunOnFrameworkThread(() => BundleEntries(known));
                    var bundle = new TellBundle(keys.PublicKey, entries, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    await api.PutTellBundle(token, TellBundles.Sign(keys.Key, bundle));
                    await framework.RunOnFrameworkThread(() => uploadedBundle = bundleKey);
                });
                break;
        }
    }

    // Reads the logged-in character and its friend list into config, telling the phones when it changed.
    private void ScanCharacter()
    {
        if (CurrentHash is not { } hash)
            return;
        var friends = FriendListReader.Read();
        if (friends.Count == 0)
        {
            Status = "Open your friend list once so ChatTerror can read it.";
            return;
        }
        Status = null;

        var character = config.TellCharacters.FirstOrDefault(c => c.Hash == hash);
        if (character == null)
        {
            character = new TellCharacter { Hash = hash };
            config.TellCharacters.Add(character);
        }
        var before = ProtocolJson.Serialize(character);
        character.Name = playerState.CharacterName;
        character.World = WorldName(playerState.HomeWorld.RowId);
        character.Friends = friends
            .Select(f => new TellFriend { Hash = TellHash.Compute(f.ContentId), Name = f.Name, World = WorldName(f.HomeWorld) })
            .ToList();
        if (ProtocolJson.Serialize(character) == before)
            return;
        saveConfig();
        hub.BroadcastSettings();
    }

    private List<TellBundleEntry> BundleEntries(IReadOnlyCollection<string>? known)
    {
        var tellPush = config.Settings.Channels.TryGetValue(ChatChannel.Tell, out var setting) && setting.Push;
        var devices = config.Devices.Select(d => new TellDevice(d.DeviceId, d.TellKey, d.MutedChannels.Contains(ChatChannel.Tell)));
        return TellSync.BundleEntries(keys.PublicKey, devices, tellPush || config.Settings.PushOnTell, known);
    }

    private string WorldName(uint id) =>
        data.GetExcelSheet<World>().GetRowOrDefault(id) is { } row ? row.Name.ExtractText() : "";

    private void Run(Func<Task> work)
    {
        busy = true;
        Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                log.Warning($"Tell directory update failed: {ex.Message}");
            }
            finally
            {
                await framework.RunOnFrameworkThread(() => busy = false);
            }
        });
    }
}
