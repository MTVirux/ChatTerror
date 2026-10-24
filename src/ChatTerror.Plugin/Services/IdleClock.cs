using FFXIVClientStructs.FFXIV.Client.UI;

namespace ChatTerror.Plugin.Services;

// The game's own time since its last keyboard, mouse or controller input. Framework thread only.
public sealed class IdleClock
{
    public unsafe float? IdleSeconds()
    {
        var ui = UIModule.Instance();
        if (ui == null)
            return null;

        var timers = ui->GetInputTimerModule();
        return timers == null ? null : timers->AfkTimer;
    }
}
