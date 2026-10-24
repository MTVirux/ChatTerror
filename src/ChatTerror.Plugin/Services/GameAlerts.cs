using System;
using System.Collections.Generic;
using ChatTerror.Protocol;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace ChatTerror.Plugin.Services;

public sealed class GameAlerts : IDisposable
{
    // "Party recruitment ended. All places have been filled.", for the leader and members, same-world and cross-world.
    private static readonly HashSet<uint> PartyFullMessageIds = [983, 984, 7451, 7452];

    private readonly IClientState clientState;
    private readonly IChatGui chatGui;
    private readonly IFramework framework;
    private readonly DeviceHub hub;
    private bool disposed;

    public GameAlerts(IClientState clientState, IChatGui chatGui, IFramework framework, DeviceHub hub)
    {
        this.clientState = clientState;
        this.chatGui = chatGui;
        this.framework = framework;
        this.hub = hub;
        clientState.CfPop += OnCfPop;
        chatGui.LogMessage += OnLogMessage;
    }

    public void Dispose()
    {
        disposed = true;
        clientState.CfPop -= OnCfPop;
        chatGui.LogMessage -= OnLogMessage;
    }

    // Dalamud raises CfPop from a thread pool task.
    private void OnCfPop(ContentFinderCondition condition)
    {
        var duty = DutyName(condition);
        framework.RunOnFrameworkThread(() =>
        {
            if (!disposed)
                hub.Alert(AlertKind.DutyPop, duty);
        });
    }

    private void OnLogMessage(ILogMessage message)
    {
        if (PartyFullMessageIds.Contains(message.LogMessageId))
            hub.Alert(AlertKind.PartyFull, null);
    }

    // Roulettes don't name the duty.
    private static string? DutyName(ContentFinderCondition condition) =>
        condition.Name.ExtractText() is { Length: > 0 } name ? name : null;
}
