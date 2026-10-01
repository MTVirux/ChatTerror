using System.Security.Cryptography;
using ChatTerror.Protocol;
using ChatTerror.Server.Data;

namespace ChatTerror.Server.Api;

public sealed record ErrorBody(string Error);

public static class AuthHelpers
{
    public static string BearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : "";
    }

    public static InstallRecord? Install(HttpContext context, RelayStore store) => store.FindInstallByToken(BearerToken(context));

    public static DeviceRecord? Device(HttpContext context, RelayStore store) => store.FindDeviceByToken(BearerToken(context));

    public static IResult Error(int status, string code) => Results.Json(new ErrorBody(code), statusCode: status);

    public static IResult Unauthorized() => Error(StatusCodes.Status401Unauthorized, "unauthorized");

    public static IResult NotFound() => Error(StatusCodes.Status404NotFound, "notFound");

    public static bool IsPublicKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        try
        {
            using var _ = P256.ImportPublicRaw(Base64Url.Decode(key));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
