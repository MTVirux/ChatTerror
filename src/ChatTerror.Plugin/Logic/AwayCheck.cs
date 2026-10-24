namespace ChatTerror.Plugin.Logic;

// A null idle time means the game's idle timer is unavailable (title screen, loading), which counts as away.
public static class AwayCheck
{
    public const int AlreadyAwaySeconds = 120;

    // The idle timer only moves once per frame, so it can trail the elapsed time a little.
    private const float FrameSlackMs = 500;

    public static int WaitSeconds(int setting, float? idleSeconds)
    {
        if (setting <= 0 || idleSeconds is not { } idle || idle >= AlreadyAwaySeconds)
            return 0;
        return setting;
    }

    public static bool StillAway(float? idleSeconds, long elapsedMs) =>
        idleSeconds is not { } idle || idle * 1000 + FrameSlackMs >= elapsedMs;
}
