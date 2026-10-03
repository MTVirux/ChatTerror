using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChatTerror.Protocol;

namespace ChatTerror.Smoke.Tests;

public record InstallResponse(string InstallId, string InstallToken);
public record PairingResponse(string Code, long ExpiresAt);
public record PairingInfo(string InstallId, string PluginPublicKey);
public record ClaimResponse(string DeviceId, string DeviceToken);
public record VapidResponse(string PublicKey);

public static class LiveRelay
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static Uri BaseUrl
    {
        get
        {
            var url = Environment.GetEnvironmentVariable("SMOKE_URL");
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("SMOKE_URL is not set. Point it at a running relay, e.g. SMOKE_URL=http://localhost:8080");
            return new Uri(url);
        }
    }

    public static HttpClient Client(string? token = null)
    {
        var client = new HttpClient { BaseAddress = BaseUrl, Timeout = Timeout };
        if (token != null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<string> ReadTextAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}: {body}");
        return body;
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await ReadTextAsync(response), JsonSerializerOptions.Web)!;

    public static string NewPublicKey()
    {
        using var key = P256.Generate();
        return Base64Url.Encode(P256.PublicRaw(key));
    }

    public static async Task<InstallResponse> RegisterInstallAsync(string publicKey)
    {
        using var client = Client();
        return await ReadAsync<InstallResponse>(await client.PostAsJsonAsync("/api/installs", new { publicKey }));
    }

    public static async Task<ClientWebSocket> OpenSocketAsync()
    {
        var url = new UriBuilder(new Uri(BaseUrl, "/ws")) { Scheme = BaseUrl.Scheme == "https" ? "wss" : "ws" }.Uri;
        var socket = new ClientWebSocket();
        using var cts = new CancellationTokenSource(Timeout);
        await socket.ConnectAsync(url, cts.Token);
        return socket;
    }

    public static Task SendAsync(WebSocket socket, RelayFrame frame) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(ProtocolJson.Serialize(frame)), WebSocketMessageType.Text, true, CancellationToken.None);

    // Returns null once the socket is closed.
    public static async Task<RelayFrame?> ReceiveAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var buffer = new byte[Limits.MaxFrameBytes];
        var length = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(length), cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            length += result.Count;
            if (result.EndOfMessage)
                return ProtocolJson.Deserialize<RelayFrame>(Encoding.UTF8.GetString(buffer, 0, length));
        }
    }
}
