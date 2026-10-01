using System;
using System.IO;
using System.Security.Cryptography;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Services;

public sealed class KeyStore : IDisposable
{
    public KeyStore(string configDirectory)
    {
        var path = Path.Combine(configDirectory, "identity.key");
        if (File.Exists(path))
        {
            Key = P256.ImportPrivate(File.ReadAllBytes(path));
        }
        else
        {
            Directory.CreateDirectory(configDirectory);
            Key = P256.Generate();
            File.WriteAllBytes(path, P256.ExportPrivate(Key));
        }

        PublicRaw = P256.PublicRaw(Key);
        PublicKey = Base64Url.Encode(PublicRaw);
    }

    public ECDiffieHellman Key { get; }

    public byte[] PublicRaw { get; }

    public string PublicKey { get; }

    public void Dispose() => Key.Dispose();
}
