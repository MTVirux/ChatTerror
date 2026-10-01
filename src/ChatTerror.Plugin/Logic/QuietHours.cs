using System;

namespace ChatTerror.Plugin.Logic;

public static class QuietHours
{
    public static bool IsQuiet(RelaySettings s, TimeOnly now)
    {
        if (!s.QuietHoursEnabled)
            return false;

        var minutes = now.Hour * 60 + now.Minute;
        var start = s.QuietStartMinutes;
        var end = s.QuietEndMinutes;

        if (start == end)
            return false;
        if (start < end)
            return minutes >= start && minutes < end;
        return minutes >= start || minutes < end;
    }
}
