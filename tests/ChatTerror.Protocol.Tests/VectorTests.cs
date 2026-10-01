using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChatTerror.Protocol.Tests;

public class VectorTests
{
    private static string VectorPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "test-vectors")))
            dir = dir.Parent;
        if (dir == null)
            throw new DirectoryNotFoundException("test-vectors not found");
        return Path.Combine(dir.FullName, "test-vectors", "e2e-v1.json");
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
}
