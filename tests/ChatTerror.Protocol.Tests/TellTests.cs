using System.Security.Cryptography;
using System.Text;

namespace ChatTerror.Protocol.Tests;

public class TellTests
{
    private static TellBody Body(string text = "hi") =>
        new("0123456789abcdef0123456789abcdef", TellHash.Compute(1), "Alpha Beta", "Twintania", TellHash.Compute(2), "Gamma Delta", "Lich", text, 1700000000000);

    [Fact]
    public void Hash_IsStableAndUrlSafe()
    {
        var hash = TellHash.Compute(0x0102030405060708);
        Assert.Equal(hash, TellHash.Compute(0x0102030405060708));
        Assert.NotEqual(hash, TellHash.Compute(0x0102030405060709));
        Assert.Equal(43, hash.Length);
        Assert.True(TellHash.IsValid(hash));
        Assert.False(TellHash.IsValid("abc"));
        Assert.False(TellHash.IsValid(null));
    }

    [Fact]
    public void SealedTell_RoundTrips()
    {
        using var recipient = P256.Generate();
        var envelope = SealedTell.SealBody(P256.PublicRaw(recipient), Body());
        Assert.Equal(Body(), SealedTell.OpenBody(recipient, envelope));
    }

    [Fact]
    public void SealedTell_RejectsTamperingAndWrongKey()
    {
        using var recipient = P256.Generate();
        using var other = P256.Generate();
        var envelope = SealedTell.Seal(P256.PublicRaw(recipient), Encoding.UTF8.GetBytes("hello"));

        var tampered = (byte[])envelope.Clone();
        tampered[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => SealedTell.Open(recipient, tampered));
        Assert.ThrowsAny<CryptographicException>(() => SealedTell.Open(other, envelope));
        Assert.ThrowsAny<CryptographicException>(() => SealedTell.Open(recipient, envelope[..20]));
    }

    [Fact]
    public void Bundle_VerifiesOnlyWithItsOwnKey()
    {
        using var identity = P256.Generate();
        using var other = P256.Generate();
        var key = Base64Url.Encode(P256.PublicRaw(identity));
        var bundle = new TellBundle(key, [new TellBundleEntry(TellTargets.Plugin, key, false)], 1);

        var signed = TellBundles.Sign(identity, bundle);

        Assert.Equal(bundle.Entries, TellBundles.Verify(signed, key)!.Entries);
        Assert.NotNull(TellBundles.Verify(signed, null));
        Assert.Null(TellBundles.Verify(signed, Base64Url.Encode(P256.PublicRaw(other))));
        Assert.Null(TellBundles.Verify(signed with { Signature = TellBundles.Sign(other, bundle).Signature }, null));
        Assert.Null(TellBundles.Verify(signed with { Bundle = signed.Bundle[..^2] + "AA" }, null));
        Assert.Null(TellBundles.Verify(new SignedTellBundle("!!", "!!"), null));
    }

    [Fact]
    public void Frames_UseTheirDiscriminators()
    {
        var json = ProtocolJson.Serialize<RelayFrame>(new TellSendFrame("id", "b", [new TellCopy(true, "plugin", "env")]));
        Assert.Contains("\"t\":\"tellSend\"", json);
        Assert.Equal("{\"t\":\"tellAck\",\"ids\":[\"x\"]}", ProtocolJson.Serialize<RelayFrame>(new TellAckFrame(["x"])));
        Assert.Equal("{\"t\":\"friendsChanged\"}", ProtocolJson.Serialize<RelayFrame>(new FriendsChangedFrame()));
        Assert.IsType<TellResultFrame>(ProtocolJson.Deserialize<RelayFrame>("{\"t\":\"tellResult\",\"id\":\"x\",\"ok\":true}"));
        Assert.IsType<TellKeyPayload>(ProtocolJson.Deserialize<Payload>("{\"type\":\"tellKey\",\"seq\":1,\"publicKey\":\"k\"}"));
    }

    [Fact]
    public void Settings_ContactsAreOptional()
    {
        var settings = ProtocolJson.Deserialize<Payload>("{\"type\":\"settings\",\"seq\":1,\"relayChannels\":[],\"sendChannels\":[],\"maxLength\":500}");
        Assert.Null(Assert.IsType<SettingsPayload>(settings).Contacts);
    }
}
