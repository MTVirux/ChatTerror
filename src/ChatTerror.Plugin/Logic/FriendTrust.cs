using System;
using System.Collections.Generic;
using System.Linq;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

// Another ChatTerror install we paired with using a friend code.
public sealed class PairedFriend
{
    public string InstallId { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string TheirScope { get; set; } = FriendScopes.Account;
    public string MyScope { get; set; } = FriendScopes.Account;

    // Their in-scope characters from the last accepted profile.
    public List<string> Characters { get; set; } = new();
    public long ProfileIssuedAt { get; set; }
    public long BundleIssuedAt { get; set; }
}

// A code we redeemed whose inviter has not accepted our claim yet. PublicKey was checked against the code's tag.
public sealed class PendingFriend
{
    public string InviteId { get; set; } = "";
    public string InstallId { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string TheirScope { get; set; } = "";
    public string MyScope { get; set; } = "";
    public long CreatedAt { get; set; }
}

public sealed record FriendRoute(TellCharacter From, TellFriend To, PairedFriend Friend);

public enum RouteError { None, NotPaired, Conflict }

// Unknown are friendships the relay lists that we have no record of, e.g. after an accept whose response was lost.
public sealed record FriendSync(List<PairedFriend> Paired, List<PendingFriend> Pending, bool PendingExpired, bool Promoted, List<string> Unknown);

public enum InviteAction { Wait, Accept, Delete }

public enum TellOrigin { Friend, Own, Forged }

public static class FriendTrust
{
    public const string BadCode = "Enter the full 24 character code.";
    public const string BadScope = "Pick which of your characters to share.";
    public const string InvalidCode = "Invalid or expired code.";
    public const string CodeSent = "Code sent. You are paired once your friend's game confirms it.";

    // Characters of the friend that we may talk to: their profile, inside their scope.
    public static IEnumerable<string> Reachable(PairedFriend friend) =>
        friend.Characters.Where(h => FriendScopes.Includes(friend.TheirScope, h)).Distinct();

    // The one friend a character of theirs belongs to, ignoring our scopes so a conflict is a conflict from every alt.
    // A friend listing one of our own characters never gets it.
    private static (PairedFriend? Friend, RouteError Error) Owner(IReadOnlyList<TellCharacter> characters, IReadOnlyList<PairedFriend> friends, string hash)
    {
        if (characters.Any(c => c.Hash == hash))
            return (null, RouteError.NotPaired);
        var listing = friends.Where(f => Reachable(f).Contains(hash)).Take(2).ToList();
        return listing.Count switch
        {
            0 => (null, RouteError.NotPaired),
            1 => (listing[0], RouteError.None),
            _ => (null, RouteError.Conflict),
        };
    }

    // Only the logged-in character sends, a tell typed on one character must never go out from an alt.
    public static (FriendRoute? Route, RouteError Error) Route(IReadOnlyList<TellCharacter> characters, IReadOnlyList<PairedFriend> friends, string? currentHash, TellTarget target)
    {
        var character = characters.FirstOrDefault(c => c.Hash == currentHash);
        if (character == null)
            return (null, RouteError.NotPaired);

        var error = RouteError.NotPaired;
        foreach (var to in character.Friends.Where(f => target.Matches(f.Name, f.World)))
        {
            var (friend, ownerError) = Owner(characters, friends, to.Hash);
            if (ownerError == RouteError.Conflict)
                error = RouteError.Conflict;
            else if (friend != null && FriendScopes.Includes(friend.MyScope, character.Hash))
                return (new FriendRoute(character, to, friend), RouteError.None);
        }
        return (null, error);
    }

    // The sending friend, our receiving character and the sender's entry on its friend list, or null when the tell breaks a pairing or scope rule.
    public static (PairedFriend Friend, TellCharacter Character, TellFriend Sender)? Incoming(IReadOnlyList<TellCharacter> characters,
        IReadOnlyList<PairedFriend> friends, string fromInstall, string fromKey, TellBody body)
    {
        var (friend, _) = Owner(characters, friends, body.FromHash);
        if (friend == null || friend.InstallId != fromInstall || friend.PublicKey != fromKey)
            return null;
        var character = characters.FirstOrDefault(c => c.Hash == body.ToHash);
        if (character == null || !FriendScopes.Includes(friend.MyScope, character.Hash))
            return null;
        var sender = character.Friends.FirstOrDefault(f => f.Hash == body.FromHash);
        return sender == null ? null : (friend, character, sender);
    }

    public static List<TellContact> ForDevices(IReadOnlyList<TellCharacter> characters, IReadOnlyList<PairedFriend> friends)
    {
        var contacts = new List<TellContact>();
        foreach (var character in characters)
        {
            foreach (var to in character.Friends)
            {
                if (Owner(characters, friends, to.Hash).Friend is { } friend && FriendScopes.Includes(friend.MyScope, character.Hash))
                    contacts.Add(new TellContact(character.Name, character.World, character.Hash, to.Name, to.World, to.Hash, friend.InstallId, friend.PublicKey));
            }
        }
        return contacts;
    }

    public static bool HasConflict(IReadOnlyList<PairedFriend> friends, PairedFriend friend)
    {
        var reachable = Reachable(friend).ToHashSet();
        return friends.Any(f => f.InstallId != friend.InstallId && Reachable(f).Any(reachable.Contains));
    }

    // Profile is newer or equal: accept and store Characters/ProfileIssuedAt.
    public static bool AcceptProfile(PairedFriend friend, FriendProfile profile)
    {
        if (profile.IssuedAt < friend.ProfileIssuedAt)
            return false;
        friend.Characters = profile.Characters.Where(h => FriendScopes.Includes(friend.TheirScope, h)).Distinct().ToList();
        friend.ProfileIssuedAt = profile.IssuedAt;
        return true;
    }

    // Refuses a bundle older than the newest one accepted, so the relay can't roll back to dropped keys.
    public static bool AcceptBundle(PairedFriend friend, long issuedAt)
    {
        if (issuedAt < friend.BundleIssuedAt)
            return false;
        friend.BundleIssuedAt = issuedAt;
        return true;
    }

    // Our characters this friend may see.
    public static List<string> ProfileCharacters(IReadOnlyList<TellCharacter> characters, PairedFriend friend) =>
        friend.MyScope == FriendScopes.Account
            ? characters.Select(c => c.Hash).Distinct().Take(Limits.MaxTellCharacters).ToList()
            : [friend.MyScope];

    public static (FriendCode? Code, string? Error) ParseRedeem(string code, string myScope)
    {
        if (FriendCode.Parse(code) is not { } parsed)
            return (null, BadCode);
        return FriendScopes.IsValid(myScope) ? (parsed, null) : (null, BadScope);
    }

    // The redeemer's check that the relay passed on the inviter's real key and scope.
    public static bool VerifyInvite(string secret, string inviteKey, string inviteScope, string tag)
    {
        if (!FriendScopes.IsValid(inviteScope))
            return false;
        try
        {
            return FriendProof.Matches(FriendProof.InviteTag(secret, inviteKey, inviteScope), tag);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    // Null when the redeemer may claim the invite. Our own key means our own code, or someone else registered it.
    public static string? InviteError(string secret, string inviteKey, string inviteScope, string tag, string ownKey)
    {
        if (!VerifyInvite(secret, inviteKey, inviteScope, tag))
            return InvalidCode;
        return inviteKey == ownKey ? ClaimError(409, "selfInvite") : null;
    }

    // The inviter's check of a claim. relayClaimKey is the claimant key the relay reports, which must be the sealed one.
    // A claimant with our own key could later pass its tells off as ours.
    public static bool VerifyClaim(FriendClaim? claim, string relayClaimKey, string secret, string ownKey, string inviteScope)
    {
        if (claim == null || claim.InstallPublicKey != relayClaimKey || claim.InstallPublicKey == ownKey || !FriendScopes.IsValid(claim.Scope))
            return false;
        try
        {
            return FriendProof.Matches(FriendProof.ClaimMac(secret, ownKey, inviteScope, claim.InstallPublicKey, claim.Scope), claim.Mac);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    // What to do with one of our invites the relay lists. An invite we have no secret for can never be verified.
    public static InviteAction DecideInvite(bool known, string? secret, string? ownScope, bool claimed, FriendClaim? claim, string? relayClaimKey, string ownKey)
    {
        if (!known)
            return InviteAction.Delete;
        if (!claimed)
            return InviteAction.Wait;
        return secret != null && ownScope != null && relayClaimKey != null && VerifyClaim(claim, relayClaimKey, secret, ownKey, ownScope)
            ? InviteAction.Accept
            : InviteAction.Delete;
    }

    // A relayed tell is a copy of our own only when both our install id and our key sent it.
    public static TellOrigin Origin(string fromInstall, string fromKey, string? ownInstall, string ownKey)
    {
        if (fromKey != ownKey)
            return TellOrigin.Friend;
        return fromInstall == ownInstall ? TellOrigin.Own : TellOrigin.Forged;
    }

    public static string ClaimError(int status, string? code) => (status, code) switch
    {
        (404, _) => InvalidCode,
        (_, "alreadyClaimed") => "This code was already used.",
        (_, "selfInvite") => "That is your own code.",
        (_, "alreadyFriends") => "You are already paired with them.",
        (_, "tooManyFriends") => "One of you already has the maximum number of paired friends.",
        _ => "The relay refused the code.",
    };

    // Applies the relay's friend list: drops friends it no longer lists, promotes pending ones it lists with the key
    // we verified, and drops pending ones older than an invite can live.
    public static FriendSync SyncFriends(IReadOnlyList<PairedFriend> paired, IReadOnlyList<PendingFriend> pending,
        IReadOnlyList<(string InstallId, string PublicKey)> listed, long now)
    {
        var result = paired.Where(f => listed.Any(l => l.InstallId == f.InstallId)).ToList();
        var remaining = new List<PendingFriend>();
        var expired = false;
        var promoted = false;
        foreach (var entry in pending)
        {
            if (listed.Any(l => l.InstallId == entry.InstallId && l.PublicKey == entry.PublicKey))
            {
                result.RemoveAll(f => f.InstallId == entry.InstallId);
                result.Add(new PairedFriend { InstallId = entry.InstallId, PublicKey = entry.PublicKey, TheirScope = entry.TheirScope, MyScope = entry.MyScope });
                promoted = true;
            }
            else if (now - entry.CreatedAt > (long)Limits.FriendInviteTtl.TotalMilliseconds)
                expired = true;
            else
                remaining.Add(entry);
        }
        var unknown = listed.Select(l => l.InstallId).Where(id => result.All(f => f.InstallId != id)).Distinct().ToList();
        return new FriendSync(result, remaining, expired, promoted, unknown);
    }

    public static string MyScopeLabel(IReadOnlyList<TellCharacter> characters, string scope) =>
        scope == FriendScopes.Account
            ? "Account"
            : characters.FirstOrDefault(c => c.Hash == scope) is { } c ? $"{c.Name}@{c.World}" : "One character";

    public static string TheirScopeLabel(IReadOnlyList<TellCharacter> characters, string scope) =>
        scope == FriendScopes.Account
            ? "Account"
            : FriendName(characters, scope) ?? "One character";

    // Names of the friend's reachable characters that are on one of our friend lists.
    public static List<string> CharacterNames(IReadOnlyList<TellCharacter> characters, PairedFriend friend) =>
        Reachable(friend).Select(h => FriendName(characters, h)).OfType<string>().Distinct().ToList();

    private static string? FriendName(IReadOnlyList<TellCharacter> characters, string hash) =>
        characters.SelectMany(c => c.Friends).FirstOrDefault(f => f.Hash == hash) is { } f ? $"{f.Name}@{f.World}" : null;
}
