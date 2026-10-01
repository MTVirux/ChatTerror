using System;
using System.Collections.Generic;
using ChatTerror.Plugin.Services;
using ChatTerror.Protocol;
using Dalamud.Bindings.ImGui;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class AdvancedTab(Configuration config, DeviceHub hub, Action changed) : ITab
{
    private readonly Dictionary<string, int> pending = new();

    public string Title => "Advanced";

    public void Draw()
    {
        var settings = config.Settings;

        var history = settings.HistorySize;
        if (IntInput("History size (messages)", ref history, 100, ConfigStatic.MinHistorySize, ConfigStatic.MaxHistorySize))
        {
            settings.HistorySize = history;
            hub.History.Capacity = history;
            changed();
        }

        var delay = settings.SendDelayMs;
        if (IntInput("Delay between sent lines (ms)", ref delay, 250, ConfigStatic.MinSendDelayMs, ConfigStatic.MaxSendDelayMs))
        {
            settings.SendDelayMs = delay;
            changed();
        }

        var maxLength = settings.MaxLengthBytes;
        if (IntInput("Max message length (bytes)", ref maxLength, 10, 1, Limits.MaxTextBytes))
        {
            settings.MaxLengthBytes = maxLength;
            changed();
        }

        var loggedIn = settings.RequireLoggedIn;
        if (ImGui.Checkbox("Only send while logged in", ref loggedIn))
        {
            settings.RequireLoggedIn = loggedIn;
            changed();
        }
    }

    // Typed values are kept here while the field is active and applied once it is released.
    private bool IntInput(string label, ref int value, int step, int min, int max)
    {
        if (!pending.TryGetValue(label, out var edited))
            edited = value;

        ImGui.SetNextItemWidth(150);
        ImGui.InputInt(label, ref edited, step, step * 10);
        if (ImGui.IsItemActive())
        {
            pending[label] = edited;
            return false;
        }

        pending.Remove(label);
        edited = Math.Clamp(edited, min, max);
        if (edited == value)
            return false;
        value = edited;
        return true;
    }
}
