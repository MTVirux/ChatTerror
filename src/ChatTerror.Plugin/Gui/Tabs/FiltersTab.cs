using System;
using ChatTerror.Plugin.Logic;
using ChatTerror.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class FiltersTab(Configuration config, Action changed) : ITab
{
    private readonly StringListEditor ignored = new("ignored", "First Last or First Last@World");
    private string? importResult;

    public string Title => "Filters";

    public void Draw()
    {
        var own = config.Settings.RelayOwnMessages;
        if (ImGui.Checkbox("Relay my own messages", ref own))
        {
            config.Settings.RelayOwnMessages = own;
            changed();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Ignored senders:");
        if (ImGui.Button("Import from in-game blacklist"))
            Import();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The blacklist has no worlds, so names match on any world.\nAdd @World to an entry to narrow it.");
        if (importResult != null)
            ImGui.TextDisabled(importResult);

        if (ignored.Draw(config.Settings.IgnoredSenders))
            changed();
    }

    private void Import()
    {
        var names = BlacklistReader.ReadNames();
        if (names.Count == 0)
        {
            importResult = "Blacklist empty or not loaded - open it in-game once and try again.";
            return;
        }

        var added = IgnoreList.Merge(config.Settings.IgnoredSenders, names);
        importResult = $"Imported {added} new name{(added == 1 ? "" : "s")}.";
        if (added > 0)
            changed();
    }
}
