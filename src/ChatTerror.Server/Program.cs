using System.Threading.RateLimiting;
using ChatTerror.Server;
using ChatTerror.Server.Api;
using ChatTerror.Server.Data;
using ChatTerror.Server.Push;
using ChatTerror.Server.Relay;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RelayStore>();
builder.Services.AddSingleton<VapidKeys>();
builder.Services.AddSingleton<IPushSender, WebPushSender>();
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<RelaySocketHandler>();
builder.Services.AddHostedService<ExpiryService>();
builder.Services.AddCors();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.OnRejected = (context, ct) =>
        new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(new ErrorBody("rateLimited"), ct));
    limiter.AddPolicy(PairingEndpoints.RateLimitPolicy, context =>
    {
        var perMinute = context.RequestServices.GetRequiredService<IOptions<RelayOptions>>().Value.PairingRequestsPerMinute;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute,
            Window = TimeSpan.FromMinutes(1),
        });
    });
});

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<RelayOptions>>().Value;
app.Services.GetRequiredService<RelayStore>();
app.Services.GetRequiredService<VapidKeys>();

var allowedOrigins = options.GetAllowedOrigins();
app.UseOriginCheck(allowedOrigins);
if (allowedOrigins.Length > 0)
    app.UseCors(cors => cors.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = file =>
    {
        if (file.File.Name == "sw.js")
            file.Context.Response.Headers.CacheControl = "no-cache";
    },
});

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseRateLimiter();

app.MapInstallEndpoints();
app.MapPairingEndpoints();
app.MapDeviceEndpoints();
app.Map("/ws", (HttpContext context, RelaySocketHandler handler) => handler.HandleAsync(context));

app.Run();

public partial class Program;
