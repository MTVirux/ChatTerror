using System;
using System.Numerics;
using ChatTerror.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class ConnectionTab(Configuration config, ConnectionManager connection, Action save) : ITab
{
    private static readonly Vector4 Green = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Red = new(1f, 0.4f, 0.4f, 1f);

    private const string ConfirmPopup = "Change relay?##confirmRelay";

    private string? relayUrl;
    private string? pendingUrl;
    private string? urlError;

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
        ImGui.InputText("##relayUrl", ref relayUrl, 256);
        ImGui.SameLine();
        if (ImGui.Button("Apply"))
            RequestRelayChange();
        ImGui.SameLine();
        ImGui.TextUnformatted("Relay URL");
        if (urlError != null)
            ImGui.TextColored(Red, urlError);
        ImGui.TextDisabled("Changing the relay registers a new install and removes all paired devices.");
        DrawConfirmPopup();

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
                connection.Reregister();
        }

        if (!ctrl)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(hold Ctrl)");
        }
    }

    private void RequestRelayChange()
    {
        urlError = null;
        var value = Normalize(relayUrl);
        if (value == null)
        {
            urlError = "Enter a full http:// or https:// URL.";
            return;
        }

        if (value == Normalize(config.RelayUrl))
        {
            relayUrl = config.RelayUrl;
            return;
        }

        pendingUrl = value;
        ImGui.OpenPopup(ConfirmPopup);
    }

    private void DrawConfirmPopup()
    {
        if (!ImGui.BeginPopupModal(ConfirmPopup, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted("Changing the relay removes all paired devices. Continue?");
        ImGui.TextDisabled(pendingUrl ?? "");
        if (ImGui.Button("Continue") && pendingUrl != null)
        {
            // Install tokens and devices belong to one relay, so a new relay means a fresh install.
            relayUrl = pendingUrl;
            connection.Reregister(pendingUrl);
            pendingUrl = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            pendingUrl = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    // Uri lowercases scheme and host, so equal relays normalize to the same string.
    private static string? Normalize(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;
        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}";
    }

    private void DrawStatus()
    {
        if (connection.Notice is { } notice)
        {
            ImGui.TextColored(Yellow, notice);
            ImGui.SameLine();
            if (ImGui.SmallButton("Dismiss"))
                connection.Notice = null;
        }

        if (!config.Enabled)
        {
            ImGui.TextDisabled("Disabled");
            return;
        }

        if (connection.LastError is { } error)
        {
            ImGui.TextColored(Red, error);
            return;
        }

        if (connection.Registering)
        {
            ImGui.TextColored(Yellow, "Registering with the relay...");
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
