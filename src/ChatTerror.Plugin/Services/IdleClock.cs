using ChatTerror.Plugin.Logic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace ChatTerror.Plugin.Services;

// The game's own time since its last keyboard, mouse or controller input. Framework thread only.
public sealed class IdleClock(IPlayerState playerState)
{
    public unsafe float? IdleSeconds()
    {
        var timers = InputTimers();
        return timers == null ? null : AwayCheck.IdleSeconds(playerState.IsLoaded, timers->AfkTimer, timers->InputTimer);
    }

    // For the Verbose log lines, so the timers can be compared in game.
    public unsafe string TimersText()
    {
        var timers = InputTimers();
        return timers == null
            ? "no input timers"
            : $"AfkTimer {timers->AfkTimer:0.0} s, ContentInputTimer {timers->ContentInputTimer:0.0} s, InputTimer {timers->InputTimer:0.0} s";
    }

    private static unsafe InputTimerModule* InputTimers()
    {
        var ui = UIModule.Instance();
        return ui == null ? null : ui->GetInputTimerModule();
    }
}
