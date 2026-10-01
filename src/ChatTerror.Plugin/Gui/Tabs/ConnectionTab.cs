using System;
using System.Numerics;
using ChatTerror.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class ConnectionTab(Configuration config, ConnectionManager connection, DeviceHub hub, Action save) : ITab
{
    private static readonly Vector4 Green = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Red = new(1f, 0.4f, 0.4f, 1f);

    private string? relayUrl;

    public string Title => "Connection";

    public void Draw()
    {
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            config.Enabled = enabled;
            save();
            connection.Apply();
        }

        relayUrl ??= config.RelayUrl;
        ImGui.SetNextItemWidth(300);
        ImGui.InputText("Relay URL", ref relayUrl, 256);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ApplyRelayUrl();
        ImGui.TextDisabled("Changing the relay registers a new install and removes all paired devices.");

        ImGui.Spacing();
        DrawStatus();

        ImGui.Spacing();
        ImGui.Separator();
        if (config.InstallToken == null)
        {
            using (ImRaii.Disabled(!config.Enabled || connection.Registering))
            {
                if (ImGui.Button("Retry registration"))
                    connection.Apply();
            }

            return;
        }

        ImGui.TextWrapped("Re-registering creates a new install on the relay. All paired devices are removed and must pair again.");
        var ctrl = ImGui.GetIO().KeyCtrl;
        using (ImRaii.Disabled(!ctrl || connection.Registering))
        {
            if (ImGui.Button("Re-register"))
            {
                hub.ClearDevices();
                connection.Reregister();
            }
        }

        if (!ctrl)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(hold Ctrl)");
        }
    }

    private void ApplyRelayUrl()
    {
        var value = relayUrl?.Trim().TrimEnd('/') ?? "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            relayUrl = config.RelayUrl;
            return;
        }

        relayUrl = value;
        if (value == config.RelayUrl)
            return;

        // Install tokens and devices belong to one relay, so a new relay means a fresh install.
        config.RelayUrl = value;
        hub.ClearDevices();
        connection.Reregister();
    }

    private void DrawStatus()
    {
        if (!config.Enabled)
        {
            ImGui.TextDisabled("Disabled");
            return;
        }

        if (connection.Registering)
        {
            ImGui.TextColored(Yellow, "Registering with the relay...");
            return;
        }

        if (connection.LastError is { } error)
        {
            ImGui.TextColored(Red, error);
            return;
        }

        switch (connection.State)
        {
            case RelayState.Connected:
                ImGui.TextColored(Green, "Connected");
                break;
            case RelayState.Connecting:
                ImGui.TextColored(Yellow, "Connecting...");
                break;
            case RelayState.AuthFailed:
                ImGui.TextColored(Red, "The relay rejected this install. Re-register to continue.");
                break;
            default:
                ImGui.TextColored(Red, "Disconnected, retrying...");
                break;
        }
    }
}
