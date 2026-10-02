using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Plugin.Services;
using World = Lumina.Excel.Sheets.World;

namespace ChatTerror.Plugin.Services;

// Keeps this install's characters, friend lists and tell bundle in sync with the relay. Framework thread only.
public sealed class TellDirectory : IDisposable
{
    private const long ScanIntervalMs = 10_000;
    private const long RefreshIntervalMs = 10 * 60_000;

    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly KeyStore keys;
    private readonly RelayApi api;
    private readonly DeviceHub hub;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly IDataManager data;
    private readonly IPluginLog log;
    private readonly Dictionary<string, (string Friends, long At)> uploaded = new();
    private string? uploadedBundle;
    private long lastScan = long.MinValue / 2;
    private TellSyncStep lastStep;
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

    // Re-uploads everything, e.g. after reconnecting or re-registering.
    private void Reset()
    {
        uploaded.Clear();
        uploadedBundle = null;
        lastScan = long.MinValue / 2;
    }

    private void OnUpdate(IFramework unused)
    {
        if (busy || config.InstallToken is not { } token)
            return;

        // Persisted, so turning tells off while the relay is unreachable still unregisters later.
        if (wasEnabled != config.TellsEnabled)
        {
            wasEnabled = config.TellsEnabled;
            config.TellsUnregisterPending = !config.TellsEnabled;
            saveConfig();
            Reset();
        }

        var now = Environment.TickCount64;
        if (now - lastScan < ScanIntervalMs)
            return;
        lastScan = now;

        var bundleKey = ProtocolJson.Serialize(BundleEntries(null));
        var character = config.TellsEnabled ? ScanCharacter() : null;
        var friends = character == null ? [] : TellContacts.Uploadable(character, config.Settings);
        var friendsKey = string.Join(',', friends.Order());
        var characterDue = character != null
            && (!uploaded.TryGetValue(character.Hash, out var last) || last.Friends != friendsKey || now - last.At >= RefreshIntervalMs);

        lastStep = TellSync.Next(config.TellsEnabled, config.TellsUnregisterPending, bundleKey != uploadedBundle, characterDue, lastStep);
        switch (lastStep)
        {
            case TellSyncStep.Unregister:
                Run(async () =>
                {
                    await api.DeleteTellCharacters(token);
                    await framework.RunOnFrameworkThread(() =>
                    {
                        config.TellsUnregisterPending = false;
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
            case TellSyncStep.Character:
                Run(() => UploadCharacter(token, character!, friends, friendsKey, now));
                break;
        }
    }

    private async Task UploadCharacter(string token, TellCharacter character, List<string> friends, string friendsKey, long now)
    {
        List<string> registered;
        try
        {
            // Only friends we uploaded count, so the relay can't grow our pins with arbitrary hashes.
            registered = (await api.PutTellCharacter(token, character.Hash, friends)).Intersect(friends).ToList();
        }
        catch (RelayApiException ex) when (ex.StatusCode == 409)
        {
            await framework.RunOnFrameworkThread(() =>
                Status = "This character is registered to another ChatTerror install that was used in the last 30 days.");
            throw;
        }
        await framework.RunOnFrameworkThread(() =>
        {
            uploaded[character.Hash] = (friendsKey, now);
            character.Registered = registered;
            saveConfig();
            hub.BroadcastSettings();
        });

        // Trusting friends' keys up front lets phones follow the plugin's pins, also for friends never written to.
        var unpinned = await framework.RunOnFrameworkThread(() => registered.Where(h => !config.TellPins.ContainsKey(h)).ToList());
        foreach (var hash in unpinned)
        {
            if (await api.GetTellBundle(token, hash) is not { } signed || TellBundles.Verify(signed, null) is not { } bundle)
                continue;
            await framework.RunOnFrameworkThread(() =>
            {
                if (!config.TellPins.ContainsKey(hash) && TellContacts.AcceptBundle(config.TellBundleIssuedAt, hash, bundle.IssuedAt))
                    config.TellPins[hash] = bundle.InstallPublicKey;
                saveConfig();
            });
        }
        if (unpinned.Count > 0)
            await framework.RunOnFrameworkThread(hub.BroadcastSettings);
    }

    // Reads the logged-in character and its friend list into config. Null when nothing is loaded yet.
    private TellCharacter? ScanCharacter()
    {
        if (CurrentHash is not { } hash)
            return null;
        var friends = FriendListReader.Read();
        if (friends.Count == 0)
        {
            Status = "Open your friend list once so ChatTerror can read it.";
            return null;
        }
        Status = null;

        var character = config.TellCharacters.FirstOrDefault(c => c.Hash == hash);
        if (character == null)
        {
            character = new TellCharacter { Hash = hash };
            config.TellCharacters.Add(character);
        }
        character.Name = playerState.CharacterName;
        character.World = WorldName(playerState.HomeWorld.RowId);
        character.Friends = friends
            .Select(f => new TellFriend { Hash = TellHash.Compute(f.ContentId), Name = f.Name, World = WorldName(f.HomeWorld) })
            .ToList();
        return character;
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
