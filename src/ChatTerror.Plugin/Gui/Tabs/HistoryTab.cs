using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ChatTerror.Plugin.Core;
using ChatTerror.Plugin.Services;
using ChatTerror.Protocol;
using Dalamud.Bindings.ImGui;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class HistoryTab(DeviceHub hub, Func<string?> currentCharacter) : ITab
{
    private static readonly Vector4 OutgoingColor = new(0.6f, 0.8f, 1f, 1f);

    private string? character;
    private ChatChannel? channel;
    private string search = "";
    private bool confirmClear;
    private bool scrollToBottom = true;

    // Rebuilt only when the history or a filter changes, not every frame.
    private long shownVersion = -1;
    private string? shownCharacter;
    private ChatChannel? shownChannel;
    private string shownSearch = "";
    private IReadOnlyList<string> characters = [];
    private IReadOnlyList<ChatItem> rows = [];

    public string Title => "History";

    public void Draw()
    {
        Refresh();
        DrawFilters();
        DrawClear();
        ImGui.Separator();
        DrawRows();
    }

    private void Refresh()
    {
        var version = hub.History.Version;
        if (version != shownVersion)
        {
            characters = hub.History.Characters;
            if (character == null || !characters.Contains(character))
                character = currentCharacter() is { } current && characters.Contains(current) ? current : characters.FirstOrDefault();
        }

        if (version == shownVersion && character == shownCharacter && channel == shownChannel && search == shownSearch)
            return;

        if (character != shownCharacter || channel != shownChannel || search != shownSearch)
            scrollToBottom = true;

        shownVersion = version;
        shownCharacter = character;
        shownChannel = channel;
        shownSearch = search;
        rows = character == null
            ? []
            : hub.History.For(character).Where(Matches).ToList();
    }

    private bool Matches(ChatItem item) =>
        (channel == null || item.Channel == channel)
        && (search.Length == 0
            || item.Text.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Sender.Contains(search, StringComparison.OrdinalIgnoreCase));

    private void DrawFilters()
    {
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("##character", character == null ? "No history" : CharacterLabel(character)))
        {
            foreach (var name in characters)
            {
                if (ImGui.Selectable(CharacterLabel(name), name == character))
                    character = name;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(170);
        if (ImGui.BeginCombo("##channel", channel is { } selected ? ChannelMap.DisplayName(selected) : "All channels"))
        {
            if (ImGui.Selectable("All channels", channel == null))
                channel = null;
            foreach (var value in Enum.GetValues<ChatChannel>())
            {
                if (ImGui.Selectable(ChannelMap.DisplayName(value), channel == value))
                    channel = value;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search messages or senders", ref search, 100);
    }

    private static string CharacterLabel(string name) => name.Length == 0 ? "(not logged in)" : name;

    private void DrawClear()
    {
        if (confirmClear)
        {
            ImGui.TextUnformatted("Delete the saved history for every character?");
            ImGui.SameLine();
            if (ImGui.SmallButton("Confirm"))
            {
                confirmClear = false;
                hub.ClearHistory();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Keep"))
                confirmClear = false;
            return;
        }

        ImGui.TextDisabled($"{rows.Count} messages");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear history"))
            confirmClear = true;
    }

    private void DrawRows()
    {
        if (rows.Count == 0)
        {
            ImGui.TextDisabled("Nothing to show.");
            return;
        }

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable("##history", 4, flags))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Time");
        ImGui.TableSetupColumn("Channel");
        ImGui.TableSetupColumn("Sender");
        ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(rows.Count);
        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                DrawRow(rows[i]);
        }

        clipper.End();
        clipper.Destroy();

        if (scrollToBottom || ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            ImGui.SetScrollHereY(1f);
        scrollToBottom = false;

        ImGui.EndTable();
    }

    private static void DrawRow(ChatItem item)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(DateTimeOffset.FromUnixTimeMilliseconds(item.Ts).ToLocalTime().ToString("g"));

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(ChannelMap.DisplayName(item.Channel));

        ImGui.TableNextColumn();
        var sender = item.SenderWorld is { } world ? $"{item.Sender}@{world}" : item.Sender;
        ImGui.TextUnformatted(sender);

        ImGui.TableNextColumn();
        if (item.Outgoing)
            ImGui.TextColored(OutgoingColor, item.Text);
        else
            ImGui.TextUnformatted(item.Text);
        if (ImGui.IsItemHovered() && ImGui.CalcTextSize(item.Text).X > ImGui.GetColumnWidth())
            ImGui.SetTooltip(item.Text);
    }
}
