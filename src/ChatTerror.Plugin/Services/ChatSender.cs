using System;
using System.Linq;
using System.Text.RegularExpressions;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ChatTerror.Plugin.Services;

public sealed unsafe class ChatSender : IGameGate, IDisposable
{
    // Same set the chat box allows when typing.
    private const AllowedEntities ChatBoxEntities =
        AllowedEntities.UppercaseLetters | AllowedEntities.LowercaseLetters | AllowedEntities.Numbers |
        AllowedEntities.SpecialCharacters | AllowedEntities.CharacterList | AllowedEntities.OtherCharacters |
        AllowedEntities.Payloads | AllowedEntities.Unknown9 | AllowedEntities.CJK;

    // Last line of defence: only channel-prefixed single-line chat ever reaches the game.
    private static readonly Regex ChatLine = new(
        @"^/(tell [A-Za-z'\-]{1,15} [A-Za-z'\-]{1,15}@[A-Za-z]{3,16}|p|a|fc|l[1-8]|cwl[1-8]|n|s|sh|y) [^\r\n/][^\r\n]*\z",
        RegexOptions.CultureInvariant);

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly DeviceHub hub;
    private readonly IPluginLog log;
    private readonly SendQueue queue;

    public ChatSender(IFramework framework, IClientState clientState, ICondition condition, DeviceHub hub, Func<RelaySettings> settings, IPluginLog log)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.condition = condition;
        this.hub = hub;
        this.log = log;
        queue = new SendQueue(settings, this, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        hub.SendRequested += queue.Enqueue;
        framework.Update += OnUpdate;
    }

    public bool IsLoggedIn => clientState.IsLoggedIn;

    public bool IsBusy =>
        condition[ConditionFlag.BetweenAreas] ||
        condition[ConditionFlag.BetweenAreas51] ||
        condition[ConditionFlag.WatchingCutscene] ||
        condition[ConditionFlag.OccupiedInCutSceneEvent];

    // Control characters are refused outright since 0x02 would start a raw SeString payload.
    public string? Sanitize(string line)
    {
        if (line.Any(char.IsControl))
            return null;

        var text = Utf8String.FromString(line);
        try
        {
            text->SanitizeString(ChatBoxEntities);
            return text->ToString() == line ? line : null;
        }
        finally
        {
            text->Dtor(true);
        }
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        hub.SendRequested -= queue.Enqueue;
    }

    private void OnUpdate(IFramework _)
    {
        var (line, results) = queue.Tick();
        var error = line == null ? null : SendLine(line);

        foreach (var (request, result) in results)
        {
            var final = result.Ok && error != null ? result with { Ok = false, Error = error } : result;
            hub.SendResult(request.DeviceId, final);
        }
    }

    // Returns an error code, or null when the line was handed to the game.
    private string? SendLine(string line)
    {
        if (!ChatLine.IsMatch(line) || line.Any(char.IsControl))
        {
            log.Error("Refusing to send a line that is not a plain chat message.");
            return SendErrors.InvalidText;
        }

        if (!clientState.IsLoggedIn)
            return SendErrors.NotLoggedIn;

        var module = UIModule.Instance();
        if (module == null)
            return SendErrors.Busy;

        var text = Utf8String.FromString(line);
        try
        {
            module->ProcessChatBoxEntry(text);
            return null;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to send chat line.");
            return SendErrors.Busy;
        }
        finally
        {
            text->Dtor(true);
        }
    }
}
