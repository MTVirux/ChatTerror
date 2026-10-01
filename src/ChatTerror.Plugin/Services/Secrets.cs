using System;
using System.Security.Cryptography;
using System.Text;

namespace ChatTerror.Plugin.Services;

// DPAPI ties secrets to the current Windows user, Wine implements it as well.
// Every method returns null instead of throwing when DPAPI is unavailable or the data can't be decrypted.
public static class Secrets
{
    private static readonly byte[] Entropy = "ChatTerror"u8.ToArray();

    public static byte[]? Protect(byte[] data)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or CryptographicException)
        {
            return null;
        }
    }

    public static byte[]? Unprotect(byte[] data)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or CryptographicException)
        {
            return null;
        }
    }

    public static string? ProtectString(string value) =>
        Protect(Encoding.UTF8.GetBytes(value)) is { } data ? Convert.ToBase64String(data) : null;

    public static string? UnprotectString(string value)
    {
        try
        {
            return Unprotect(Convert.FromBase64String(value)) is { } data ? Encoding.UTF8.GetString(data) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
