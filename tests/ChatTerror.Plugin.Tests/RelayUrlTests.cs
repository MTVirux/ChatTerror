using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class RelayUrlTests
{
    [Theory]
    [InlineData("https://chatterror.mtvirux.app")]
    [InlineData("https://example.com:8443/relay/")]
    [InlineData("wss://example.com/ws")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://LOCALHOST")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://127.1.2.3")]
    [InlineData("http://[::1]:5000")]
    [InlineData("ws://localhost:5000/ws")]
    [InlineData("  https://example.com  ")]
    public void Allowed(string url)
    {
        Assert.True(RelayUrl.IsAllowed(url));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("http://192.168.1.10:5000")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://localhost.example.com")]
    [InlineData("http://127.0.0.1.example.com")]
    [InlineData("http://[::2]")]
    [InlineData("ws://example.com/ws")]
    [InlineData("ftp://localhost")]
    [InlineData("localhost:5000")]
    [InlineData("")]
    [InlineData("not a url")]
    public void Rejected(string url)
    {
        Assert.False(RelayUrl.IsAllowed(url));
    }

    [Theory]
    [InlineData("HTTPS://Example.com/relay/", "https://example.com/relay")]
    [InlineData(" http://localhost:5000/ ", "http://localhost:5000")]
    public void Normalize_LowercasesAndTrims(string url, string expected)
    {
        Assert.Equal(expected, RelayUrl.Normalize(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ws://localhost")]
    [InlineData("example.com")]
    public void Normalize_NonHttp_Null(string? url)
    {
        Assert.Null(RelayUrl.Normalize(url));
    }
}
