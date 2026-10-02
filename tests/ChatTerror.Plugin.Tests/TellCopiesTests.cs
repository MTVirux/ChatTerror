using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class TellCopiesTests
{
    [Fact]
    public void Build_SealsForEveryRecipientKeyAndOwnKeysButTheSender()
    {
        using var recipientKey = P256.Generate();
        using var ownKey = P256.Generate();
        using var phoneKey = P256.Generate();
        var r = Base64Url.Encode(P256.PublicRaw(recipientKey));
        var o = Base64Url.Encode(P256.PublicRaw(ownKey));
        var p = Base64Url.Encode(P256.PublicRaw(phoneKey));
        var recipient = new TellBundle(r, [new TellBundleEntry(TellTargets.Plugin, r, false)], 1);
        var own = new TellBundle(o, [new TellBundleEntry(TellTargets.Plugin, o, false), new TellBundleEntry("phone", p, true)], 1);
        var body = new TellBody("id", TellHash.Compute(1), "A B", "Lich", TellHash.Compute(2), "C D", "Lich", "hi", 1);

        var copies = TellCopies.Build(body, recipient, own, TellTargets.Plugin);

        Assert.Equal([(false, TellTargets.Plugin), (true, "phone")], copies.Select(c => (c.Self, c.Target)));
        Assert.Equal(body, SealedTell.OpenBody(recipientKey, copies[0].Envelope));
        Assert.Equal(body, SealedTell.OpenBody(phoneKey, copies[1].Envelope));
    }

    [Fact]
    public void Build_SkipsInvalidKeys()
    {
        var recipient = new TellBundle("x", [new TellBundleEntry(TellTargets.Plugin, "not-a-key", false)], 1);
        var body = new TellBody("id", "a", "A B", "Lich", "b", "C D", "Lich", "hi", 1);

        Assert.Empty(TellCopies.Build(body, recipient, null, TellTargets.Plugin));
    }
}
