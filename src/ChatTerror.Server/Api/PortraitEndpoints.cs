namespace ChatTerror.Server.Api;

public static class PortraitEndpoints
{
    // Unauthenticated because the web app loads it straight from <img src>.
    public static void MapPortraitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/portrait", async (string? name, string? world, HttpContext context, LodestonePortraits portraits) =>
        {
            if (!LodestonePortraits.IsValid(name, world) || await portraits.FindAsync(name!, world!) is not { } image)
                return AuthHelpers.NotFound();

            context.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(image, "image/jpeg");
        }).RequireRateLimiting(RequestLimits.PortraitPolicy);
    }
}
