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
        ImGui.Spacing();

        if (!ImGui.BeginTable("##channels", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Channel");
        ImGui.TableSetupColumn("Relay");
        ImGui.TableSetupColumn("Push");
        ImGui.TableSetupColumn("Send");
        ImGui.TableHeadersRow();

        foreach (var channel in Enum.GetValues<ChatChannel>())
        {
            if (!config.Settings.Channels.TryGetValue(channel, out var setting))
            {
                setting = new ChannelSetting();
                config.Settings.Channels[channel] = setting;
            }

            ImGui.PushID((int)channel);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ChannelMap.DisplayName(channel));

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
