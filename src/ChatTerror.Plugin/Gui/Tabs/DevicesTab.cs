using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ChatTerror.Plugin.Services;
using ChatTerror.Protocol;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class DevicesTab : ITab, IDisposable
{
    // Code is the full code shown to the user: the relay code followed by the local secret.
    private sealed record ActivePairing(string Code, long ExpiresAt);

    private static readonly Vector4 Green = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 Red = new(1f, 0.4f, 0.4f, 1f);

    private readonly Configuration config;
    private readonly RelayApi api;
    private readonly DeviceHub hub;
    private readonly IFramework framework;
    private readonly QrRenderer qr = new();

    // Written by background tasks; each is replaced as a whole so the UI never sees partial state.
    private volatile IReadOnlyDictionary<string, DeviceInfo> remoteDevices = new Dictionary<string, DeviceInfo>();
    private volatile ActivePairing? pairing;
    private volatile string? error;
    private volatile bool busy;
    private volatile bool disposed;

    private int lastDrawnFrame = -1;
    private string? confirmRevoke;

    // Hidden by default for streams and screenshots; a new pairing is a new instance, so it starts hidden again.
    private ActivePairing? revealedPairing;

    public DevicesTab(Configuration config, RelayApi api, DeviceHub hub, IFramework framework)
    {
        this.config = config;
        this.api = api;
        this.hub = hub;
        this.framework = framework;
        hub.PairRequested += OnPairRequested;
        hub.Connected += OnConnected;
    }

    public string Title => "Devices";

    public void Dispose()
    {
        disposed = true;
        hub.PairRequested -= OnPairRequested;
        hub.Connected -= OnConnected;
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
            if (pair.Verified)
            {
                ImGui.BulletText($"{pair.DeviceName}   code {pair.Fingerprint}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Approve"))
                    error = hub.Approve(pair) ? null : "Not connected to the relay.";
            }
            else
            {
                ImGui.BulletText($"{pair.DeviceName}   unverified");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("This request did not come from the pairing code shown here, so it can't be approved. Reject it and pair again.");
            }

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
                    Run(StartPairing);
            }

            ImGui.Separator();
            return;
        }

        var url = $"{config.RelayUrl.TrimEnd('/')}/#pair={current.Code}";
        var revealed = revealedPairing == current;
        ImGui.TextUnformatted($"Pairing code: {(revealed ? current.Code : Mask(current.Code))}");
        ImGui.SameLine();
        if (ImGui.SmallButton(revealed ? "Hide" : "Show"))
            revealedPairing = revealed ? null : current;
        ImGui.TextUnformatted($"Expires in {remaining:m\\:ss}");
        ImGui.TextWrapped(revealed ? $"Scan with your phone or open {url}" : "Click Show to see the code, link and QR code.");
        if (ImGui.SmallButton("Copy link"))
            ImGui.SetClipboardText(url);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The link contains the pairing secret. Only share it with your own device.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Cancel"))
        {
            pairing = null;
            hub.CancelPairing();
        }

        if (revealed)
            qr.Draw(url, 4f * ImGuiHelpers.GlobalScale);
        ImGui.Separator();
    }

    private static string Mask(string code) => string.Concat(code.Select(c => c == '-' ? '-' : '*'));

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

    private void OnPairRequested(PendingPair pair)
    {
        if (pair.Verified)
            pairing = null;
    }

    // The secret never goes to the relay; it only appears in the code and QR shown here.
    private async Task StartPairing(string token)
    {
        var response = await api.CreatePairing(token);
        var secret = PairingSecret.Generate();
        await OnFramework(() => hub.StartPairing(secret, response.ExpiresAt));
        pairing = new ActivePairing($"{response.Code}-{PairingSecret.Format(secret)}", response.ExpiresAt);
    }

    private void Refresh() => Run(Reconcile);

    // Not gated on busy, so a reconnect always catches devices removed while the plugin was offline.
    private void OnConnected()
    {
        if (disposed || config.InstallToken is not { } token)
            return;

        Task.Run(async () =>
        {
            try
            {
                await Reconcile(token);
            }
            catch (Exception ex) when (!disposed)
            {
                error = ex.Message;
            }
            catch (Exception)
            {
            }
        });
    }

    // Devices the relay no longer lists were deleted there, so drop them here too.
    private async Task Reconcile(string token)
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
    }

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
