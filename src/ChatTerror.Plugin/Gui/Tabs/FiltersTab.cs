using System;
using Dalamud.Bindings.ImGui;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class FiltersTab(Configuration config, Action changed) : ITab
{
    private readonly StringListEditor ignored = new("ignored", "First Last or First Last@World");

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
        if (ignored.Draw(config.Settings.IgnoredSenders))
            changed();
    }
}
