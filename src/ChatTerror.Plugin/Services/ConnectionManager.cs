using System;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Services;

public sealed class ConnectionManager
{
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

    public bool Registering { get; private set; }

    public string? LastError { get; private set; }

    public RelayState State => relay.State;

    // Call from the framework thread after any change to Enabled, RelayUrl or the install token.
    public void Apply()
    {
        if (!config.Enabled)
        {
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
        config.InstallId = null;
        config.InstallToken = null;
        saveConfig();
        Apply();
    }

    private void Register()
    {
        if (Registering)
            return;

        Registering = true;
        LastError = null;
        Task.Run(async () =>
        {
            try
            {
                var install = await api.RegisterInstall(keys.PublicKey);
                await framework.RunOnFrameworkThread(() =>
                {
                    config.InstallId = install.InstallId;
                    config.InstallToken = install.InstallToken;
                    saveConfig();
                    Registering = false;
                    Apply();
                });
            }
            catch (Exception ex)
            {
                log.Warning($"Install registration failed: {ex.Message}");
                await framework.RunOnFrameworkThread(() =>
                {
                    LastError = $"Registration failed: {ex.Message}";
                    Registering = false;
                });
            }
        });
    }
}
