using System;
using System.Collections.Generic;
using ChatTerror.Plugin.Core;
using ChatTerror.Plugin.Logic;
using ChatTerror.Plugin.Services;
using ChatTerror.Protocol;
using Dalamud.Bindings.ImGui;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class ChannelsTab(Configuration config, TellDirectory tells, Action changed) : ITab
{
    private const string DragType = "CHANNEL_ROW";

    private ChatChannel? dragged;

    public string Title => "Channels";

    public void Draw()
    {
        var relayed = config.TellsEnabled;
        if (ImGui.Checkbox("Relay tells to ChatTerror friends", ref relayed))
        {
            config.TellsEnabled = relayed;
            changed();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Tells to friends who also use ChatTerror are delivered through the relay when the game can't reach them.");
        ImGui.TextDisabled("Uploads hashed IDs of your characters and friend lists to the relay. ChatTerror friends can see you use it.");
        if (config.TellsEnabled && tells.Status is { } status)
            ImGui.TextDisabled(status);
        ImGui.Spacing();

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
        (ChatChannel From, ChatChannel To)? drop = null;
        foreach (var channel in order)
        {
            if (!config.Settings.Channels.TryGetValue(channel, out var setting))
            {
                setting = new ChannelSetting();
                config.Settings.Channels[channel] = setting;
            }

            ImGui.PushID((int)channel);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Selectable(ChannelMap.DisplayName(channel), dragged == channel);
            if (ImGui.BeginDragDropSource())
            {
                dragged = channel;
                ImGui.SetDragDropPayload(DragType, ReadOnlySpan<byte>.Empty, ImGuiCond.None);
                ImGui.TextUnformatted(ChannelMap.DisplayName(channel));
                ImGui.EndDragDropSource();
            }
            if (ImGui.BeginDragDropTarget())
            {
                if (!ImGui.AcceptDragDropPayload(DragType).IsNull && dragged is { } from)
                    drop = (from, channel);
                ImGui.EndDragDropTarget();
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

        if (drop is { } d && d.From != d.To)
            Move(order, d.From, d.To);
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            dragged = null;
    }

    private void Move(List<ChatChannel> order, ChatChannel from, ChatChannel to)
    {
        // The dragged channel takes the target's slot, shifting the target toward where it came from.
        var target = order.IndexOf(to);
        order.Remove(from);
        order.Insert(target, from);
        config.Settings.ChannelOrder = order;
        changed();
    }
}
