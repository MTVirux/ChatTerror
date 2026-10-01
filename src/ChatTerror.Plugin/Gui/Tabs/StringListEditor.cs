using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class StringListEditor(string id, string hint)
{
    private string input = "";

    public bool Draw(List<string> items)
    {
        var changed = false;
        ImGui.PushID(id);

        ImGui.SetNextItemWidth(250);
        var submitted = ImGui.InputTextWithHint("##input", hint, ref input, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button("Add") || submitted) && input.Trim() is { Length: > 0 } value)
        {
            if (!items.Exists(i => string.Equals(i, value, StringComparison.OrdinalIgnoreCase)))
            {
                items.Add(value);
                changed = true;
            }

            input = "";
        }

        for (var i = 0; i < items.Count; i++)
        {
            ImGui.PushID(i);
            if (ImGui.SmallButton("Remove"))
            {
                items.RemoveAt(i);
                changed = true;
                ImGui.PopID();
                break;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(items[i]);
            ImGui.PopID();
        }

        if (items.Count == 0)
            ImGui.TextDisabled("None.");

        ImGui.PopID();
        return changed;
    }
}
