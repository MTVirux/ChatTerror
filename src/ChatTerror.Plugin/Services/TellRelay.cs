using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Services;

public static class TellMarker
{
    public const string Prefix = " ";
}

// Sends tells the game dropped through the relay and shows relayed tells in game. Framework thread only.
public sealed class TellRelay : IDisposable
{
    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly KeyStore keys;
    private readonly RelayApi api;
    private readonly RelayClient relay;
    private readonly DeviceHub hub;
    private readonly TellDirectory directory;
    private readonly IChatGui chatGui;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly IPluginLog log;
    private readonly TellFallback fallback = new();
    private readonly ReplyTracker reply = new();
    private readonly SeenIds seen = new(1000);
    private readonly InFlightTells<SentTell> inFlight = new();
    private readonly TellInbox inbox = new();

    private sealed record SentTell(TellBody Body, TellFriend To);

    public TellRelay(Configuration config, Action saveConfig, KeyStore keys, RelayApi api, RelayClient relay, DeviceHub hub,
        TellDirectory directory, IChatGui chatGui, IFramework framework, IPlayerState playerState, IPluginLog log)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.keys = keys;
        this.api = api;
        this.relay = relay;
        this.hub = hub;
        this.directory = directory;
        this.chatGui = chatGui;
        this.framework = framework;
        this.playerState = playerState;
        this.log = log;
        hub.TellFrameReceived += OnFrame;
        relay.StateChanged += OnStateChanged;
        chatGui.ChatMessage += OnChatMessage;
        framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        chatGui.ChatMessage -= OnChatMessage;
        relay.StateChanged -= OnStateChanged;
        hub.TellFrameReceived -= OnFrame;
    }

    // Called by the hook for every chat command. Returns a replacement line or null.
    public string? OnCommand(string line)
    {
        if (!config.TellsEnabled)
            return null;
        var rewritten = reply.Rewrite(line);
        if (TellCommand.ParseTell(rewritten ?? line, CurrentWorld() ?? "") is { } tell
            && TellContacts.Route(config.TellCharacters, directory.CurrentHash, tell.Target) != null)
            fallback.Sent(tell.Target, tell.Text, Environment.TickCount64);
        return rewritten;
    }

    private string? CurrentWorld() =>
        playerState.IsLoaded && playerState.CurrentWorld.IsValid ? playerState.CurrentWorld.Value.Name.ExtractText() : null;

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (message.LogKind is not (XivChatType.TellIncoming or XivChatType.TellOutgoing)
            || message.Message.TextValue.StartsWith(TellMarker.Prefix, StringComparison.Ordinal))
            return;

        var player = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        var name = player?.PlayerName ?? message.Sender.TextValue;
        var world = player != null && player.World.IsValid && player.World.RowId != 0 ? player.World.Value.Name.ExtractText() : null;
        if (message.LogKind == XivChatType.TellOutgoing)
            fallback.Echoed(name, world);
        else if ((world ?? CurrentWorld()) is { } from)
            reply.Incoming(new TellTarget(name, from), relayed: false);
    }

    // Results of tells still in flight never arrive once the connection drops.
    private void OnStateChanged(RelayState state)
    {
        if (state == RelayState.Connected)
            return;
        framework.RunOnFrameworkThread(() =>
        {
            foreach (var sent in inFlight.DropAll())
                PrintError(sent.To, "the connection to the relay was lost");
        });
    }

    private void OnUpdate(IFramework unused)
    {
        // Unacked tells are delivered again on reconnect, so holding them until login loses nothing.
        foreach (var tell in inbox.Drain(playerState.IsLoaded))
        {
            Receive(tell);
            relay.Send(new TellAckFrame([tell.Id]));
        }

        foreach (var pending in fallback.Expired(Environment.TickCount64))
        {
            if (TellContacts.Route(config.TellCharacters, directory.CurrentHash, pending.Target) is { } route)
                _ = SendAsync(route, pending.Text);
        }
    }

    private sealed class KeyChangedException : Exception;

    private async Task SendAsync(TellRoute route, string text)
    {
        try
        {
            if (config.InstallToken is not { } token)
                throw new InvalidOperationException("Not registered with the relay.");
            var recipient = await FetchBundle(token, route.To.Hash);
            var own = await api.GetTellBundle(token, "self") is { } signed ? TellBundles.Verify(signed, keys.PublicKey) : null;
            var body = new TellBody(Guid.NewGuid().ToString("N"), route.From.Hash, route.From.Name, route.From.World,
                route.To.Hash, route.To.Name, route.To.World, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var copies = TellCopies.Build(body, recipient, own, TellTargets.Plugin);
            await framework.RunOnFrameworkThread(() =>
            {
                inFlight.Add(body.Id, new SentTell(body, route.To));
                if (!relay.Send(new TellSendFrame(body.Id, body.FromHash, body.ToHash, copies)))
                {
                    inFlight.Complete(body.Id);
                    PrintError(route.To, "not connected to the relay");
                }
            });
        }
        catch (KeyChangedException)
        {
            await framework.RunOnFrameworkThread(() =>
                PrintError(route.To, "their ChatTerror key changed, forget it in the Advanced tab to trust the new one"));
        }
        catch (Exception ex)
        {
            log.Warning($"Relayed tell failed: {ex.Message}");
            await framework.RunOnFrameworkThread(() => PrintError(route.To, "the relay could not be reached"));
        }
    }

    // Pins the install key the first time, then refuses bundles signed by another key.
    private async Task<TellBundle> FetchBundle(string token, string hash)
    {
        var signed = await api.GetTellBundle(token, hash) ?? throw new InvalidOperationException("They don't use ChatTerror.");
        var pin = await framework.RunOnFrameworkThread(() => config.TellPins.GetValueOrDefault(hash));
        var bundle = TellBundles.Verify(signed, pin);
        if (bundle == null)
            throw pin != null && TellBundles.Verify(signed, null) != null ? new KeyChangedException() : new InvalidOperationException("Invalid bundle.");
        if (pin == null)
        {
            await framework.RunOnFrameworkThread(() =>
            {
                config.TellPins[hash] = bundle.InstallPublicKey;
                saveConfig();
            });
        }
        return bundle;
    }

    private void OnFrame(RelayFrame frame)
    {
        switch (frame)
        {
            case TellResultFrame result when inFlight.Complete(result.Id) is { } sent:
                if (result.Ok)
                    ShowOutgoing(sent.Body);
                else
                    PrintError(sent.To, ErrorText(result.Error));
                break;
            case TellFrame tell:
                inbox.Add(tell);
                break;
        }
    }

    private void Receive(TellFrame tell)
    {
        TellBody body;
        try
        {
            body = SealedTell.OpenBody(keys.Key, tell.Envelope);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            log.Warning("Dropped an undecryptable relayed tell.");
            return;
        }
        if (body.Id != tell.Id || body.FromHash != tell.From || !seen.Add(body.Id))
            return;

        // A copy of a tell one of our phones sent.
        if (config.TellCharacters.Any(c => c.Hash == tell.From))
        {
            ShowOutgoing(body);
            return;
        }

        if (!config.TellsEnabled || TellItems.Incoming(body, tell.From, config.TellCharacters, Now()) is not { } item)
            return;
        chatGui.Print(new XivChatEntry
        {
            Type = XivChatType.TellIncoming,
            Name = new SeStringBuilder().AddText($"{item.Sender}@{item.SenderWorld}").Build(),
            Message = new SeStringBuilder().AddText(TellMarker.Prefix + body.Text).Build(),
        });
        reply.Incoming(new TellTarget(item.Sender, item.SenderWorld!), relayed: true);
        hub.PublishRelayed(item);
    }

    private void ShowOutgoing(TellBody body)
    {
        seen.Add(body.Id);
        chatGui.Print(new XivChatEntry
        {
            Type = XivChatType.TellOutgoing,
            Name = new SeStringBuilder().AddText($"{body.ToName}@{body.ToWorld}").Build(),
            Message = new SeStringBuilder().AddText(TellMarker.Prefix + body.Text).Build(),
        });
        hub.PublishRelayed(TellItems.Outgoing(body, Now()));
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void PrintError(TellFriend to, string reason) =>
        chatGui.PrintError($"[ChatTerror] Tell to {to.Name}@{to.World} could not be relayed: {reason}.");

    private static string ErrorText(string? code) => code switch
    {
        TellErrors.NotFriend => "you are not on their friend list",
        TellErrors.NotChatTerror => "they don't use ChatTerror",
        TellErrors.NotOwner => "this character is registered to another ChatTerror install",
        TellErrors.RateLimited => "too many messages, try again in a moment",
        _ => "the relay refused it",
    };
}
