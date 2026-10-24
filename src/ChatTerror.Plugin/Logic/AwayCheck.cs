using System;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

// A null idle time means the game's idle timer is unavailable (title screen, loading), which counts as away.
public static class AwayCheck
{
    public const int AlreadyAwaySeconds = 120;

    // The Duty Finder accept window is about 45 s (Limits.DutyPopTtlSeconds), so a duty pop can't wait out a longer setting.
    public const int MaxDutyPopWaitSeconds = 30;

    private const int MinDutyPopTtlSeconds = 5;

    // The idle timer only moves once per frame, so it can trail the elapsed time a little.
    private const float FrameSlackMs = 500;

    // The larger timer wins, so one that stopped counting can't hold a notification back.
    public static float? IdleSeconds(bool characterLoaded, float afkTimer, float inputTimer) =>
        characterLoaded ? Math.Max(afkTimer, inputTimer) : null;

    public static int WaitSeconds(int setting, float? idleSeconds)
    {
        if (setting <= 0 || idleSeconds is not { } idle || idle >= AlreadyAwaySeconds)
            return 0;
        return setting;
    }

    public static int AlertWaitSetting(AlertKind kind, int setting) =>
        kind == AlertKind.DutyPop ? Math.Min(setting, MaxDutyPopWaitSeconds) : setting;

    // A duty pop's TTL counts from the pop, so the push expires with the accept window.
    public static int AlertTtlSeconds(AlertKind kind, long waitedMs) =>
        kind == AlertKind.DutyPop
            ? Math.Max(MinDutyPopTtlSeconds, Limits.DutyPopTtlSeconds - (int)Math.Ceiling(waitedMs / 1000.0))
            : Limits.PartyFullTtlSeconds;

    public static bool StillAway(float? idleSeconds, long elapsedMs) =>
        idleSeconds is not { } idle || idle * 1000 + FrameSlackMs >= elapsedMs;
}
