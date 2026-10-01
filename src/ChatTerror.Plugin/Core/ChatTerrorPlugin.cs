using System;
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
    private readonly WindowSystem windowSystem = new("ChatTerror");
    private readonly ConfigWindow configWindow;
    private string? lastCharacter;

    public ChatTerrorPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        ICondition condition,
        IChatGui chatGui,
        IPlayerState playerState,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.playerState = playerState;

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        foreach (var (channel, setting) in RelaySettings.Defaults())
            config.Settings.Channels.TryAdd(channel, setting);
        SaveConfig();

        keys = new KeyStore(pluginInterface.ConfigDirectory.FullName);
        api = new RelayApi(() => config.RelayUrl);
        relay = new RelayClient(log);
        hub = new DeviceHub(config, SaveConfig, keys, relay, framework, log, CharacterName);
        connection = new ConnectionManager(config, SaveConfig, keys, api, relay, framework, log);
        capture = new ChatCapture(chatGui, playerState, config, hub);
        sender = new ChatSender(framework, clientState, condition, hub, () => config.Settings, log);

        configWindow = new ConfigWindow(
        [
            new ConnectionTab(config, connection, hub, SaveConfig),
            new ChannelsTab(config, SettingsChanged),
            new NotificationsTab(config, SettingsChanged),
            new FiltersTab(config, SettingsChanged),
            new DevicesTab(config, api, hub, framework),
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

        connection.Apply();
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

        sender.Dispose();
        capture.Dispose();
        hub.Dispose();
        relay.Dispose();
        api.Dispose();
        keys.Dispose();
    }

    private string? CharacterName() => playerState.IsLoaded ? playerState.CharacterName : null;

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
