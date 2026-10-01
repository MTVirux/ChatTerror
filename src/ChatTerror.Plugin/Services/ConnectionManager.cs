using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Services;

public sealed class ConnectionManager : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? registration;
    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly KeyStore keys;
    private readonly RelayApi api;
    private readonly RelayClient relay;
    private readonly IFramework framework;
    private readonly IPluginLog log;

    public ConnectionManager(Configuration config, Action saveConfig, KeyStore keys, RelayApi api, RelayClient relay, IFramework framework, IPluginLog log)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.keys = keys;
        this.api = api;
        this.relay = relay;
        this.framework = framework;
        this.log = log;
    }

    public bool Registering => registration != null;

    public string? LastError { get; private set; }

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
        relay.Start(config.RelayUrl, config.InstallToken);
    }

    // A new install id orphans every paired device, so the caller clears them.
    public void Reregister()
    {
        CancelRegistration();
        config.InstallId = null;
        config.InstallToken = null;
        saveConfig();
        Apply();
    }

    public void Dispose()
    {
        lifetime.Cancel();
        CancelRegistration();
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
                var install = await api.RegisterInstall(keys.PublicKey, ct);
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
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.Warning($"Install registration failed: {ex.Message}");
                await framework.RunOnFrameworkThread(() =>
                {
                    if (ct.IsCancellationRequested)
                        return;
                    registration = null;
                    current.Dispose();
                    LastError = $"Registration failed: {ex.Message}";
                });
            }
            catch (Exception)
            {
            }
        });
    }
}
