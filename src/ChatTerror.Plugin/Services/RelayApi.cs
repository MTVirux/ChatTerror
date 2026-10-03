using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Services;

public sealed record InstallResponse(string InstallId, string InstallToken);

public sealed record PairingResponse(string Code, long ExpiresAt);

public sealed record DeviceInfo(string DeviceId, string Name, string Status, string PublicKey, long? LastSeen = null);

public sealed record FriendInviteCreated(string Id, long ExpiresAt);

public sealed record FriendInviteInfo(string InstallId, string InstallPublicKey, string Scope, string Tag);

public sealed record FriendClaimInfo(string InstallId, string PublicKey, string Sealed);

public sealed record RelayFriendInvite(string Id, string Scope, long ExpiresAt, FriendClaimInfo? Claim);

// Profile is the envelope that friend uploaded for us.
public sealed record RelayFriend(string InstallId, string PublicKey, string? Profile);

public sealed record FriendList(List<RelayFriend> Friends, List<RelayFriendInvite> Invites);

public sealed class RelayApiException(int statusCode, string message, string? code = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
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

    // A device the relay no longer knows counts as revoked. relayUrl overrides the configured relay.
    public async Task RevokeDevice(string installToken, string deviceId, string? relayUrl = null)
    {
        using var request = Request(HttpMethod.Delete, $"/api/devices/{Uri.EscapeDataString(deviceId)}", installToken, null, relayUrl);
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;
        await EnsureSuccess(response);
    }

    public async Task<FriendInviteCreated> CreateFriendInvite(string token, string scope, string tag)
    {
        using var request = Request(HttpMethod.Post, "/api/friends/invites", token, new { scope, tag });
        return await Send<FriendInviteCreated>(request);
    }

    // Null when the invite is unknown, expired or already claimed.
    public async Task<FriendInviteInfo?> GetFriendInvite(string token, string id)
    {
        using var request = Request(HttpMethod.Get, $"/api/friends/invites/{Uri.EscapeDataString(id)}", token, null);
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<FriendInviteInfo>(ProtocolJson.Options);
    }

    public Task ClaimFriendInvite(string token, string id, string @sealed) =>
        SendEmpty(Request(HttpMethod.Post, $"/api/friends/invites/{Uri.EscapeDataString(id)}/claim", token, new { @sealed }));

    public Task AcceptFriendInvite(string token, string id) =>
        SendEmpty(Request(HttpMethod.Post, $"/api/friends/invites/{Uri.EscapeDataString(id)}/accept", token, new { }));

    // An invite the relay no longer knows is gone already.
    public Task DeleteFriendInvite(string token, string id) =>
        SendEmpty(Request(HttpMethod.Delete, $"/api/friends/invites/{Uri.EscapeDataString(id)}", token, null), allowNotFound: true);

    public async Task<FriendList> ListFriends(string token)
    {
        using var request = Request(HttpMethod.Get, "/api/friends", token, null);
        return await Send<FriendList>(request);
    }

    public Task PutFriendProfile(string token, string installId, string envelope) =>
        SendEmpty(Request(HttpMethod.Put, $"/api/friends/{Uri.EscapeDataString(installId)}/profile", token, new { envelope }));

    public Task RemoveFriend(string token, string installId) =>
        SendEmpty(Request(HttpMethod.Delete, $"/api/friends/{Uri.EscapeDataString(installId)}", token, null), allowNotFound: true);

    public Task DeleteTellBundle(string token) => SendEmpty(Request(HttpMethod.Delete, "/api/tells/bundle", token, null));

    public async Task PutTellBundle(string token, SignedTellBundle bundle)
    {
        using var request = Request(HttpMethod.Put, "/api/tells/bundle", token, bundle);
        using var response = await http.SendAsync(request);
        await EnsureSuccess(response);
    }

    // installId is a paired friend's install or "self". Null when they have no bundle, e.g. relayed tells are off.
    public async Task<SignedTellBundle?> GetTellBundle(string token, string installId)
    {
        using var request = Request(HttpMethod.Get, $"/api/tells/bundles/{Uri.EscapeDataString(installId)}", token, null);
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<SignedTellBundle>(ProtocolJson.Options);
    }

    public void Dispose() => http.Dispose();

    private HttpRequestMessage Request(HttpMethod method, string path, string? token, object? body, string? baseUrl = null)
    {
        var uri = new Uri((baseUrl ?? relayUrl()).Trim().TrimEnd('/') + path);
        if (!RelayUrl.IsAllowed(uri))
            throw new InvalidOperationException(RelayUrl.InsecureError);

        var request = new HttpRequestMessage(method, uri);
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

    private async Task SendEmpty(HttpRequestMessage request, bool allowNotFound = false)
    {
        using (request)
        {
            using var response = await http.SendAsync(request);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return;
            await EnsureSuccess(response);
        }
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

        throw new RelayApiException((int)response.StatusCode, $"Relay returned {(int)response.StatusCode}{(code != null ? $" ({code})" : "")}.", code);
    }
}
