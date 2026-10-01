using System.Collections.Generic;
using System.Numerics;
using ChatTerror.Plugin.Gui.Tabs;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ChatTerror.Plugin.Gui;

public sealed class ConfigWindow : Window
{
    private readonly IReadOnlyList<ITab> tabs;

    public ConfigWindow(IReadOnlyList<ITab> tabs)
        : base("ChatTerror##ConfigWindow")
    {
        this.tabs = tabs;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 360),
        };
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##tabs"))
            return;

        foreach (var tab in tabs)
        {
            if (!ImGui.BeginTabItem(tab.Title))
                continue;
            tab.Draw();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }
}
