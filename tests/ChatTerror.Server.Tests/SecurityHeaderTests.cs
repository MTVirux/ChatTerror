using ChatTerror.Server.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChatTerror.Server.Tests;

public class SecurityHeaderTests
{
    [Fact]
    public async Task IndexPage_HasSecurityHeaders()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "chatterror-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><title>x</title>");
        try
        {
            using var app = new RelayApp();
            using var factory = app.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));

            var response = await factory.CreateClient().GetAsync("/");

            response.EnsureSuccessStatusCode();
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            AssertSecurityHeaders(response);
        }
        finally
        {
            Directory.Delete(webRoot, true);
        }
    }

    [Fact]
    public async Task ApiResponse_HasSecurityHeaders()
    {
        using var app = new RelayApp();

        var response = await app.Client().GetAsync("/api/vapid");

        response.EnsureSuccessStatusCode();
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Hsts_OnlyOverHttps()
    {
        using var app = new RelayApp();
        var http = await app.Client().GetAsync("/");
        var https = await app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }).GetAsync("/");

        Assert.False(http.Headers.Contains("Strict-Transport-Security"));
        Assert.Equal("max-age=31536000", Header(https, "Strict-Transport-Security"));
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Header(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal(SecurityHeaders.PermissionsPolicy, Header(response, "Permissions-Policy"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Opener-Policy"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        string.Join(", ", response.Headers.GetValues(name));
}
