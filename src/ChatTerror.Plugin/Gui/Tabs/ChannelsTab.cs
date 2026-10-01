using System;
using ChatTerror.Plugin.Core;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Bindings.ImGui;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class ChannelsTab(Configuration config, Action changed) : ITab
{
    public string Title => "Channels";

    public void Draw()
    {
        ImGui.TextWrapped("Relay sends the channel to your phone, Push notifies for every message, Send lets the phone write to it.");
        ImGui.TextWrapped("Drag a channel name to reorder it, the app lists channels in this order.");
        if (ImGui.Button("Reset order"))
        {
            config.Settings.ChannelOrder = new();
            changed();
        }
        ImGui.Spacing();

        if (!ImGui.BeginTable("##channels", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Channel");
        ImGui.TableSetupColumn("Relay");
        ImGui.TableSetupColumn("Push");
        ImGui.TableSetupColumn("Send");
        ImGui.TableHeadersRow();

        var order = config.Settings.OrderedChannels();
        for (var i = 0; i < order.Count; i++)
        {
            var channel = order[i];
            if (!config.Settings.Channels.TryGetValue(channel, out var setting))
            {
                setting = new ChannelSetting();
                config.Settings.Channels[channel] = setting;
            }

            ImGui.PushID((int)channel);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Selectable(ChannelMap.DisplayName(channel));
            if (ImGui.IsItemActive() && !ImGui.IsItemHovered())
            {
                var next = i + (ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y < 0 ? -1 : 1);
                if (next >= 0 && next < order.Count)
                {
                    (order[i], order[next]) = (order[next], order[i]);
                    config.Settings.ChannelOrder = order;
                    ImGui.ResetMouseDragDelta();
                    changed();
                }
            }

            ImGui.TableNextColumn();
            var relay = setting.Relay;
            if (ImGui.Checkbox("##relay", ref relay))
            {
                setting.Relay = relay;
                changed();
            }

            ImGui.TableNextColumn();
            var push = setting.Push;
            if (ImGui.Checkbox("##push", ref push))
            {
                setting.Push = push;
                changed();
            }

            ImGui.TableNextColumn();
            var send = setting.Send;
            if (ImGui.Checkbox("##send", ref send))
            {
                setting.Send = send;
                changed();
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }
}
