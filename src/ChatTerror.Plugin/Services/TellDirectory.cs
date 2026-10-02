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

    private void OnUpdate(IFramework _)
    {
        if (busy || config.InstallToken is not { } token)
            return;

        if (wasEnabled && !config.TellsEnabled)
        {
            wasEnabled = false;
            Reset();
            Status = null;
            Run(() => api.DeleteTellCharacters(token));
            return;
        }
        wasEnabled = config.TellsEnabled;
        if (!config.TellsEnabled)
            return;

        var now = Environment.TickCount64;
        if (now - lastScan < ScanIntervalMs)
            return;
        lastScan = now;

        var entries = BundleEntries();
        var bundleKey = ProtocolJson.Serialize(entries);
        if (bundleKey != uploadedBundle)
        {
            var signed = TellBundles.Sign(keys.Key, new TellBundle(keys.PublicKey, entries, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            Run(async () =>
            {
                await api.PutTellBundle(token, signed);
                await framework.RunOnFrameworkThread(() => uploadedBundle = bundleKey);
            });
            return;
        }

        if (ScanCharacter() is not { } character)
            return;
        var friends = TellContacts.Uploadable(character, config.Settings);
        var friendsKey = string.Join(',', friends.Order());
        if (uploaded.TryGetValue(character.Hash, out var last) && last.Friends == friendsKey && now - last.At < RefreshIntervalMs)
            return;

        Run(async () =>
        {
            var registered = await api.PutTellCharacter(token, character.Hash, friends);
            await framework.RunOnFrameworkThread(() =>
            {
                uploaded[character.Hash] = (friendsKey, now);
                character.Registered = registered;
                saveConfig();
                hub.BroadcastSettings();
            });
        });
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

    private List<TellBundleEntry> BundleEntries()
    {
        var entries = new List<TellBundleEntry> { new(TellTargets.Plugin, keys.PublicKey, false) };
        var tellPush = config.Settings.Channels.TryGetValue(ChatChannel.Tell, out var setting) && setting.Push;
        var notify = tellPush || config.Settings.PushOnTell;
        foreach (var device in config.Devices.Where(d => d.TellKey != null))
            entries.Add(new TellBundleEntry(device.DeviceId, device.TellKey!, notify && !device.MutedChannels.Contains(ChatChannel.Tell)));
        return entries;
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
