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
        // A plaintext key next to an encrypted one means a migration was interrupted or an older version ran since,
        // either way it is the current key.
        var plain = Path.Combine(configDirectory, IdentityKeyFile.PlainName);
        var encrypted = Path.Combine(configDirectory, IdentityKeyFile.ProtectedName);
        var key = Load(plain, false, log) ?? Load(encrypted, true, log);
        if (key == null)
        {
            key = P256.Generate();
            Save(configDirectory, key, log);
            if (Replaced)
                log.Error("Generated a new identity key, paired devices have to be paired again.");
        }
        else
        {
            Replaced = false;
            if (File.Exists(plain))
                Save(configDirectory, key, log);
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

    private ECDiffieHellman? Load(string path, bool encrypted, IPluginLog log)
    {
        if (!File.Exists(path))
            return null;

        var name = Path.GetFileName(path);
        ECDiffieHellman? key = null;
        try
        {
            var der = IdentityKeyFile.Read(path, encrypted)
                ?? throw new CryptographicException("It could not be decrypted, it may belong to another Windows user or machine.");
            key = P256.ImportPrivate(der);
            if (key.KeySize != 256)
                throw new CryptographicException("Not a P-256 key.");
            return key;
        }
        catch (CryptographicException ex)
        {
            key?.Dispose();
            try
            {
                var backup = IdentityKeyFile.MoveToBackup(path);
                log.Error(ex, $"{name} is unreadable, moved it to {Path.GetFileName(backup)}.");
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                log.Error(ex, $"{name} is unreadable and could not be backed up ({moveError.Message}).");
            }
            Replaced = true;
            return null;
        }
    }

    private static void Save(string configDirectory, ECDiffieHellman key, IPluginLog log)
    {
        if (!IdentityKeyFile.Save(configDirectory, P256.ExportPrivate(key)))
            log.Warning("Windows data protection is unavailable, the identity key is stored unencrypted.");
    }
}
