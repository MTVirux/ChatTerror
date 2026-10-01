using System;
using System.Globalization;
using Dalamud.Bindings.ImGui;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class NotificationsTab(Configuration config, Action changed) : ITab
{
    private readonly StringListEditor keywords = new("keywords", "Keyword");
    private string? quietStart;
    private string? quietEnd;

    public string Title => "Notifications";

    public void Draw()
    {
        var settings = config.Settings;

        var tell = settings.PushOnTell;
        if (ImGui.Checkbox("Notify on tells", ref tell))
        {
            settings.PushOnTell = tell;
            changed();
        }

        var mention = settings.PushOnMention;
        if (ImGui.Checkbox("Notify when my name is mentioned", ref mention))
        {
            settings.PushOnMention = mention;
            changed();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Notify when a message contains:");
        if (keywords.Draw(settings.PushKeywords))
            changed();

        ImGui.Spacing();
        ImGui.Separator();
        var quiet = settings.QuietHoursEnabled;
        if (ImGui.Checkbox("Quiet hours (still relayed, no notifications)", ref quiet))
        {
            settings.QuietHoursEnabled = quiet;
            changed();
        }

        quietStart ??= Format(settings.QuietStartMinutes);
        quietEnd ??= Format(settings.QuietEndMinutes);

        if (TimeInput("Start", ref quietStart, settings.QuietStartMinutes, out var start))
        {
            settings.QuietStartMinutes = start;
            changed();
        }

        ImGui.SameLine();
        if (TimeInput("End", ref quietEnd, settings.QuietEndMinutes, out var end))
        {
            settings.QuietEndMinutes = end;
            changed();
        }
    }

    private static string Format(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";

    // Applies on focus loss so half-typed values are not saved; invalid input reverts.
    private static bool TimeInput(string label, ref string value, int current, out int minutes)
    {
        minutes = current;
        ImGui.SetNextItemWidth(70);
        ImGui.InputTextWithHint(label, "HH:MM", ref value, 5);
        if (!ImGui.IsItemDeactivatedAfterEdit())
            return false;

        if (!TimeOnly.TryParseExact(value, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            value = Format(current);
            return false;
        }

        minutes = time.Hour * 60 + time.Minute;
        value = Format(minutes);
        return minutes != current;
    }
}
