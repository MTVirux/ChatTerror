using System;
using System.Linq;
using ChatTerror.Plugin.Core;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace ChatTerror.Plugin.Services;

public sealed class ChatCapture : IDisposable
{
    private readonly IChatGui chatGui;
    private readonly IPlayerState playerState;
    private readonly Configuration config;
    private readonly DeviceHub hub;

    // Relayed tells TellRelay printed, which TellRelay also publishes itself with their own id.
    public PrintedTells Printed { get; } = new(50);

    // Tells from the game itself, never ones this plugin printed.
    public event Action<IHandleableChatMessage>? GameTell;

    public ChatCapture(IChatGui chatGui, IPlayerState playerState, Configuration config, DeviceHub hub)
    {
        this.chatGui = chatGui;
        this.playerState = playerState;
        this.config = config;
        this.hub = hub;
        chatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose() => chatGui.ChatMessage -= OnChatMessage;

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (message.LogKind is XivChatType.TellIncoming or XivChatType.TellOutgoing)
        {
            if (Printed.Take(PrintedTells.Key(message.LogKind == XivChatType.TellOutgoing, message.Message.TextValue)))
                return;
            GameTell?.Invoke(message);
        }

        if (!config.Enabled || config.Devices.Count == 0)
            return;

        var type = message.LogKind;
        if (ChannelMap.FromXivChatType(type) is not { } channel)
            return;

        var me = playerState.IsLoaded ? playerState.CharacterName : null;

        // For outgoing tells the sender string holds the recipient, which is what the phone groups by.
        var player = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        var sender = player?.PlayerName ?? StripGlyphs(message.Sender.TextValue);
        var outgoing = ChannelMap.IsOutgoing(type)
            || message.SourceKind == XivChatRelationKind.LocalPlayer
            || (type != XivChatType.TellIncoming && me != null && string.Equals(sender, me, StringComparison.OrdinalIgnoreCase) && IsHomeWorld(player));

        // Players on the local world often carry no world in the payload, and own lines carry no payload at all.
        // System, NPC and battle lines only get a world from their payload.
        var world = WorldName(player?.World)
            ?? (!SendValidator.CanSend(channel) || sender.Length == 0 ? null
                : outgoing && !ChannelMap.IsOutgoing(type) ? WorldName(playerState.HomeWorld) : WorldName(playerState.CurrentWorld));

        var text = message.Message.TextValue;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = ChatFilter.Evaluate(
            new IncomingChat(channel, sender, world, text, outgoing, ts),
            config.Settings,
            me,
            TimeOnly.FromDateTime(DateTime.Now));
        if (!result.Relay)
            return;

        var item = new ChatItem(
            Id: Guid.NewGuid().ToString("N"),
            Ts: ts,
            Channel: channel,
            Sender: sender,
            SenderWorld: world,
            Text: text,
            Character: me ?? "",
            Outgoing: outgoing);
        hub.Publish(item, result);
    }

    // Own lines carry no player payload; anyone else with the same name is from another world.
    private bool IsHomeWorld(PlayerPayload? player)
    {
        if (player == null)
            return true;
        var world = player.World.RowId != 0 ? player.World.RowId : playerState.CurrentWorld.RowId;
        return world == playerState.HomeWorld.RowId;
    }

    private static string? WorldName(RowRef<World>? world)
    {
        if (world is not { IsValid: true } row || row.RowId == 0)
            return null;
        var name = row.Value.Name.ExtractText();
        return name.Length > 0 ? name : null;
    }

    // Own lines may start with party slot or cross-world icons from the private use area.
    private static string StripGlyphs(string name) =>
        new string(name.Where(c => c < '\uE000' || c > '\uF8FF').ToArray()).Trim();
}
