using System.Net;
using ChatTerror.Server.Api;
using ChatTerror.Server.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests;

public class PortraitTests
{
    private const string FaceUrl = "https://img2.finalfantasyxiv.com/f/abc_96x96.jpg?1700000000";
    private static readonly byte[] Face = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private static string Entry(string name, string world, string face = FaceUrl) => $"""
        <div class="entry"><a href="/lodestone/character/1/" class="entry__link">
        <div class="entry__chara__face"><img src="{face}" alt=""></div>
        <div class="entry__box entry__box--world"><p class="entry__name">{name}</p>
        <p class="entry__world"><i class="xiv-lds xiv-lds-home-world js__tooltip" data-tooltip="Home World"></i>{world}</p></div></a></div>
        """;

    private sealed class FakeLodestone(string html, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests)
                Requests.Add(request.RequestUri!);
            var response = request.RequestUri!.Host == "na.finalfantasyxiv.com"
                ? new HttpResponseMessage(status) { Content = new StringContent(html) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Face) };
            return Task.FromResult(response);
        }
    }

    private static RelayApp App(FakeLodestone lodestone) => new()
    {
        ExtraServices = services => services.AddHttpClient(LodestonePortraits.ClientName).ConfigurePrimaryHttpMessageHandler(() => lodestone),
    };

    [Fact]
    public async Task Portrait_ReturnsFaceOfMatchingCharacter()
    {
        var lodestone = new FakeLodestone(Entry("Other Person", "Gilgamesh [Aether]") + Entry("Jane O&#39;Doe", "Gilgamesh [Aether]"));
        using var app = App(lodestone);

        var response = await app.Client().GetAsync("/api/portrait?name=jane%20o'doe&world=Gilgamesh");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=86400", response.Headers.CacheControl?.ToString());
        Assert.Equal(Face, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(new Uri(FaceUrl), lodestone.Requests[1]);
    }

    [Fact]
    public async Task Portrait_IgnoresOtherWorlds()
    {
        var lodestone = new FakeLodestone(Entry("Jane Doe", "Sargatanas [Aether]"));
        using var app = App(lodestone);

        var response = await app.Client().GetAsync("/api/portrait?name=Jane%20Doe&world=Gilgamesh");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(lodestone.Requests);
    }

    [Fact]
    public async Task Portrait_NotFound_404()
    {
        var lodestone = new FakeLodestone("<html><body>Your search returned no results.</body></html>");
        using var app = App(lodestone);

        var response = await app.Client().GetAsync("/api/portrait?name=Jane%20Doe&world=Gilgamesh");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Portrait_OnlyFetchesLodestoneImages()
    {
        var lodestone = new FakeLodestone(Entry("Jane Doe", "Gilgamesh [Aether]", "https://evil.example.com/face.jpg"));
        using var app = App(lodestone);

        var response = await app.Client().GetAsync("/api/portrait?name=Jane%20Doe&world=Gilgamesh");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(lodestone.Requests);
    }

    [Fact]
    public async Task Portrait_LodestoneError_404()
    {
        var lodestone = new FakeLodestone("", HttpStatusCode.ServiceUnavailable);
        using var app = App(lodestone);

        var response = await app.Client().GetAsync("/api/portrait?name=Jane%20Doe&world=Gilgamesh");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("name=Jane&world=Gilgamesh")]
    [InlineData("name=Jane%20Doe%20Smith&world=Gilgamesh")]
    [InlineData("name=Jane1%20Doe&world=Gilgamesh")]
    [InlineData("name=Janeeeeeeeeee%20Doeeeeeeeee&world=Gilgamesh")]
    [InlineData("name=Jane%20Doe&world=Gil%20gamesh")]
    [InlineData("name=Jane%20Doe&world=Gilgameshhhhhhhhh")]
    [InlineData("name=Jane%20Doe")]
    [InlineData("")]
    public async Task Portrait_InvalidInput_404WithoutLookup(string query)
    {
        var lodestone = new FakeLodestone(Entry("Jane Doe", "Gilgamesh [Aether]"));
        using var app = App(lodestone);

        var response = await app.Client().GetAsync("/api/portrait?" + query);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(lodestone.Requests);
    }

    [Fact]
    public async Task Portrait_CachesHitsAndMisses()
    {
        var lodestone = new FakeLodestone(Entry("Jane Doe", "Gilgamesh [Aether]"));
        using var app = App(lodestone);
        var client = app.Client();

        await client.GetAsync("/api/portrait?name=Jane%20Doe&world=Gilgamesh");
        var again = await client.GetAsync("/api/portrait?name=JANE%20DOE&world=gilgamesh");
        await client.GetAsync("/api/portrait?name=John%20Doe&world=Gilgamesh");
        await client.GetAsync("/api/portrait?name=John%20Doe&world=Gilgamesh");

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(3, lodestone.Requests.Count);
    }

    [Fact]
    public async Task Portrait_ConcurrentLookupsShareOneRequest()
    {
        var lodestone = new FakeLodestone(Entry("Jane Doe", "Gilgamesh [Aether]"));
        using var app = App(lodestone);
        var client = app.Client();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync("/api/portrait?name=Jane%20Doe&world=Gilgamesh")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(2, lodestone.Requests.Count);
    }
}
