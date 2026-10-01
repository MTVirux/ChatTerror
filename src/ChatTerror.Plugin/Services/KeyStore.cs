using System;
using System.IO;
using System.Security.Cryptography;
using ChatTerror.Protocol;
using Dalamud.Plugin.Services;

namespace ChatTerror.Plugin.Services;

public sealed class KeyStore : IDisposable
{
    public KeyStore(string configDirectory, IPluginLog log)
    {
        var path = Path.Combine(configDirectory, "identity.key");
        var key = File.Exists(path) ? Load(path, log) : null;
        if (key == null)
        {
            Directory.CreateDirectory(configDirectory);
            key = P256.Generate();
            Write(path, P256.ExportPrivate(key));
        }

        Key = key;
        PublicRaw = P256.PublicRaw(Key);
        PublicKey = Base64Url.Encode(PublicRaw);
    }

    public ECDiffieHellman Key { get; }

    public byte[] PublicRaw { get; }

    public string PublicKey { get; }

    // True when an unreadable key file was replaced, which invalidates the install and every paired device.
    public bool Replaced { get; private set; }

    public void Dispose() => Key.Dispose();

    private ECDiffieHellman? Load(string path, IPluginLog log)
    {
        ECDiffieHellman? key = null;
        try
        {
            key = P256.ImportPrivate(File.ReadAllBytes(path));
            if (key.KeySize != 256)
                throw new CryptographicException("Not a P-256 key.");
            return key;
        }
        catch (CryptographicException ex)
        {
            key?.Dispose();
            var backup = $"{path}.bad-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            log.Error(ex, $"identity.key is unreadable, moved it to {Path.GetFileName(backup)} and generated a new key.");
            File.Move(path, backup, true);
            Replaced = true;
            return null;
        }
    }

    private static void Write(string path, byte[] data)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, true);
    }
}
