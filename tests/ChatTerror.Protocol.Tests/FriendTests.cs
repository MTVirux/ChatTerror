using System.Security.Cryptography;
using System.Text;

namespace ChatTerror.Protocol.Tests;

public class FriendTests
{
    private static string Key(ECDiffieHellman key) => Base64Url.Encode(P256.PublicRaw(key));

    [Fact]
    public void Code_FormatsAndParsesLeniently()
    {
        var code = new FriendCode("ABCD-EFGH-JKMN", FriendCode.NewSecret());
        var formatted = code.Format();
        Assert.Matches("^([0-9A-Z]{4}-){6}[0-9A-Z]{4}$", formatted);
        Assert.Equal(code, FriendCode.Parse(formatted));
        Assert.Equal(code, FriendCode.Parse(formatted.ToLowerInvariant().Replace("-", " ")));
        Assert.Equal(FriendCode.Parse("0000-0000-0000-0000-0000-0000-0000"), FriendCode.Parse("oooo-OOOO-0000-0000-0000-0000-0000"));
        Assert.Null(FriendCode.Parse("ABCD-EFGH"));
        Assert.Null(FriendCode.Parse("ABCD-EFGH-ABCD-EFGH-ABCD-EFGH-ABCU"));
        Assert.Null(FriendCode.Parse("ABCD-EFGH-0000-0000-0000-0000"));
        Assert.Null(FriendCode.Parse(""));
    }

    [Fact]
    public void Scopes()
    {
        var hash = TellHash.Compute(7);
        Assert.True(FriendScopes.IsValid("*"));
        Assert.True(FriendScopes.IsValid(hash));
        Assert.False(FriendScopes.IsValid("x"));
        Assert.False(FriendScopes.IsValid(null));
        Assert.True(FriendScopes.Includes("*", hash));
        Assert.True(FriendScopes.Includes(hash, hash));
        Assert.False(FriendScopes.Includes(TellHash.Compute(8), hash));
    }

    [Fact]
    public void Proofs_BindEveryInput()
    {
        using var a = P256.Generate();
        using var b = P256.Generate();
        var secret = FriendCode.NewSecret();
        var other = FriendCode.NewSecret();
        var hash = TellHash.Compute(1);

        var tag = FriendProof.InviteTag(secret, Key(a), "*");
        Assert.True(FriendProof.Matches(tag, FriendProof.InviteTag(secret, Key(a), "*")));
        Assert.False(FriendProof.Matches(tag, FriendProof.InviteTag(other, Key(a), "*")));
        Assert.False(FriendProof.Matches(tag, FriendProof.InviteTag(secret, Key(b), "*")));
        Assert.False(FriendProof.Matches(tag, FriendProof.InviteTag(secret, Key(a), hash)));

        var mac = FriendProof.ClaimMac(secret, Key(a), "*", Key(b), hash);
        Assert.False(FriendProof.Matches(mac, FriendProof.ClaimMac(secret, Key(a), "*", Key(b), "*")));
        Assert.False(FriendProof.Matches(mac, FriendProof.ClaimMac(secret, Key(a), hash, Key(b), hash)));
        Assert.False(FriendProof.Matches(mac, FriendProof.ClaimMac(secret, Key(b), "*", Key(a), hash)));
        Assert.False(FriendProof.Matches(mac, "not-base64!"));
    }

    [Fact]
    public void Claim_RoundTripsOnlyForInviter()
    {
        using var a = P256.Generate();
        using var b = P256.Generate();
        var claim = new FriendClaim(Key(b), "*", FriendProof.ClaimMac(FriendCode.NewSecret(), Key(a), "*", Key(b), "*"));
        var sealedClaim = FriendClaims.Seal(Key(a), claim);
        Assert.Equal(claim, FriendClaims.Open(a, sealedClaim));
        Assert.Null(FriendClaims.Open(b, sealedClaim));
        Assert.Null(FriendClaims.Open(a, "garbage"));
    }

    [Fact]
    public void Profile_ChecksSignerRecipientAndContent()
    {
        using var a = P256.Generate();
        using var b = P256.Generate();
        using var c = P256.Generate();
        var profile = new FriendProfile([TellHash.Compute(1), TellHash.Compute(2)], 1700000000000);
        var envelope = FriendProfiles.Seal(a, Key(b), profile);

        var opened = FriendProfiles.Open(b, envelope, Key(a));
        Assert.NotNull(opened);
        Assert.Equal(profile.Characters, opened!.Characters);
        Assert.Equal(profile.IssuedAt, opened.IssuedAt);
        Assert.Null(FriendProfiles.Open(b, envelope, Key(c)));
        Assert.Null(FriendProfiles.Open(c, envelope, Key(a)));
        Assert.Null(FriendProfiles.Open(b, FriendProfiles.Seal(a, Key(b), new FriendProfile(["bad"], 1)), Key(a)));
    }

    [Fact]
    public void SealedBox_SeparatesPurposes()
    {
        using var a = P256.Generate();
        var claim = FriendClaims.Seal(Key(a), new FriendClaim(Key(a), "*", "mac"));
        Assert.ThrowsAny<CryptographicException>(() => SealedTell.OpenBody(a, claim));
        var tell = SealedTell.Seal(P256.PublicRaw(a), Encoding.UTF8.GetBytes("{}"));
        Assert.Null(FriendClaims.Open(a, Base64Url.Encode(tell)));
    }
}
