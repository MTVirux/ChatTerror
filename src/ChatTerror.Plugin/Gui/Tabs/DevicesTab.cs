using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ChatTerror.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class DevicesTab : ITab, IDisposable
{
    private static readonly Vector4 Green = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 Red = new(1f, 0.4f, 0.4f, 1f);

    private readonly Configuration config;
    private readonly RelayApi api;
    private readonly DeviceHub hub;
    private readonly IFramework framework;
    private readonly QrRenderer qr = new();

    // Written by background tasks; each is replaced as a whole so the UI never sees partial state.
    private volatile IReadOnlyDictionary<string, DeviceInfo> remoteDevices = new Dictionary<string, DeviceInfo>();
    private volatile PairingResponse? pairing;
    private volatile string? error;
    private volatile bool busy;
    private volatile bool disposed;

    private int lastDrawnFrame = -1;
    private string? confirmRevoke;

    public DevicesTab(Configuration config, RelayApi api, DeviceHub hub, IFramework framework)
    {
        this.config = config;
        this.api = api;
        this.hub = hub;
        this.framework = framework;
        hub.PairRequested += OnPairRequested;
    }

    public string Title => "Devices";

    public void Dispose()
    {
        disposed = true;
        hub.PairRequested -= OnPairRequested;
    }

    public void Draw()
    {
        var frame = ImGui.GetFrameCount();
        if (frame != lastDrawnFrame + 1)
            Refresh();
        lastDrawnFrame = frame;

        if (error is { } message)
            ImGui.TextColored(Red, message);

        DrawPending();
        DrawPairing();
        DrawPaired();
    }

    private void DrawPending()
    {
        if (hub.PendingPairs.Count == 0)
            return;

        ImGui.TextUnformatted("Waiting for approval");
        ImGui.TextWrapped("Only approve if the code matches the one shown on your phone.");
        foreach (var pair in hub.PendingPairs.ToList())
        {
            ImGui.PushID(pair.DeviceId);
            ImGui.BulletText($"{pair.DeviceName}   code {pair.Fingerprint}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Approve"))
                error = hub.Approve(pair) ? null : "Not connected to the relay.";
            ImGui.SameLine();
            if (ImGui.SmallButton("Reject"))
                error = hub.Reject(pair) ? null : "Not connected to the relay.";
            ImGui.PopID();
        }

        ImGui.Separator();
    }

    private void DrawPairing()
    {
        var current = pairing;
        var remaining = current == null
            ? TimeSpan.Zero
            : DateTimeOffset.FromUnixTimeMilliseconds(current.ExpiresAt) - DateTimeOffset.UtcNow;

        if (current == null || remaining <= TimeSpan.Zero)
        {
            using (ImRaii.Disabled(busy || config.InstallToken == null))
            {
                if (ImGui.Button("Pair new device"))
                    Run(async token => pairing = await api.CreatePairing(token));
            }

            ImGui.Separator();
            return;
        }

        var url = $"{config.RelayUrl.TrimEnd('/')}/#pair={current.Code}";
        ImGui.TextUnformatted($"Pairing code: {current.Code}");
        ImGui.TextUnformatted($"Expires in {remaining:m\\:ss}");
        ImGui.TextWrapped($"Scan with your phone or open {url}");
        if (ImGui.SmallButton("Copy link"))
            ImGui.SetClipboardText(url);
        ImGui.SameLine();
        if (ImGui.SmallButton("Cancel"))
            pairing = null;

        qr.Draw(url, 4f * ImGuiHelpers.GlobalScale);
        ImGui.Separator();
    }

    private void DrawPaired()
    {
        ImGui.TextUnformatted("Paired devices");
        ImGui.SameLine();
        using (ImRaii.Disabled(busy || config.InstallToken == null))
        {
            if (ImGui.SmallButton("Refresh"))
                Refresh();
        }

        if (config.Devices.Count == 0)
        {
            ImGui.TextDisabled("No devices paired yet.");
            return;
        }

        if (!ImGui.BeginTable("##devices", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("Status");
        ImGui.TableSetupColumn("Last seen");
        ImGui.TableSetupColumn("");
        ImGui.TableHeadersRow();

        foreach (var device in config.Devices.ToList())
        {
            ImGui.PushID(device.DeviceId);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(device.Name);

            ImGui.TableNextColumn();
            if (hub.OnlineDevices.Contains(device.DeviceId))
                ImGui.TextColored(Green, "Online");
            else
                ImGui.TextDisabled("Offline");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(LastSeen(device.DeviceId));

            ImGui.TableNextColumn();
            if (confirmRevoke == device.DeviceId)
            {
                if (ImGui.SmallButton("Confirm"))
                {
                    confirmRevoke = null;
                    Revoke(device.DeviceId);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton("Keep"))
                    confirmRevoke = null;
            }
            else
            {
                using (ImRaii.Disabled(busy))
                {
                    if (ImGui.SmallButton("Revoke"))
                        confirmRevoke = device.DeviceId;
                }
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private string LastSeen(string deviceId)
    {
        if (!remoteDevices.TryGetValue(deviceId, out var info) || info.LastSeen is not { } lastSeen || lastSeen <= 0)
            return "Never";
        return DateTimeOffset.FromUnixTimeMilliseconds(lastSeen).ToLocalTime().ToString("g");
    }

    private void OnPairRequested(PendingPair pair) => pairing = null;

    // Devices the relay no longer lists were deleted there, so drop them here too.
    private void Refresh() =>
        Run(async token =>
        {
            var startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var remote = (await api.ListDevices(token)).ToDictionary(d => d.DeviceId);
            remoteDevices = remote;
            await OnFramework(() =>
            {
                var gone = config.Devices.Where(d => d.PairedAt < startedAt && !remote.ContainsKey(d.DeviceId)).ToList();
                foreach (var device in gone)
                    hub.RemoveDevice(device.DeviceId);
            });
        });

    private void Revoke(string deviceId) =>
        Run(async token =>
        {
            await api.RevokeDevice(token, deviceId);
            await OnFramework(() => hub.RemoveDevice(deviceId));
        });

    private Task OnFramework(Action action) =>
        framework.RunOnFrameworkThread(() =>
        {
            if (!disposed)
                action();
        });

    private void Run(Func<string, Task> action)
    {
        if (disposed || busy || config.InstallToken is not { } token)
            return;

        busy = true;
        error = null;
        Task.Run(async () =>
        {
            try
            {
                await action(token);
            }
            catch (Exception ex) when (!disposed)
            {
                error = ex.Message;
            }
            catch (Exception)
            {
            }
            finally
            {
                busy = false;
            }
        });
    }
}
