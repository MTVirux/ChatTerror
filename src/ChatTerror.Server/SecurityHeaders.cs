namespace ChatTerror.Server;

public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; " +
        "manifest-src 'self'; worker-src 'self'; font-src 'self'; media-src 'self'; object-src 'none'; " +
        "base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    // Camera stays allowed for scanning the pairing QR code.
    public const string PermissionsPolicy =
        "camera=(self), microphone=(), geolocation=(), payment=(), usb=(), serial=(), bluetooth=(), " +
        "accelerometer=(), gyroscope=(), magnetometer=(), display-capture=(), browsing-topics=()";

    // Must run after UseForwardedHeaders so IsHttps reflects the proxy's scheme.
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = PermissionsPolicy;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            if (context.Request.IsHttps)
                headers.StrictTransportSecurity = "max-age=31536000";
            await next(context);
        });
}
