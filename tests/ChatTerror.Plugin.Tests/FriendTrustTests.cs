using System.Security.Cryptography;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class FriendTrustTests
{
    private static readonly string MainHash = TellHash.Compute(1);
    private static readonly string AltHash = TellHash.Compute(2);
    private static readonly string BobHash = TellHash.Compute(10);
    private static readonly string BobAltHash = TellHash.Compute(11);
    private static readonly string CidHash = TellHash.Compute(12);

    private static TellFriend Bob => new() { Hash = BobHash, Name = "Bob Smith", World = "Lich" };
    private static TellFriend BobAlt => new() { Hash = BobAltHash, Name = "Bob Alt", World = "Lich" };
    private static TellFriend Cid => new() { Hash = CidHash, Name = "Cid Garlond", World = "Lich" };

    private static TellCharacter Main => new() { Hash = MainHash, Name = "Main Char", World = "Twintania", Friends = [Bob, BobAlt, Cid] };
    private static TellCharacter Alt => new() { Hash = AltHash, Name = "Alt Char", World = "Lich", Friends = [Bob] };

    private static PairedFriend BobInstall(string theirScope = FriendScopes.Account, string myScope = FriendScopes.Account) => new()
    {
        InstallId = "bob-install", PublicKey = "bob-key", TheirScope = theirScope, MyScope = myScope, Characters = [BobHash, BobAltHash],
    };

    private static PairedFriend CidInstall(params string[] characters) => new()
    {
        InstallId = "cid-install", PublicKey = "cid-key", Characters = [.. characters],
    };

    private static TellBody Body(string from, string to) => new("t1", from, "Forged", "Lich", to, "Me", "Twintania", "hi", 1000);

    [Fact]
    public void Reachable_KeepsOnlyCharactersInTheirScope()
    {
        Assert.Equal([BobHash, BobAltHash], FriendTrust.Reachable(BobInstall()));
        Assert.Equal([BobHash], FriendTrust.Reachable(BobInstall(theirScope: BobHash)));
        Assert.Empty(FriendTrust.Reachable(BobInstall(theirScope: CidHash)));
    }

    [Fact]
    public void Route_AccountScopesReachEveryListedCharacter()
    {
        var (route, error) = FriendTrust.Route([Main, Alt], [BobInstall()], MainHash, new TellTarget("bob alt", "lich"));

        Assert.Equal(RouteError.None, error);
        Assert.Equal(MainHash, route!.From.Hash);
        Assert.Equal(BobAltHash, route.To.Hash);
        Assert.Equal("bob-install", route.Friend.InstallId);
    }

    [Fact]
    public void Route_RespectsTheirCharacterScope()
    {
        var friends = new[] { BobInstall(theirScope: BobHash) };

        Assert.NotNull(FriendTrust.Route([Main], friends, MainHash, new TellTarget("Bob Smith", "Lich")).Route);
        Assert.Equal(RouteError.NotPaired, FriendTrust.Route([Main], friends, MainHash, new TellTarget("Bob Alt", "Lich")).Error);
    }

    [Fact]
    public void Route_RespectsMyCharacterScope()
    {
        var friends = new[] { BobInstall(myScope: AltHash) };
        var target = new TellTarget("Bob Smith", "Lich");

        Assert.Equal(AltHash, FriendTrust.Route([Main, Alt], friends, AltHash, target).Route!.From.Hash);
        Assert.Equal(RouteError.NotPaired, FriendTrust.Route([Main, Alt], friends, MainHash, target).Error);
    }

    [Fact]
    public void Route_NeedsTheTargetOnTheCurrentCharactersFriendList()
    {
        var friends = new[] { BobInstall() };

        // Only the logged-in character sends, never an alt that has the target on its list.
        Assert.Equal(RouteError.NotPaired, FriendTrust.Route([Main, Alt], friends, AltHash, new TellTarget("Bob Alt", "Lich")).Error);
        Assert.Equal(RouteError.NotPaired, FriendTrust.Route([Main], friends, MainHash, new TellTarget("Nobody Here", "Lich")).Error);
        Assert.Equal(RouteError.NotPaired, FriendTrust.Route([Main], friends, null, new TellTarget("Bob Smith", "Lich")).Error);
        Assert.Equal(RouteError.NotPaired, FriendTrust.Route([Main], [], MainHash, new TellTarget("Bob Smith", "Lich")).Error);
    }

    [Fact]
    public void Route_TwoFriendsListingTheSameCharacterIsAConflict()
    {
        var friends = new[] { BobInstall(), CidInstall(BobHash, CidHash) };

        var (route, error) = FriendTrust.Route([Main], friends, MainHash, new TellTarget("Bob Smith", "Lich"));

        Assert.Null(route);
        Assert.Equal(RouteError.Conflict, error);
        Assert.Equal(RouteError.None, FriendTrust.Route([Main], friends, MainHash, new TellTarget("Cid Garlond", "Lich")).Error);
    }

    [Fact]
    public void Route_ConflictCountsFriendsOutsideMyScopeToo()
    {
        var friends = new[] { BobInstall(), CidInstall(BobHash) };
        friends[1].MyScope = AltHash;

        Assert.Equal(RouteError.Conflict, FriendTrust.Route([Main, Alt], friends, MainHash, new TellTarget("Bob Smith", "Lich")).Error);
    }

    [Fact]
    public void Route_AFriendListingOneOfOurCharactersNeverReachesIt()
    {
        var altOnMain = new TellCharacter { Hash = MainHash, Name = "Main Char", World = "Twintania", Friends = [new TellFriend { Hash = AltHash, Name = "Alt Char", World = "Lich" }] };
        var friends = new[] { CidInstall(AltHash) };

        Assert.Equal(RouteError.NotPaired, FriendTrust.Route([altOnMain, Alt], friends, MainHash, new TellTarget("Alt Char", "Lich")).Error);
        Assert.Empty(FriendTrust.ForDevices([altOnMain, Alt], friends));
    }

    [Fact]
    public void HasConflict_FlagsBothFriends()
    {
        var friends = new[] { BobInstall(), CidInstall(BobHash), new PairedFriend { InstallId = "x", Characters = [CidHash] } };

        Assert.True(FriendTrust.HasConflict(friends, friends[0]));
        Assert.True(FriendTrust.HasConflict(friends, friends[1]));
        Assert.False(FriendTrust.HasConflict(friends, friends[2]));
        Assert.False(FriendTrust.HasConflict([BobInstall()], BobInstall()));
    }

    [Fact]
    public void ForDevices_ListsRoutablePairsWithInstallAndKey()
    {
        var contacts = FriendTrust.ForDevices([Main, Alt], [BobInstall(theirScope: BobHash)]);

        Assert.Equal(
            [
                new TellContact("Main Char", "Twintania", MainHash, "Bob Smith", "Lich", BobHash, "bob-install", "bob-key"),
                new TellContact("Alt Char", "Lich", AltHash, "Bob Smith", "Lich", BobHash, "bob-install", "bob-key"),
            ],
            contacts);
    }

    [Fact]
    public void ForDevices_LeavesOutConflictsAndCharactersOutsideMyScope()
    {
        var friends = new[] { BobInstall(myScope: AltHash), CidInstall(BobAltHash) };

        var contacts = FriendTrust.ForDevices([Main, Alt], friends);

        Assert.Equal([(AltHash, BobHash)], contacts.Select(c => (c.CharacterHash, c.Hash)));
    }

    [Fact]
    public void Incoming_AcceptsAPairedFriendWithinBothScopes()
    {
        var result = FriendTrust.Incoming([Main, Alt], [BobInstall()], "bob-install", "bob-key", Body(BobAltHash, MainHash));

        Assert.NotNull(result);
        Assert.Equal("bob-install", result!.Value.Friend.InstallId);
        Assert.Equal(MainHash, result.Value.Character.Hash);
        Assert.Equal("Bob Alt", result.Value.Sender.Name);
    }

    [Fact]
    public void Incoming_RejectsWhenAnyRuleFails()
    {
        IReadOnlyList<TellCharacter> characters = [Main, Alt];
        var friends = new[] { BobInstall() };

        Assert.Null(FriendTrust.Incoming(characters, friends, "bob-install", "other-key", Body(BobHash, MainHash)));
        Assert.Null(FriendTrust.Incoming(characters, friends, "stranger", "bob-key", Body(BobHash, MainHash)));
        Assert.Null(FriendTrust.Incoming(characters, friends, "bob-install", "bob-key", Body(CidHash, MainHash)));
        Assert.Null(FriendTrust.Incoming(characters, friends, "bob-install", "bob-key", Body(BobHash, CidHash)));
        Assert.Null(FriendTrust.Incoming(characters, [BobInstall(theirScope: BobHash)], "bob-install", "bob-key", Body(BobAltHash, MainHash)));
        Assert.Null(FriendTrust.Incoming(characters, [BobInstall(myScope: AltHash)], "bob-install", "bob-key", Body(BobHash, MainHash)));
        // Bob Alt is not on Alt's friend list.
        Assert.Null(FriendTrust.Incoming(characters, friends, "bob-install", "bob-key", Body(BobAltHash, AltHash)));
        Assert.Null(FriendTrust.Incoming(characters, [BobInstall(), CidInstall(BobHash)], "bob-install", "bob-key", Body(BobHash, MainHash)));
    }

    [Fact]
    public void AcceptProfile_KeepsNewerProfilesInsideTheirScope()
    {
        var friend = BobInstall(theirScope: BobHash);
        friend.Characters = [];

        Assert.True(FriendTrust.AcceptProfile(friend, new FriendProfile([BobHash, BobAltHash], 100)));
        Assert.Equal([BobHash], friend.Characters);
        Assert.Equal(100, friend.ProfileIssuedAt);

        Assert.True(FriendTrust.AcceptProfile(friend, new FriendProfile([], 100)));
        Assert.Empty(friend.Characters);

        Assert.False(FriendTrust.AcceptProfile(friend, new FriendProfile([BobHash], 99)));
        Assert.Empty(friend.Characters);
        Assert.Equal(100, friend.ProfileIssuedAt);
    }

    [Fact]
    public void AcceptBundle_RefusesRollbacks()
    {
        var friend = BobInstall();

        Assert.True(FriendTrust.AcceptBundle(friend, 100));
        Assert.True(FriendTrust.AcceptBundle(friend, 100));
        Assert.False(FriendTrust.AcceptBundle(friend, 99));
        Assert.Equal(100, friend.BundleIssuedAt);
    }

    [Fact]
    public void ProfileCharacters_FollowMyScope()
    {
        var friend = BobInstall();
        Assert.Equal([MainHash, AltHash], FriendTrust.ProfileCharacters([Main, Alt], friend));

        friend.MyScope = AltHash;
        Assert.Equal([AltHash], FriendTrust.ProfileCharacters([Main, Alt], friend));
    }

    private static string Key(ECDiffieHellman key) => Base64Url.Encode(P256.PublicRaw(key));

    [Fact]
    public void VerifyInvite_ChecksTheTagAgainstTheSecret()
    {
        using var a = P256.Generate();
        using var other = P256.Generate();
        var secret = FriendCode.NewSecret();
        var tag = FriendProof.InviteTag(secret, Key(a), MainHash);

        Assert.True(FriendTrust.VerifyInvite(secret, Key(a), MainHash, tag));
        Assert.False(FriendTrust.VerifyInvite(secret, Key(other), MainHash, tag));
        Assert.False(FriendTrust.VerifyInvite(secret, Key(a), FriendScopes.Account, tag));
        Assert.False(FriendTrust.VerifyInvite(FriendCode.NewSecret(), Key(a), MainHash, tag));
        Assert.False(FriendTrust.VerifyInvite(secret, "not-a-key", MainHash, tag));
        Assert.False(FriendTrust.VerifyInvite(secret, Key(a), "bad", tag));
    }

    [Fact]
    public void VerifyClaim_AcceptsOnlyAMatchingClaim()
    {
        using var a = P256.Generate();
        using var b = P256.Generate();
        using var swapped = P256.Generate();
        var secret = FriendCode.NewSecret();
        var mac = FriendProof.ClaimMac(secret, Key(a), FriendScopes.Account, Key(b), AltHash);
        var claim = new FriendClaim(Key(b), AltHash, mac);

        Assert.True(FriendTrust.VerifyClaim(claim, Key(b), secret, Key(a), FriendScopes.Account));
        Assert.False(FriendTrust.VerifyClaim(null, Key(b), secret, Key(a), FriendScopes.Account));
        // The relay reports a different claimant than the one sealed inside.
        Assert.False(FriendTrust.VerifyClaim(claim, Key(swapped), secret, Key(a), FriendScopes.Account));
        Assert.False(FriendTrust.VerifyClaim(claim, Key(b), FriendCode.NewSecret(), Key(a), FriendScopes.Account));
        Assert.False(FriendTrust.VerifyClaim(claim, Key(b), secret, Key(a), MainHash));
        Assert.False(FriendTrust.VerifyClaim(claim with { Scope = FriendScopes.Account }, Key(b), secret, Key(a), FriendScopes.Account));
        Assert.False(FriendTrust.VerifyClaim(claim with { Scope = "bad" }, Key(b), secret, Key(a), FriendScopes.Account));
        Assert.False(FriendTrust.VerifyClaim(claim with { Mac = "!!" }, Key(b), secret, Key(a), FriendScopes.Account));
        Assert.False(FriendTrust.VerifyClaim(claim with { InstallPublicKey = "short" }, "short", secret, Key(a), FriendScopes.Account));
    }

    [Fact]
    public void VerifyClaim_RejectsAClaimantWithOurOwnKey()
    {
        using var a = P256.Generate();
        var secret = FriendCode.NewSecret();
        var claim = new FriendClaim(Key(a), AltHash, FriendProof.ClaimMac(secret, Key(a), MainHash, Key(a), AltHash));

        Assert.False(FriendTrust.VerifyClaim(claim, Key(a), secret, Key(a), MainHash));
        Assert.Equal(InviteAction.Delete, FriendTrust.DecideInvite(true, secret, MainHash, true, claim, Key(a), Key(a)));
    }

    [Fact]
    public void InviteError_RefusesBadTagsAndOurOwnKey()
    {
        using var a = P256.Generate();
        using var b = P256.Generate();
        var secret = FriendCode.NewSecret();
        var tag = FriendProof.InviteTag(secret, Key(a), MainHash);

        Assert.Null(FriendTrust.InviteError(secret, Key(a), MainHash, tag, Key(b)));
        Assert.Equal(FriendTrust.InvalidCode, FriendTrust.InviteError(FriendCode.NewSecret(), Key(a), MainHash, tag, Key(b)));
        Assert.Equal("That is your own code.", FriendTrust.InviteError(secret, Key(a), MainHash, tag, Key(a)));
    }

    [Fact]
    public void Origin_OwnCopiesNeedOurInstallIdAndKey()
    {
        Assert.Equal(TellOrigin.Own, FriendTrust.Origin("me", "myKey", "me", "myKey"));
        Assert.Equal(TellOrigin.Forged, FriendTrust.Origin("other", "myKey", "me", "myKey"));
        Assert.Equal(TellOrigin.Forged, FriendTrust.Origin("me", "myKey", null, "myKey"));
        Assert.Equal(TellOrigin.Friend, FriendTrust.Origin("me", "theirKey", "me", "myKey"));
        Assert.Equal(TellOrigin.Friend, FriendTrust.Origin("bob", "bobKey", "me", "myKey"));
    }

    [Fact]
    public void DecideInvite_AcceptsOnlyVerifiedClaimsOnKnownCodes()
    {
        using var a = P256.Generate();
        using var b = P256.Generate();
        using var swapped = P256.Generate();
        var secret = FriendCode.NewSecret();
        var claim = new FriendClaim(Key(b), AltHash, FriendProof.ClaimMac(secret, Key(a), MainHash, Key(b), AltHash));

        Assert.Equal(InviteAction.Accept, FriendTrust.DecideInvite(true, secret, MainHash, true, claim, Key(b), Key(a)));
        Assert.Equal(InviteAction.Wait, FriendTrust.DecideInvite(true, secret, MainHash, false, null, null, Key(a)));
        Assert.Equal(InviteAction.Delete, FriendTrust.DecideInvite(false, null, null, false, null, null, Key(a)));
        Assert.Equal(InviteAction.Delete, FriendTrust.DecideInvite(false, null, null, true, claim, Key(b), Key(a)));
        Assert.Equal(InviteAction.Delete, FriendTrust.DecideInvite(true, null, MainHash, true, claim, Key(b), Key(a)));
        Assert.Equal(InviteAction.Delete, FriendTrust.DecideInvite(true, secret, MainHash, true, null, Key(b), Key(a)));
        Assert.Equal(InviteAction.Delete, FriendTrust.DecideInvite(true, secret, MainHash, true, claim, Key(swapped), Key(a)));
        Assert.Equal(InviteAction.Delete, FriendTrust.DecideInvite(true, secret, FriendScopes.Account, true, claim, Key(b), Key(a)));
    }

    [Fact]
    public void Migration_DeletesTheBundleOnlyForOldConfigsWithTellsOff()
    {
        Assert.True(ConfigMigration.NeedsBundleDelete(2, tellsEnabled: false));
        Assert.False(ConfigMigration.NeedsBundleDelete(2, tellsEnabled: true));
        Assert.False(ConfigMigration.NeedsBundleDelete(ConfigMigration.CurrentVersion, tellsEnabled: false));
    }

    [Fact]
    public void ParseRedeem_ReportsBadInputInsteadOfThrowing()
    {
        Assert.Equal("Enter the full 24 character code.", FriendTrust.ParseRedeem("ABCD-EFGH", FriendScopes.Account).Error);
        Assert.Equal("Enter the full 24 character code.", FriendTrust.ParseRedeem("", FriendScopes.Account).Error);
        Assert.Equal("Pick which of your characters to share.", FriendTrust.ParseRedeem("0000-0000-0000-0000-0000-0000", "bad").Error);

        var (code, error) = FriendTrust.ParseRedeem("abcd efgh 0000 0000 0000 000o", MainHash);
        Assert.Null(error);
        Assert.Equal(new FriendCode("ABCD-EFGH", "0000000000000000"), code);
    }

    [Fact]
    public void ClaimError_ExplainsRelayRefusals()
    {
        Assert.Equal("Invalid or expired code.", FriendTrust.ClaimError(404, null));
        Assert.Equal("This code was already used.", FriendTrust.ClaimError(409, "alreadyClaimed"));
        Assert.Equal("That is your own code.", FriendTrust.ClaimError(409, "selfInvite"));
        Assert.Equal("You are already paired with them.", FriendTrust.ClaimError(409, "alreadyFriends"));
        Assert.Equal("One of you already has the maximum number of paired friends.", FriendTrust.ClaimError(409, "tooManyFriends"));
        Assert.Equal("The relay refused the code.", FriendTrust.ClaimError(400, "invalidEnvelope"));
    }

    [Fact]
    public void SyncFriends_PromotesPendingOnlyWithThePairedKey()
    {
        var pending = new List<PendingFriend>
        {
            new() { InviteId = "i1", InstallId = "bob-install", PublicKey = "bob-key", TheirScope = BobHash, MyScope = AltHash, CreatedAt = 1000 },
            new() { InviteId = "i2", InstallId = "cid-install", PublicKey = "cid-key", TheirScope = "*", MyScope = "*", CreatedAt = 1000 },
        };

        var result = FriendTrust.SyncFriends([], pending, [("bob-install", "bob-key"), ("cid-install", "swapped-key")], now: 2000);

        var bob = Assert.Single(result.Paired);
        Assert.Equal(("bob-install", "bob-key", BobHash, AltHash), (bob.InstallId, bob.PublicKey, bob.TheirScope, bob.MyScope));
        Assert.Equal(["i2"], result.Pending.Select(p => p.InviteId));
        Assert.False(result.PendingExpired);
        Assert.True(result.Promoted);
        // Cid's pending entry is still waiting, its listed key just doesn't match yet.
        Assert.Equal(["cid-install"], result.Unknown);
    }

    [Fact]
    public void SyncFriends_DropsUnlistedFriendsAndOldPendingOnes()
    {
        var day = (long)Limits.FriendInviteTtl.TotalMilliseconds;
        var paired = new List<PairedFriend> { BobInstall(), CidInstall(CidHash) };
        var pending = new List<PendingFriend> { new() { InviteId = "i1", InstallId = "x", PublicKey = "k", CreatedAt = 1000 } };

        var result = FriendTrust.SyncFriends(paired, pending, [("bob-install", "bob-key"), ("unknown", "k")], now: 1000 + day);
        Assert.Equal(["bob-install"], result.Paired.Select(f => f.InstallId));
        Assert.Same(paired[0], result.Paired[0]);
        Assert.Single(result.Pending);
        Assert.False(result.Promoted);
        Assert.Equal(["unknown"], result.Unknown);

        var expired = FriendTrust.SyncFriends(paired, pending, [], now: 1001 + day);
        Assert.Empty(expired.Pending);
        Assert.True(expired.PendingExpired);
    }

    [Fact]
    public void ScopeLabels_NameCharactersWhenKnown()
    {
        Assert.Equal("Account", FriendTrust.MyScopeLabel([Main], FriendScopes.Account));
        Assert.Equal("Alt Char@Lich", FriendTrust.MyScopeLabel([Main, Alt], AltHash));
        Assert.Equal("One character", FriendTrust.TheirScopeLabel([Main], TellHash.Compute(99)));
        Assert.Equal("Bob Alt@Lich", FriendTrust.TheirScopeLabel([Main, Alt], BobAltHash));
        Assert.Equal(["Bob Smith@Lich", "Bob Alt@Lich"], FriendTrust.CharacterNames([Main, Alt], BobInstall()));
    }
}
