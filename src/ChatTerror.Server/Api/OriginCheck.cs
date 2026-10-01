namespace ChatTerror.Server.Api;

public static class OriginCheck
{
    // Requests without an Origin header (the plugin) are always allowed.
    public static bool IsAllowed(HttpRequest request, string[] allowedOrigins)
    {
        var origin = request.Headers.Origin.ToString();
        if (origin == "")
            return true;
        if (allowedOrigins.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase))
            return true;

        // Compared by host only, so a TLS-terminating proxy in front of the relay still counts as same origin.
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    public static IApplicationBuilder UseOriginCheck(this IApplicationBuilder app, string[] allowedOrigins) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var isWrite = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method);
            var mustCheck = request.Path.StartsWithSegments("/ws") || (request.Path.StartsWithSegments("/api") && isWrite);
            if (mustCheck && !IsAllowed(request, allowedOrigins))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ErrorBody("forbiddenOrigin"));
                return;
            }
            await next(context);
        });
}
