using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Services;

public sealed record InstallResponse(string InstallId, string InstallToken);

public sealed record PairingResponse(string Code, long ExpiresAt);

public sealed record DeviceInfo(string DeviceId, string Name, string Status, string PublicKey, long? LastSeen = null);

public sealed class RelayApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class RelayApi : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Func<string> relayUrl;

    public RelayApi(Func<string> relayUrl)
    {
        this.relayUrl = relayUrl;
    }

    public async Task<InstallResponse> RegisterInstall(string publicKey, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "/api/installs", null, new { publicKey });
        return await Send<InstallResponse>(request, ct);
    }

    public async Task<PairingResponse> CreatePairing(string installToken)
    {
        using var request = Request(HttpMethod.Post, "/api/pairings", installToken, new { });
        return await Send<PairingResponse>(request);
    }

    public async Task<IReadOnlyList<DeviceInfo>> ListDevices(string installToken)
    {
        using var request = Request(HttpMethod.Get, "/api/devices", installToken, null);
        return await Send<List<DeviceInfo>>(request);
    }

    // A device the relay no longer knows counts as revoked.
    public async Task RevokeDevice(string installToken, string deviceId)
    {
        using var request = Request(HttpMethod.Delete, $"/api/devices/{Uri.EscapeDataString(deviceId)}", installToken, null);
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;
        await EnsureSuccess(response);
    }

    public void Dispose() => http.Dispose();

    private HttpRequestMessage Request(HttpMethod method, string path, string? token, object? body)
    {
        var request = new HttpRequestMessage(method, relayUrl().TrimEnd('/') + path);
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null)
            request.Content = JsonContent.Create(body, options: ProtocolJson.Options);
        return request;
    }

    private async Task<T> Send<T>(HttpRequestMessage request, CancellationToken ct = default)
    {
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<T>(ProtocolJson.Options, ct)
            ?? throw new RelayApiException((int)response.StatusCode, "Empty response from relay.");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? code = null;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("error", out var error))
                code = error.GetString();
        }
        catch (JsonException)
        {
        }

        throw new RelayApiException((int)response.StatusCode, $"Relay returned {(int)response.StatusCode}{(code != null ? $" ({code})" : "")}.");
    }
}
