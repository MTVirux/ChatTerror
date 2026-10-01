using System.Net;
using System.Threading.RateLimiting;
using ChatTerror.Server;
using ChatTerror.Server.Api;
using ChatTerror.Server.Data;
using ChatTerror.Server.Push;
using ChatTerror.Server.Relay;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var relay = context.Configuration.GetSection(RelayOptions.Section).Get<RelayOptions>() ?? new RelayOptions();
    kestrel.Limits.MaxConcurrentConnections = relay.MaxConcurrentConnections;
    kestrel.Limits.MaxConcurrentUpgradedConnections = relay.MaxConcurrentUpgradedConnections;
});

builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RelayStore>();
builder.Services.AddSingleton<VapidKeys>();
builder.Services.AddSingleton<IPushSender, WebPushSender>();
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<RelaySocketHandler>();
builder.Services.AddSingleton<PairingLimiter>();
builder.Services.AddSingleton<ExpiryService>();
builder.Services.AddHostedService(services => services.GetRequiredService<ExpiryService>());
builder.Services.AddCors();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.OnRejected = (context, ct) =>
        new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(new ErrorBody("rateLimited"), ct));
    limiter.AddPolicy(RequestLimits.PairingPolicy, context => PerClient(context, relay => relay.PairingRequestsPerMinute, TimeSpan.FromMinutes(1)));
    limiter.AddPolicy(RequestLimits.InstallPolicy, context => PerClient(context, relay => relay.InstallsPerHour, TimeSpan.FromHours(1)));
});

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<RelayOptions>>().Value;
app.Services.GetRequiredService<RelayStore>();
app.Services.GetRequiredService<VapidKeys>();

var trustedProxies = options.GetTrustedProxies();
if (trustedProxies.Length > 0)
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    forwarded.KnownProxies.Clear();
    forwarded.KnownIPNetworks.Clear();
    foreach (var proxy in trustedProxies)
    {
        if (proxy.Contains('/'))
            forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxy));
        else
            forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
    }
    app.UseForwardedHeaders(forwarded);
}

var allowedOrigins = options.GetAllowedOrigins();
app.UseOriginCheck(allowedOrigins);
if (allowedOrigins.Length > 0)
    app.UseCors(cors => cors.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
app.UseApiBodyLimit(options.MaxRequestBodyBytes);

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = file =>
    {
        if (file.File.Name is "index.html" or "manifest.webmanifest" or "sw.js")
            file.Context.Response.Headers.CacheControl = "no-cache";
    },
});

// Without a timeout a phone that went to sleep stays "online" and its messages are never pushed.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(20) });
app.UseRateLimiter();

app.MapInstallEndpoints();
app.MapPairingEndpoints();
app.MapDeviceEndpoints();
app.Map("/ws", (HttpContext context, RelaySocketHandler handler) => handler.HandleAsync(context));

app.Run();

static RateLimitPartition<string> PerClient(HttpContext context, Func<RelayOptions, int> permits, TimeSpan window)
{
    var relay = context.RequestServices.GetRequiredService<IOptions<RelayOptions>>().Value;
    return RateLimitPartition.GetFixedWindowLimiter(RequestLimits.PartitionKey(context.Connection.RemoteIpAddress), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permits(relay),
        Window = window,
    });
}

public partial class Program;
