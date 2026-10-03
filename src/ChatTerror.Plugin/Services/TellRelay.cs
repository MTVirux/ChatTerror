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
    // Only marks relayed tells in the log, ChatCapture.Printed is what recognizes them.
    public const string Prefix = "\uE0BB ";
}

// Sends tells the game dropped through the relay and shows relayed tells in game. Framework thread only.
public sealed class TellRelay : IDisposable
{
    private const int SeenCapacity = 500;
    private const int TellsPerFrame = 5;

    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly KeyStore keys;
    private readonly RelayApi api;
    private readonly RelayClient relay;
    private readonly DeviceHub hub;
    private readonly TellDirectory directory;
    private readonly ChatCapture capture;
    private readonly IChatGui chatGui;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly IPluginLog log;
    private readonly TellFallback fallback = new();
    private readonly ReplyTracker reply = new();
    private readonly SeenIds seen;
    private readonly InFlightTells<SentTell> inFlight = new();
    private readonly TellInbox inbox = new(Limits.MaxQueuedTells);

    private sealed record SentTell(TellBody Body, TellFriend To);

    public TellRelay(Configuration config, Action saveConfig, KeyStore keys, RelayApi api, RelayClient relay, DeviceHub hub,
        TellDirectory directory, ChatCapture capture, IChatGui chatGui, IFramework framework, IPlayerState playerState, IPluginLog log)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.keys = keys;
        this.api = api;
        this.relay = relay;
        this.hub = hub;
        this.directory = directory;
        this.capture = capture;
        this.chatGui = chatGui;
        this.framework = framework;
        this.playerState = playerState;
        this.log = log;
        seen = new SeenIds(SeenCapacity, config.SeenTellIds);
        hub.TellFrameReceived += OnFrame;
        relay.StateChanged += OnStateChanged;
        capture.GameTell += OnGameTell;
        framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        capture.GameTell -= OnGameTell;
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
            && Route(tell.Target).Error != RouteError.NotPaired)
            fallback.Sent(tell.Target, tell.Text, Environment.TickCount64);
        return rewritten;
    }

    private (FriendRoute? Route, RouteError Error) Route(TellTarget target) =>
        FriendTrust.Route(config.TellCharacters, config.PairedFriends, directory.CurrentHash, target);

    private string? CurrentWorld() =>
        playerState.IsLoaded && playerState.CurrentWorld.IsValid ? playerState.CurrentWorld.Value.Name.ExtractText() : null;

    private void OnGameTell(IHandleableChatMessage message)
    {
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
        var tells = inbox.Drain(playerState.IsLoaded, TellsPerFrame);
        foreach (var tell in tells)
        {
            Receive(tell);
            relay.Send(new TellAckFrame([tell.Id]));
        }
        if (tells.Count > 0)
        {
            config.SeenTellIds = seen.Ids;
            saveConfig();
        }

        foreach (var pending in fallback.Expired(Environment.TickCount64))
        {
            var (route, error) = Route(pending.Target);
            if (route != null)
                _ = SendAsync(route, pending.Text);
            else if (error == RouteError.Conflict)
                PrintError(pending.Target.ToString(), "claimed by more than one paired friend");
        }
    }

    private sealed class TellFailedException(string reason) : Exception(reason);

    private async Task SendAsync(FriendRoute route, string text)
    {
        try
        {
            if (config.InstallToken is not { } token)
                throw new InvalidOperationException("Not registered with the relay.");
            var recipient = await FetchBundle(token, route.Friend);
            var own = await api.GetTellBundle(token, "self") is { } signed ? TellBundles.Verify(signed, keys.PublicKey) : null;
            var body = new TellBody(Guid.NewGuid().ToString("N"), route.From.Hash, route.From.Name, route.From.World,
                route.To.Hash, route.To.Name, route.To.World, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var copies = TellCopies.Build(body, recipient, own, TellTargets.Plugin);
            await framework.RunOnFrameworkThread(() =>
            {
                inFlight.Add(body.Id, new SentTell(body, route.To));
                if (!relay.Send(new TellSendFrame(body.Id, route.Friend.InstallId, copies)))
                {
                    inFlight.Complete(body.Id);
                    PrintError(route.To, "not connected to the relay");
                }
            });
        }
        catch (TellFailedException ex)
        {
            await framework.RunOnFrameworkThread(() => PrintError(route.To, ex.Message));
        }
        catch (Exception ex)
        {
            log.Warning($"Relayed tell failed: {ex.Message}");
            await framework.RunOnFrameworkThread(() => PrintError(route.To, "the relay could not be reached"));
        }
    }

    // The bundle must be signed with the key we paired with and not older than the newest one seen.
    private async Task<TellBundle> FetchBundle(string token, PairedFriend friend)
    {
        var signed = await api.GetTellBundle(token, friend.InstallId) ?? throw new TellFailedException(ErrorText(TellErrors.NotChatTerror));
        var bundle = TellBundles.Verify(signed, friend.PublicKey)
            ?? throw new TellFailedException("their ChatTerror key does not match the one you paired with");
        var current = await framework.RunOnFrameworkThread(() =>
        {
            if (!FriendTrust.AcceptBundle(friend, bundle.IssuedAt))
                return false;
            saveConfig();
            return true;
        });
        return current ? bundle : throw new TellFailedException("the relay sent an outdated bundle");
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
        if (!config.TellsEnabled)
            return;
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
        if (body.Id != tell.Id || TellItems.IsExpired(body, Now()) || !seen.Add(body.Id))
            return;

        // A copy of a tell one of our phones sent, which only this install can have sent.
        if (tell.FromKey == keys.PublicKey)
        {
            if (config.TellCharacters.Any(c => c.Hash == body.FromHash))
                ShowOutgoing(body);
            else
                log.Warning("Dropped a relayed tell sent with our key from a character that is not ours.");
            return;
        }

        if (FriendTrust.Incoming(config.TellCharacters, config.PairedFriends, tell.From, tell.FromKey, body) is not { } incoming
            || ChatFilter.IsIgnored(incoming.Sender.Name, incoming.Sender.World, config.Settings))
            return;
        var item = TellItems.Incoming(body, incoming.Character, incoming.Sender, Now());
        if (TellText.Clean(body.Text, Limits.MaxTextBytes) is not { Length: > 0 } text)
            return;
        Print(XivChatType.TellIncoming, $"{item.Sender}@{item.SenderWorld}", text);
        reply.Incoming(new TellTarget(item.Sender, item.SenderWorld!), relayed: true);
        hub.PublishRelayed(item with { Text = text });
    }

    private void ShowOutgoing(TellBody body)
    {
        seen.Add(body.Id);
        var name = TellText.Clean(body.ToName, TellText.MaxNameBytes);
        var world = TellText.Clean(body.ToWorld, TellText.MaxNameBytes);
        var text = TellText.Clean(body.Text, Limits.MaxTextBytes);
        Print(XivChatType.TellOutgoing, $"{name}@{world}", text);
        hub.PublishRelayed(TellItems.Outgoing(body, Now()) with { Text = text });
    }

    private void Print(XivChatType type, string name, string text)
    {
        var message = new SeStringBuilder().AddText(TellMarker.Prefix + text).Build();
        capture.Printed.Add(PrintedTells.Key(type == XivChatType.TellOutgoing, message.TextValue));
        chatGui.Print(new XivChatEntry
        {
            Type = type,
            Name = new SeStringBuilder().AddText(name).Build(),
            Message = message,
        });
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void PrintError(TellFriend to, string reason) => PrintError($"{to.Name}@{to.World}", reason);

    private void PrintError(string to, string reason) =>
        chatGui.PrintError($"[ChatTerror] Tell to {to} could not be relayed: {reason}.");

    private static string ErrorText(string? code) => code switch
    {
        TellErrors.NotPaired => "you are not paired with them",
        TellErrors.NotChatTerror => "they don't use ChatTerror or turned relayed tells off",
        TellErrors.RateLimited => "too many messages, try again in a moment",
        _ => "the relay refused it",
    };
}
