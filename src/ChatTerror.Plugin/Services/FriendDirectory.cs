using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Services;

// Pairs this install with friends' installs and exchanges friend profiles with them. Config is only touched on the
// framework thread; relay calls run one at a time in the background.
public sealed class FriendDirectory : IDisposable
{
    private const long RefreshIntervalMs = 10 * 60_000;
    private const long ProfileCheckIntervalMs = 10_000;

    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly KeyStore keys;
    private readonly RelayApi api;
    private readonly DeviceHub hub;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly SemaphoreSlim gate = new(1, 1);

    // The character list last uploaded to each friend, framework thread only.
    private readonly Dictionary<string, string> uploadedProfiles = new();

    private long lastRefresh = long.MinValue / 2;
    private long lastProfileCheck = long.MinValue / 2;
    private bool refreshDue = true;
    private bool running;
    private volatile bool busy;
    private volatile string? status;
    private volatile bool disposed;

    private sealed record ProfileUpload(string InstallId, string PublicKey, List<string> Characters, string Key);

    public FriendDirectory(Configuration config, Action saveConfig, KeyStore keys, RelayApi api, DeviceHub hub, IFramework framework, IPluginLog log)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.keys = keys;
        this.api = api;
        this.hub = hub;
        this.framework = framework;
        this.log = log;
        hub.Connected += OnConnected;
        hub.FriendsChanged += OnFriendsChanged;
        framework.Update += OnUpdate;
    }

    public string? Status => status;

    // True while a code is being created or redeemed, or a friend removed.
    public bool Busy => busy;

    public void Dispose()
    {
        disposed = true;
        framework.Update -= OnUpdate;
        hub.FriendsChanged -= OnFriendsChanged;
        hub.Connected -= OnConnected;
    }

    private void OnConnected()
    {
        uploadedProfiles.Clear();
        refreshDue = true;
    }

    private void OnFriendsChanged() => refreshDue = true;

    private void OnUpdate(IFramework unused)
    {
        if (running || config.InstallToken is not { } token)
            return;

        var now = Environment.TickCount64;
        if (refreshDue || now - lastRefresh >= RefreshIntervalMs)
        {
            refreshDue = false;
            lastRefresh = now;
            Start(() => Refresh(token));
            return;
        }

        if (now - lastProfileCheck < ProfileCheckIntervalMs)
            return;
        lastProfileCheck = now;
        var due = DueProfiles();
        if (due.Count > 0)
            Start(() => UploadProfiles(token, due));
    }

    public Task<FriendCode?> CreateInvite(string scope) =>
        Exclusive<FriendCode?>(async token =>
        {
            var secret = FriendCode.NewSecret();
            var created = await api.CreateFriendInvite(token, scope, FriendProof.InviteTag(secret, keys.PublicKey, scope));
            var invite = new OwnFriendInvite { Id = created.Id, Secret = secret, Scope = scope, ExpiresAt = created.ExpiresAt };
            await OnFramework(() =>
            {
                config.FriendInvites.Add(invite);
                saveConfig();
            });
            status = null;
            return new FriendCode(created.Id, secret);
        }, ex =>
        {
            status = ex is RelayApiException { Code: "tooManyInvites" }
                ? $"You already have {Limits.MaxFriendInvites} active codes, cancel one first."
                : $"Could not create a code: {ex.Message}";
            return null;
        });

    public async Task<string> Redeem(string code, string myScope)
    {
        var (parsed, error) = FriendTrust.ParseRedeem(code, myScope);
        var result = parsed == null
            ? error!
            : await Exclusive(token => Claim(token, parsed, myScope), ex => $"Could not pair: {ex.Message}");
        status = result;
        return result;
    }

    private async Task<string> Claim(string token, FriendCode code, string myScope)
    {
        var invite = await api.GetFriendInvite(token, code.Id);
        if (invite == null || !FriendTrust.VerifyInvite(code.Secret, invite.InstallPublicKey, invite.Scope, invite.Tag))
            return FriendTrust.InvalidCode;
        if (invite.InstallPublicKey == keys.PublicKey)
            return FriendTrust.ClaimError(409, "selfInvite");

        string sealedClaim;
        try
        {
            var mac = FriendProof.ClaimMac(code.Secret, invite.InstallPublicKey, invite.Scope, keys.PublicKey, myScope);
            sealedClaim = FriendClaims.Seal(invite.InstallPublicKey, new FriendClaim(keys.PublicKey, myScope, mac));
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            return FriendTrust.InvalidCode;
        }

        try
        {
            await api.ClaimFriendInvite(token, code.Id, sealedClaim);
        }
        catch (RelayApiException ex) when (ex.StatusCode is 400 or 404 or 409)
        {
            return FriendTrust.ClaimError(ex.StatusCode, ex.Code);
        }

        var pending = new PendingFriend
        {
            InviteId = code.Id, InstallId = invite.InstallId, PublicKey = invite.InstallPublicKey,
            TheirScope = invite.Scope, MyScope = myScope, CreatedAt = Now(),
        };
        await OnFramework(() =>
        {
            config.PendingFriends.RemoveAll(p => p.InstallId == pending.InstallId);
            config.PendingFriends.Add(pending);
            saveConfig();
        });
        return FriendTrust.CodeSent;
    }

    public Task CancelInvite(string id) =>
        Exclusive(async token =>
        {
            await api.DeleteFriendInvite(token, id);
            await OnFramework(() =>
            {
                config.FriendInvites.RemoveAll(i => i.Id == id);
                saveConfig();
            });
            return true;
        }, ex => Failed("Could not cancel the code", ex));

    public Task Remove(string installId) =>
        Exclusive(async token =>
        {
            await api.RemoveFriend(token, installId);
            await OnFramework(() =>
            {
                config.PairedFriends.RemoveAll(f => f.InstallId == installId);
                uploadedProfiles.Remove(installId);
                saveConfig();
                hub.BroadcastSettings();
            });
            return true;
        }, ex => Failed("Could not remove the friend", ex));

    private bool Failed(string what, Exception ex)
    {
        status = $"{what}: {ex.Message}";
        return false;
    }

    private async Task Refresh(string token)
    {
        var list = await api.ListFriends(token);
        var listed = list.Friends.Select(f => (f.InstallId, f.PublicKey)).ToList();
        var unknown = new List<string>();
        var friendKeys = await OnFramework(() =>
        {
            var sync = FriendTrust.SyncFriends(config.PairedFriends, config.PendingFriends, listed, Now());
            if (sync.Promoted)
                status = "Paired with a new friend.";
            else if (sync.PendingExpired)
                status = "Your friend code was not accepted.";
            unknown = sync.Unknown;
            config.PairedFriends = sync.Paired;
            config.PendingFriends = sync.Pending;
            var ids = list.Invites.Select(i => i.Id).ToHashSet();
            config.FriendInvites.RemoveAll(i => !ids.Contains(i.Id));
            foreach (var id in uploadedProfiles.Keys.Where(id => config.PairedFriends.All(f => f.InstallId != id)).ToList())
                uploadedProfiles.Remove(id);
            saveConfig();
            return config.PairedFriends.ToDictionary(f => f.InstallId, f => f.PublicKey);
        });

        var profiles = new List<(string InstallId, string PublicKey, FriendProfile Profile)>();
        foreach (var friend in list.Friends)
        {
            if (friend.Profile is { } envelope && friendKeys.TryGetValue(friend.InstallId, out var key)
                && FriendProfiles.Open(keys.Key, envelope, key) is { } profile)
                profiles.Add((friend.InstallId, key, profile));
        }
        await OnFramework(() =>
        {
            foreach (var (installId, key, profile) in profiles)
            {
                if (config.PairedFriends.FirstOrDefault(f => f.InstallId == installId && f.PublicKey == key) is { } friend)
                    FriendTrust.AcceptProfile(friend, profile);
            }
            saveConfig();
            hub.BroadcastSettings();
        });

        // Accept, refresh and claims are serialized, so no pairing of ours can be half done here.
        foreach (var installId in unknown)
            await Try("Removing an unknown friendship", () => api.RemoveFriend(token, installId));

        foreach (var invite in list.Invites)
            await Try("Handling a friend code", () => HandleInvite(token, invite));

        await UploadProfiles(token, await OnFramework(DueProfiles));
    }

    private async Task Try(string what, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            log.Warning($"{what} failed: {ex.Message}");
        }
    }

    // Accepts a verified claim on one of our codes and cancels codes with a bad claim or that we no longer know.
    private async Task HandleInvite(string token, RelayFriendInvite invite)
    {
        var own = await OnFramework(() => config.FriendInvites.FirstOrDefault(i => i.Id == invite.Id));
        var claim = invite.Claim == null ? null : FriendClaims.Open(keys.Key, invite.Claim.Sealed);
        switch (FriendTrust.DecideInvite(own != null, own?.Secret, own?.Scope, invite.Claim != null, claim, invite.Claim?.PublicKey, keys.PublicKey))
        {
            case InviteAction.Wait:
                return;
            case InviteAction.Delete:
                if (own != null)
                    status = "Someone used one of your friend codes but it did not check out, so the code was cancelled.";
                await DeleteInvite(token, invite.Id);
                return;
        }

        try
        {
            await api.AcceptFriendInvite(token, invite.Id);
        }
        catch (RelayApiException ex) when (ex.StatusCode is 404 or 409)
        {
            status = ex.Code == "tooManyFriends"
                ? $"A friend code was used but one of you already has {Limits.MaxPairedFriends} paired friends."
                : null;
            await DeleteInvite(token, invite.Id);
            return;
        }

        var friend = new PairedFriend { InstallId = invite.Claim!.InstallId, PublicKey = claim!.InstallPublicKey, TheirScope = claim.Scope, MyScope = own!.Scope };
        await OnFramework(() =>
        {
            config.PairedFriends.RemoveAll(f => f.InstallId == friend.InstallId);
            config.PairedFriends.Add(friend);
            config.FriendInvites.RemoveAll(i => i.Id == invite.Id);
            saveConfig();
            hub.BroadcastSettings();
        });
        status = "Paired with a new friend.";
    }

    private async Task DeleteInvite(string token, string id)
    {
        await api.DeleteFriendInvite(token, id);
        await OnFramework(() =>
        {
            config.FriendInvites.RemoveAll(i => i.Id == id);
            saveConfig();
        });
    }

    private List<ProfileUpload> DueProfiles()
    {
        var due = new List<ProfileUpload>();
        foreach (var friend in config.PairedFriends)
        {
            var characters = FriendTrust.ProfileCharacters(config.TellCharacters, friend);
            var key = string.Join(',', characters);
            if (uploadedProfiles.GetValueOrDefault(friend.InstallId) != key)
                due.Add(new ProfileUpload(friend.InstallId, friend.PublicKey, characters, key));
        }
        return due;
    }

    private async Task UploadProfiles(string token, List<ProfileUpload> due)
    {
        foreach (var upload in due)
        {
            try
            {
                var envelope = FriendProfiles.Seal(keys.Key, upload.PublicKey, new FriendProfile(upload.Characters, Now()));
                await api.PutFriendProfile(token, upload.InstallId, envelope);
                await OnFramework(() => uploadedProfiles[upload.InstallId] = upload.Key);
            }
            catch (Exception ex) when (ex is not ObjectDisposedException)
            {
                log.Warning($"Friend profile upload failed: {ex.Message}");
            }
        }
    }

    private void Start(Func<Task> work)
    {
        running = true;
        Task.Run(async () =>
        {
            await gate.WaitAsync();
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                log.Warning($"Friend list update failed: {ex.Message}");
            }
            finally
            {
                gate.Release();
                await OnFramework(() => running = false);
            }
        });
    }

    // UI actions never throw, failed returns what they report instead.
    private Task<T> Exclusive<T>(Func<string, Task<T>> work, Func<Exception, T> failed)
    {
        if (disposed || config.InstallToken is not { } token)
            return Task.FromResult(failed(new InvalidOperationException("Not registered with the relay yet.")));

        busy = true;
        return Task.Run(async () =>
        {
            await gate.WaitAsync();
            try
            {
                return await work(token);
            }
            catch (Exception ex)
            {
                log.Warning($"Friend action failed: {ex.Message}");
                return failed(ex);
            }
            finally
            {
                gate.Release();
                busy = false;
            }
        });
    }

    private Task OnFramework(Action action) =>
        framework.RunOnFrameworkThread(() =>
        {
            if (!disposed)
                action();
        });

    private Task<T> OnFramework<T>(Func<T> func) => framework.RunOnFrameworkThread(func);

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
