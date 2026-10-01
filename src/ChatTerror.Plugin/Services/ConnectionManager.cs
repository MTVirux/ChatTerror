using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Services;

public sealed class ConnectionManager : IDisposable
{
    private static readonly TimeSpan MinRetry = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AutoReregisterCooldown = TimeSpan.FromMinutes(10);

    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? registration;
    private DateTime lastAutoReregister = DateTime.MinValue;
    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly KeyStore keys;
    private readonly RelayApi api;
    private readonly RelayClient relay;
    private readonly DeviceHub hub;
    private readonly IFramework framework;
    private readonly IPluginLog log;

    public ConnectionManager(Configuration config, Action saveConfig, KeyStore keys, RelayApi api, RelayClient relay, DeviceHub hub, IFramework framework, IPluginLog log)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.keys = keys;
        this.api = api;
        this.relay = relay;
        this.hub = hub;
        this.framework = framework;
        this.log = log;
        relay.StateChanged += OnStateChanged;
    }

    public bool Registering => registration != null;

    public string? LastError { get; private set; }

    // Stays visible after a successful re-register, unlike LastError.
    public string? Notice { get; set; }

    public RelayState State => relay.State;

    // Call from the framework thread after any change to Enabled, RelayUrl or the install token.
    public void Apply()
    {
        if (lifetime.IsCancellationRequested)
            return;

        if (!config.Enabled)
        {
            CancelRegistration();
            relay.Stop();
            return;
        }

        if (config.InstallToken == null)
        {
            relay.Stop();
            Register();
            return;
        }

        LastError = null;
        try
        {
            relay.Start(config.RelayUrl, config.InstallToken);
        }
        catch (FormatException ex)
        {
            log.Warning($"Invalid relay URL: {ex.Message}");
            LastError = "The relay URL is invalid.";
        }
    }

    // A new install id orphans every paired device, so they are removed here and on the old relay.
    public void Reregister(string? newRelayUrl = null)
    {
        RevokeOnRelay(config.RelayUrl, config.InstallToken, config.Devices.Select(d => d.DeviceId).ToList());
        CancelRegistration();
        relay.Stop();
        hub.ClearDevices();
        if (newRelayUrl != null)
            config.RelayUrl = newRelayUrl;
        config.InstallId = null;
        config.InstallToken = null;
        saveConfig();
        Apply();
    }

    public void Dispose()
    {
        relay.StateChanged -= OnStateChanged;
        lifetime.Cancel();
        CancelRegistration();
        lifetime.Dispose();
    }

    // The relay deletes long-unused installs without devices, so with nothing paired registering again loses nothing.
    private void OnStateChanged(RelayState state)
    {
        if (state != RelayState.AuthFailed)
            return;

        var rejectedToken = config.InstallToken;
        framework.RunOnFrameworkThread(() =>
        {
            if (lifetime.IsCancellationRequested || !config.Enabled || config.Devices.Count > 0 || config.InstallToken != rejectedToken)
                return;
            if (DateTime.UtcNow - lastAutoReregister < AutoReregisterCooldown)
            {
                log.Warning("Relay rejected the install again soon after an automatic re-register, waiting for a manual one.");
                return;
            }
            lastAutoReregister = DateTime.UtcNow;
            log.Information("Relay rejected the install and no devices are paired, registering again.");
            Reregister();
        });
    }

    private void RevokeOnRelay(string relayUrl, string? token, IReadOnlyList<string> deviceIds)
    {
        if (token == null || deviceIds.Count == 0)
            return;

        Task.Run(async () =>
        {
            foreach (var deviceId in deviceIds)
            {
                try
                {
                    await api.RevokeDevice(token, deviceId, relayUrl);
                }
                catch (Exception ex)
                {
                    log.Debug($"Could not remove device {deviceId} from the old relay: {ex.Message}");
                }
            }
        });
    }

    private void CancelRegistration()
    {
        registration?.Cancel();
        registration?.Dispose();
        registration = null;
    }

    private void Register()
    {
        if (registration != null)
            return;

        var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var ct = current.Token;
        registration = current;
        LastError = null;
        Task.Run(async () =>
        {
            try
            {
                var install = await RegisterWithRetry(ct);
                await framework.RunOnFrameworkThread(() =>
                {
                    if (ct.IsCancellationRequested)
                        return;
                    registration = null;
                    current.Dispose();
                    config.InstallId = install.InstallId;
                    config.InstallToken = install.InstallToken;
                    saveConfig();
                    Apply();
                });
            }
            catch (Exception)
            {
            }
        });
    }

    // Retries until it succeeds or the registration is cancelled (disabled, re-registered or disposed).
    private async Task<InstallResponse> RegisterWithRetry(CancellationToken ct)
    {
        var delay = MinRetry;
        while (true)
        {
            try
            {
                return await api.RegisterInstall(keys.PublicKey, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.Warning($"Install registration failed: {ex.Message}");
                var message = $"Registration failed: {ex.Message} Retrying in {delay.TotalSeconds:0} s.";
                await framework.RunOnFrameworkThread(() =>
                {
                    if (!ct.IsCancellationRequested)
                        LastError = message;
                });
            }

            await Task.Delay(delay, ct);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetry.Ticks));
        }
    }
}
