using System;
using System.IO;
using ChatTerror.Plugin.Gui;
using ChatTerror.Plugin.Gui.Tabs;
using ChatTerror.Plugin.Logic;
using ChatTerror.Plugin.Services;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Core;

public sealed class ChatTerrorPlugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly Configuration config;
    private readonly KeyStore keys;
    private readonly RelayApi api;
    private readonly RelayClient relay;
    private readonly DeviceHub hub;
    private readonly ConnectionManager connection;
    private readonly ChatCapture capture;
    private readonly ChatSender sender;
    private readonly TellDirectory tellDirectory;
    private readonly FriendDirectory friendDirectory;
    private readonly TellRelay tellRelay;
    private readonly TellCommandHook tellHook;
    private readonly WindowSystem windowSystem = new("ChatTerror");
    private readonly ConfigWindow configWindow;
    private readonly DevicesTab devicesTab;
    private string? lastCharacter;

    public ChatTerrorPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        ICondition condition,
        IChatGui chatGui,
        IPlayerState playerState,
        IDataManager dataManager,
        IGameInteropProvider interop,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.playerState = playerState;

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var tokenReadable = config.LoadInstallToken();
        if (!tokenReadable)
            log.Error("The saved install token could not be decrypted, it may belong to another Windows user or machine. Registering a new install.");
        foreach (var (channel, setting) in RelaySettings.Defaults())
            config.Settings.Channels.TryAdd(channel, setting);
        SaveConfig();

        keys = new KeyStore(pluginInterface.ConfigDirectory.FullName, log);
        api = new RelayApi(() => config.RelayUrl);
        relay = new RelayClient(log);
        hub = new DeviceHub(config, SaveConfig, keys, relay, framework, log, CharacterName,
            Path.Combine(pluginInterface.ConfigDirectory.FullName, HistoryFile.Name));
        connection = new ConnectionManager(config, SaveConfig, keys, api, relay, hub, framework, log);
        capture = new ChatCapture(chatGui, playerState, config, hub);
        sender = new ChatSender(framework, clientState, condition, hub, () => config.Settings, log);
        tellDirectory = new TellDirectory(config, SaveConfig, keys, api, hub, framework, playerState, dataManager, log);
        friendDirectory = new FriendDirectory(config, SaveConfig, keys, api, hub, framework, log);
        tellRelay = new TellRelay(config, SaveConfig, keys, api, relay, hub, tellDirectory, capture, chatGui, framework, playerState, log);
        tellHook = new TellCommandHook(interop, tellRelay, log);
        hub.CharacterWorld = HomeWorldName;
        hub.Contacts = () => config.TellsEnabled ? FriendTrust.ForDevices(config.TellCharacters, config.PairedFriends) : [];

        devicesTab = new DevicesTab(config, api, hub, framework);
        configWindow = new ConfigWindow(
        [
            new ConnectionTab(config, connection, SaveConfig),
            new ChannelsTab(config, tellDirectory, SettingsChanged),
            new NotificationsTab(config, SettingsChanged),
            new FiltersTab(config, SettingsChanged),
            devicesTab,
            new FriendsTab(config, friendDirectory),
            new HistoryTab(hub, CharacterName),
            new AdvancedTab(config, hub, SettingsChanged),
        ]);
        windowSystem.AddWindow(configWindow);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;
        framework.Update += OnFrameworkUpdate;

        var command = new CommandInfo(OnCommand) { HelpMessage = "Open the ChatTerror settings." };
        commandManager.AddHandler(ConfigStatic.CommandName, command);
        commandManager.AddHandler(ConfigStatic.CommandAlias, command);

        if (!tokenReadable)
        {
            connection.Notice = "The saved install token could not be decrypted. Paired devices were removed; pair them again.";
            connection.Reregister();
        }
        else if (keys.Replaced && config.InstallToken != null)
        {
            connection.Notice = "The identity key was unreadable and has been replaced. Paired devices were removed; pair them again.";
            connection.Reregister();
        }
        else
        {
            connection.Apply();
        }
    }

    public void Dispose()
    {
        commandManager.RemoveHandler(ConfigStatic.CommandName);
        commandManager.RemoveHandler(ConfigStatic.CommandAlias);
        framework.Update -= OnFrameworkUpdate;
        pluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        windowSystem.RemoveAllWindows();

        connection.Dispose();
        devicesTab.Dispose();
        tellHook.Dispose();
        tellRelay.Dispose();
        friendDirectory.Dispose();
        tellDirectory.Dispose();
        sender.Dispose();
        capture.Dispose();
        relay.Dispose();
        hub.Dispose();
        api.Dispose();
        keys.Dispose();
    }

    private string? CharacterName() => playerState.IsLoaded ? playerState.CharacterName : null;

    private string? HomeWorldName() =>
        playerState.IsLoaded && playerState.HomeWorld.IsValid ? playerState.HomeWorld.Value.Name.ExtractText() : null;

    private void SaveConfig() => pluginInterface.SavePluginConfig(config);

    private void SettingsChanged()
    {
        SaveConfig();
        hub.BroadcastSettings();
    }

    // Devices show which character is online, so push settings whenever it changes.
    private void OnFrameworkUpdate(IFramework _)
    {
        var character = CharacterName();
        if (character == lastCharacter)
            return;
        lastCharacter = character;
        hub.BroadcastSettings();
    }

    private void OpenConfigUi() => configWindow.IsOpen = true;

    private void OnCommand(string command, string args) => configWindow.Toggle();
}
