using ChatTerror.Plugin.Services;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "DPAPI is only available on Windows.";
    }
}

public class SecretStorageTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "chatterror-tests-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] der;

    public SecretStorageTests()
    {
        Directory.CreateDirectory(dir);
        using var key = P256.Generate();
        der = P256.ExportPrivate(key);
    }

    public void Dispose() => Directory.Delete(dir, true);

    private string PlainPath => Path.Combine(dir, IdentityKeyFile.PlainName);

    private string ProtectedPath => Path.Combine(dir, IdentityKeyFile.ProtectedName);

    [WindowsFact]
    public void ProtectRoundTrips()
    {
        var sealedData = Secrets.Protect(der)!;

        Assert.NotEqual(der, sealedData);
        Assert.Equal(der, Secrets.Unprotect(sealedData));
    }

    [WindowsFact]
    public void ProtectStringRoundTrips()
    {
        var sealedToken = Secrets.ProtectString("i.abc.secret")!;

        Assert.DoesNotContain("secret", sealedToken);
        Assert.Equal("i.abc.secret", Secrets.UnprotectString(sealedToken));
    }

    [Fact]
    public void UnprotectReturnsNullForGarbage()
    {
        Assert.Null(Secrets.Unprotect([1, 2, 3]));
        Assert.Null(Secrets.UnprotectString("AQID"));
        Assert.Null(Secrets.UnprotectString("not base64!"));
    }

    [WindowsFact]
    public void SaveWritesOnlyTheEncryptedFile()
    {
        Assert.True(IdentityKeyFile.Save(dir, der));

        Assert.False(File.Exists(PlainPath));
        Assert.NotEqual(der, File.ReadAllBytes(ProtectedPath));
        Assert.Equal(der, IdentityKeyFile.Read(ProtectedPath, true));
    }

    [WindowsFact]
    public void SaveMigratesThePlaintextKey()
    {
        File.WriteAllBytes(PlainPath, der);

        Assert.Equal(der, IdentityKeyFile.Read(PlainPath, false));
        Assert.True(IdentityKeyFile.Save(dir, IdentityKeyFile.Read(PlainPath, false)!));

        Assert.False(File.Exists(PlainPath));
        Assert.Equal(der, IdentityKeyFile.Read(ProtectedPath, true));
    }

    [WindowsFact]
    public void ReadReturnsNullForATamperedFile()
    {
        IdentityKeyFile.Save(dir, der);
        var data = File.ReadAllBytes(ProtectedPath);
        data[^1] ^= 0xFF;
        File.WriteAllBytes(ProtectedPath, data);

        Assert.Null(IdentityKeyFile.Read(ProtectedPath, true));
    }

    [Fact]
    public void MoveToBackupKeepsOnlyTheNewestBackup()
    {
        File.WriteAllBytes(PlainPath + ".bad-1", [1]);
        File.WriteAllBytes(ProtectedPath + ".bad-2", [2]);
        File.WriteAllBytes(ProtectedPath, [3]);

        var backup = IdentityKeyFile.MoveToBackup(ProtectedPath);

        Assert.False(File.Exists(ProtectedPath));
        Assert.Equal([backup], Directory.GetFiles(dir));
        Assert.Equal([3], File.ReadAllBytes(backup));
    }

    [Fact]
    public void PlaintextKeyIsNotMistakenForEncrypted()
    {
        File.WriteAllBytes(ProtectedPath, der);

        Assert.Null(IdentityKeyFile.Read(ProtectedPath, true));
    }
}
