using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Push;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests.Support;

public record InstallResponse(string InstallId, string InstallToken);
public record PairingResponse(string Code, long ExpiresAt);
public record PairingInfo(string InstallId, string PluginPublicKey);
public record ClaimResponse(string DeviceId, string DeviceToken);
public record DeviceInfo(string DeviceId, string Name, string Status, long LastSeen, string PublicKey);
public record DeviceMe(string DeviceId, string Status);
public record ErrorResponse(string Error);
public record VapidResponse(string PublicKey);

public sealed class RelayApp : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "chatterror-tests", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> settings = new()
    {
        ["Relay:PairingRequestsPerMinute"] = "1000",
        ["Relay:InstallsPerHour"] = "1000",
        ["Relay:PairingsPerInstallPerHour"] = "1000",
        ["Relay:MaxSocketsPerClient"] = "1000",
        ["Relay:SocketConnectsPerMinute"] = "1000",
        ["Relay:PushSubscriptionsPerMinute"] = "1000",
        ["Relay:PushServiceHosts"] = "1.1.1.1",
    };

    public RelayApp(Dictionary<string, string>? overrides = null)
    {
        foreach (var (key, value) in overrides ?? [])
            settings[key] = value;
    }

    private string DbPath => Path.Combine(directory, "relay.db");

    public FakePushSender Push { get; } = new();

    public MutableTimeProvider Time { get; } = new();

    public Action<IServiceCollection>? ExtraServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.UseSetting("Relay:DbPath", DbPath);
        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPushSender>(Push);
            services.AddSingleton<TimeProvider>(Time);
            ExtraServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Must match the connection string RelayStore builds, so only this app's pool is cleared.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(DbPath), Pooling = true }.ToString()))
            SqliteConnection.ClearPool(connection);
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException)
        {
        }
    }

    public static string NewPublicKey()
    {
        using var key = P256.Generate();
        return Base64Url.Encode(P256.PublicRaw(key));
    }

    public HttpClient Client(string? token = null, string? origin = null)
    {
        var client = CreateClient();
        if (token != null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (origin != null)
            client.DefaultRequestHeaders.Add("Origin", origin);
        return client;
    }

    public async Task<InstallResponse> RegisterInstallAsync()
    {
        var response = await Client().PostAsJsonAsync("/api/installs", new { publicKey = NewPublicKey() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InstallResponse>())!;
    }

    public async Task<string> CreatePairingAsync(string installToken)
    {
        var response = await Client(installToken).PostAsJsonAsync("/api/pairings", new { });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PairingResponse>())!.Code;
    }

    public Task<HttpResponseMessage> PostClaimAsync(string code, string name = "Phone") =>
        Client().PostAsJsonAsync($"/api/pairings/{code}/claim", new { devicePublicKey = NewPublicKey(), deviceName = name });

    public async Task<ClaimResponse> ClaimAsync(string code, string name = "Phone")
    {
        var response = await PostClaimAsync(code, name);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClaimResponse>())!;
    }

    public async Task<ClaimResponse> PairDeviceAsync(InstallResponse install, TestSocket plugin, bool approve = true)
    {
        var code = await CreatePairingAsync(install.InstallToken);
        var device = await ClaimAsync(code);
        var request = await plugin.ReceiveAsync<PairRequestFrame>();
        Assert.Equal(device.DeviceId, request.DeviceId);
        if (approve)
        {
            await plugin.SendAsync(new PairDecisionFrame(device.DeviceId, true));
            await plugin.BarrierAsync();
        }
        return device;
    }

    public async Task<TestSocket> OpenSocketAsync(string? origin = null)
    {
        var client = Server.CreateWebSocketClient();
        if (origin != null)
            client.ConfigureRequest = request => request.Headers.Origin = origin;
        var socket = await client.ConnectAsync(new Uri(Server.BaseAddress, "ws"), CancellationToken.None);
        return new TestSocket(socket);
    }

    public async Task<TestSocket> ConnectAsync(string token)
    {
        var socket = await OpenSocketAsync();
        await socket.SendAsync(new AuthFrame(token));
        var frame = await socket.NextAsync();
        Assert.IsType<AuthOkFrame>(frame);
        return socket;
    }
}
