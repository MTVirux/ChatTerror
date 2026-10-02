using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChatTerror.Protocol.Tests;

public class VectorTests
{
    private static string VectorPath() => VectorFile("e2e-v1.json");

    private static string TellVectorPath() => VectorFile("tell-v1.json");

    private static string VectorFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "test-vectors")))
            dir = dir.Parent;
        if (dir == null)
            throw new DirectoryNotFoundException("test-vectors not found");
        return Path.Combine(dir.FullName, "test-vectors", name);
    }

    private static ECDiffieHellman FromJwk(JsonNode jwk)
    {
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.Decode((string)jwk["d"]!),
            Q = new ECPoint
            {
                X = Base64Url.Decode((string)jwk["x"]!),
                Y = Base64Url.Decode((string)jwk["y"]!),
            },
        };
        return ECDiffieHellman.Create(parameters);
    }

    private static JsonObject ToJwk(ECDiffieHellman key)
    {
        var p = key.ExportParameters(true);
        return new JsonObject
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url.Encode(p.Q.X),
            ["y"] = Base64Url.Encode(p.Q.Y),
            ["d"] = Base64Url.Encode(p.D),
        };
    }

    [Fact]
    public void Vectors_MatchFile()
    {
        var root = JsonNode.Parse(File.ReadAllText(VectorPath()))!;
        using var plugin = FromJwk(root["plugin"]!["jwk"]!);
        using var device = FromJwk(root["device"]!["jwk"]!);
        var pluginPub = Base64Url.Decode((string)root["plugin"]!["publicRaw"]!);
        var devicePub = Base64Url.Decode((string)root["device"]!["publicRaw"]!);
        var expectedKey = Base64Url.Decode((string)root["key"]!);
        var seal = root["seal"]!;
        var nonce = Base64Url.Decode((string)seal["nonce"]!);
        var plaintext = Encoding.UTF8.GetBytes((string)seal["plaintext"]!);
        var envelope = Base64Url.Decode((string)seal["envelope"]!);

        Assert.Equal(pluginPub, P256.PublicRaw(plugin));
        Assert.Equal(devicePub, P256.PublicRaw(device));
        Assert.Equal(expectedKey, E2eCrypto.DeriveKey(plugin, devicePub, pluginPub, devicePub));
        Assert.Equal(expectedKey, E2eCrypto.DeriveKey(device, pluginPub, pluginPub, devicePub));
        Assert.Equal((string)root["fingerprint"]!, Fingerprint.Compute((string)root["secret"]!, pluginPub, devicePub));
        Assert.Equal("p2d", (string)seal["direction"]!);
        Assert.Equal(envelope, E2eCrypto.Seal(expectedKey, Direction.PluginToDevice, plaintext, nonce));
        Assert.Equal(plaintext, E2eCrypto.Open(expectedKey, Direction.PluginToDevice, envelope));
    }

    // Regenerates the vector file with fresh keys. Remove Skip locally to run it once.
    [Fact(Skip = "generator")]
    public void Vectors_Generate()
    {
        using var plugin = P256.Generate();
        using var device = P256.Generate();
        var pluginPub = P256.PublicRaw(plugin);
        var devicePub = P256.PublicRaw(device);
        var key = E2eCrypto.DeriveKey(plugin, devicePub, pluginPub, devicePub);
        var secret = PairingSecret.Generate();
        var nonce = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
        var item = new ChatItem("0123456789abcdef0123456789abcdef", 1700000000000, ChatChannel.FreeCompany, "Y'shtola Rhul", "Twintania", "hello <3", "Alpha Beta", false);
        var plaintext = ProtocolJson.Serialize<Payload>(new ChatPayload(item) { Seq = 1700000000000 });
        var envelope = E2eCrypto.Seal(key, Direction.PluginToDevice, Encoding.UTF8.GetBytes(plaintext), nonce);

        var root = new JsonObject
        {
            ["plugin"] = new JsonObject { ["jwk"] = ToJwk(plugin), ["publicRaw"] = Base64Url.Encode(pluginPub) },
            ["device"] = new JsonObject { ["jwk"] = ToJwk(device), ["publicRaw"] = Base64Url.Encode(devicePub) },
            ["key"] = Base64Url.Encode(key),
            ["secret"] = secret,
            ["fingerprint"] = Fingerprint.Compute(secret, pluginPub, devicePub),
            ["seal"] = new JsonObject
            {
                ["direction"] = "p2d",
                ["nonce"] = Base64Url.Encode(nonce),
                ["plaintext"] = plaintext,
                ["envelope"] = Base64Url.Encode(envelope),
            },
        };
        File.WriteAllText(VectorPath(), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }

    [Fact]
    public void TellVectors_MatchFile()
    {
        var root = JsonNode.Parse(File.ReadAllText(TellVectorPath()))!;
        using var recipient = FromJwk(root["recipient"]!["jwk"]!);
        using var ephemeral = FromJwk(root["ephemeral"]!["jwk"]!);
        var recipientPub = Base64Url.Decode((string)root["recipient"]!["publicRaw"]!);
        var nonce = Base64Url.Decode((string)root["nonce"]!);
        var plaintext = Encoding.UTF8.GetBytes((string)root["plaintext"]!);
        var envelope = Base64Url.Decode((string)root["envelope"]!);
        var signed = new SignedTellBundle((string)root["bundle"]!["bundle"]!, (string)root["bundle"]!["signature"]!);

        Assert.Equal((string)root["hash"]!, TellHash.Compute(0x0102030405060708));
        Assert.Equal(envelope, SealedTell.Seal(ephemeral, recipientPub, plaintext, nonce));
        Assert.Equal(plaintext, SealedTell.Open(recipient, envelope));
        Assert.NotNull(TellBundles.Verify(signed, Base64Url.Encode(recipientPub)));
    }

    // Regenerates the tell vector file with fresh keys. Remove Skip locally to run it once.
    [Fact(Skip = "generator")]
    public void TellVectors_Generate()
    {
        using var recipient = P256.Generate();
        using var ephemeral = P256.Generate();
        var recipientPub = P256.PublicRaw(recipient);
        var key = Base64Url.Encode(recipientPub);
        var nonce = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
        var body = new TellBody("0123456789abcdef0123456789abcdef", TellHash.Compute(1), "Y'shtola Rhul", "Twintania", TellHash.Compute(2), "Alpha Beta", "Lich", "hello <3", 1700000000000);
        var plaintext = ProtocolJson.Serialize(body);
        var envelope = SealedTell.Seal(ephemeral, recipientPub, Encoding.UTF8.GetBytes(plaintext), nonce);
        var signed = TellBundles.Sign(recipient, new TellBundle(key, [new TellBundleEntry(TellTargets.Plugin, key, false), new TellBundleEntry("device1", key, true)], 1700000000000));

        var root = new JsonObject
        {
            ["recipient"] = new JsonObject { ["jwk"] = ToJwk(recipient), ["publicRaw"] = key },
            ["ephemeral"] = new JsonObject { ["jwk"] = ToJwk(ephemeral), ["publicRaw"] = Base64Url.Encode(P256.PublicRaw(ephemeral)) },
            ["hash"] = TellHash.Compute(0x0102030405060708),
            ["nonce"] = Base64Url.Encode(nonce),
            ["plaintext"] = plaintext,
            ["envelope"] = Base64Url.Encode(envelope),
            ["bundle"] = new JsonObject { ["bundle"] = signed.Bundle, ["signature"] = signed.Signature },
        };
        File.WriteAllText(TellVectorPath(), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }
}
