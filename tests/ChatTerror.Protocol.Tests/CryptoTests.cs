using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatTerror.Protocol.Tests;

public class CryptoTests
{
    [Fact]
    public void Base64Url_RoundTrips_NoPadding()
    {
        var text = Base64Url.Encode(new byte[] { 0xfb, 0xff });

        Assert.Equal("-_8", text);
        Assert.Equal(new byte[] { 0xfb, 0xff }, Base64Url.Decode(text));
    }

    [Fact]
    public void DeriveKey_BothSidesMatch()
    {
        using var plugin = P256.Generate();
        using var device = P256.Generate();
        var pluginPub = P256.PublicRaw(plugin);
        var devicePub = P256.PublicRaw(device);

        var pluginKey = E2eCrypto.DeriveKey(plugin, devicePub, pluginPub, devicePub);
        var deviceKey = E2eCrypto.DeriveKey(device, pluginPub, pluginPub, devicePub);

        Assert.Equal(65, pluginPub.Length);
        Assert.Equal(0x04, pluginPub[0]);
        Assert.Equal(32, pluginKey.Length);
        Assert.Equal(pluginKey, deviceKey);
    }

    [Fact]
    public void PrivateKey_ExportImport_KeepsPublicKey()
    {
        using var key = P256.Generate();
        using var imported = P256.ImportPrivate(P256.ExportPrivate(key));

        Assert.Equal(P256.PublicRaw(key), P256.PublicRaw(imported));
    }

    [Fact]
    public void SealOpen_RoundTrip()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("hello");

        var envelope = E2eCrypto.Seal(key, Direction.PluginToDevice, plaintext);

        Assert.Equal(1, envelope[0]);
        Assert.Equal(1 + 12 + plaintext.Length + 16, envelope.Length);
        Assert.Equal(plaintext, E2eCrypto.Open(key, Direction.PluginToDevice, envelope));
    }

    [Fact]
    public void Open_WrongDirection_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var envelope = E2eCrypto.Seal(key, Direction.PluginToDevice, "hi"u8.ToArray());

        Assert.ThrowsAny<CryptographicException>(() => E2eCrypto.Open(key, Direction.DeviceToPlugin, envelope));
    }

    [Fact]
    public void Open_TamperedByte_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var envelope = E2eCrypto.Seal(key, Direction.DeviceToPlugin, "hi"u8.ToArray());
        envelope[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => E2eCrypto.Open(key, Direction.DeviceToPlugin, envelope));
    }

    [Fact]
    public void Open_WrongVersion_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var envelope = E2eCrypto.Seal(key, Direction.DeviceToPlugin, "hi"u8.ToArray());
        envelope[0] = 2;

        Assert.ThrowsAny<CryptographicException>(() => E2eCrypto.Open(key, Direction.DeviceToPlugin, envelope));
    }

    [Fact]
    public void Open_TooShort_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Assert.ThrowsAny<CryptographicException>(() => E2eCrypto.Open(key, Direction.DeviceToPlugin, new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void ImportPublicRaw_Garbage_Throws()
    {
        using var key = P256.Generate();
        var valid = P256.PublicRaw(key);

        var wrongPrefix = (byte[])valid.Clone();
        wrongPrefix[0] = 0x02;
        var offCurve = (byte[])valid.Clone();
        offCurve[64] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => P256.ImportPublicRaw(valid[..64]));
        Assert.ThrowsAny<CryptographicException>(() => P256.ImportPublicRaw(wrongPrefix));
        Assert.ThrowsAny<CryptographicException>(() => P256.ImportPublicRaw(offCurve));
    }

    [Fact]
    public void Fingerprint_Format()
    {
        using var a = P256.Generate();
        using var b = P256.Generate();
        var pubA = P256.PublicRaw(a);
        var pubB = P256.PublicRaw(b);

        var forward = Fingerprint.Compute(pubA, pubB);
        var reverse = Fingerprint.Compute(pubB, pubA);

        Assert.Matches(new Regex(@"^\d{3} \d{3}$"), forward);
        Assert.Matches(new Regex(@"^\d{3} \d{3}$"), reverse);
        Assert.NotEqual(forward, reverse);
    }
}
