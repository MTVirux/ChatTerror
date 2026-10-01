using System;
using System.IO;
using System.Linq;

namespace ChatTerror.Plugin.Services;

// identity.key is the plaintext DER from older versions (or when DPAPI is unavailable), identity.key.dpapi the encrypted one.
public static class IdentityKeyFile
{
    public const string PlainName = "identity.key";
    public const string ProtectedName = "identity.key.dpapi";

    public static byte[]? Read(string path, bool encrypted)
    {
        var data = File.ReadAllBytes(path);
        return encrypted ? Secrets.Unprotect(data) : data;
    }

    // The plaintext file is removed only once the encrypted copy has been read back intact. Returns false when the key
    // could only be stored as plaintext.
    public static bool Save(string directory, byte[] der)
    {
        Directory.CreateDirectory(directory);
        var plain = Path.Combine(directory, PlainName);
        var encrypted = Path.Combine(directory, ProtectedName);
        if (Secrets.Protect(der) is { } data && WriteVerified(encrypted, data, der))
        {
            try
            {
                File.Delete(plain);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            return true;
        }

        Write(plain, der);
        return false;
    }

    // Keeps only the newest backup, older ones belong to keys that were already replaced.
    public static string MoveToBackup(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var backup = $"{path}.bad-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        File.Move(path, backup, true);
        foreach (var old in Directory.GetFiles(directory, PlainName + "*.bad-*").Where(f => f != backup))
            File.Delete(old);
        return backup;
    }

    private static bool WriteVerified(string path, byte[] data, byte[] expected)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        if (Secrets.Unprotect(File.ReadAllBytes(temp)) is not { } roundTrip || !roundTrip.AsSpan().SequenceEqual(expected))
        {
            File.Delete(temp);
            return false;
        }

        File.Move(temp, path, true);
        return true;
    }

    private static void Write(string path, byte[] data)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, true);
    }
}
