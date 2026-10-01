using System.Security.Cryptography;
using System.Text.Json;
using ChatTerror.Protocol;
using Microsoft.Extensions.Options;

namespace ChatTerror.Server.Push;

public sealed class VapidKeys
{
    public VapidKeys(IOptions<RelayOptions> options, ILogger<VapidKeys> log)
    {
        var relay = options.Value;
        Subject = relay.VapidSubject;
        if (relay.VapidPublicKey != "" && relay.VapidPrivateKey != "")
        {
            PublicKey = relay.VapidPublicKey;
            PrivateKey = relay.VapidPrivateKey;
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(relay.DbPath))!;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "vapid.json");
        if (File.Exists(path))
        {
            var stored = JsonSerializer.Deserialize<StoredKeys>(File.ReadAllText(path), JsonSerializerOptions.Web)!;
            PublicKey = stored.PublicKey;
            PrivateKey = stored.PrivateKey;
            return;
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        PublicKey = Base64Url.Encode([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]);
        PrivateKey = Base64Url.Encode(parameters.D!);
        var fileOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var writer = new StreamWriter(path, fileOptions))
            writer.Write(JsonSerializer.Serialize(new StoredKeys(PublicKey, PrivateKey), JsonSerializerOptions.Web));
        log.LogWarning("No VAPID keys configured, generated a new pair and saved it to {Path}", path);
    }

    public string PublicKey { get; }

    public string PrivateKey { get; }

    public string Subject { get; }

    private sealed record StoredKeys(string PublicKey, string PrivateKey);
}
