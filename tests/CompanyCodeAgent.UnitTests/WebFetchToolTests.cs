using CompanyCodeAgent.Tools;

namespace CompanyCodeAgent.UnitTests;

public sealed class WebFetchToolTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://localhost/admin")]
    [InlineData("https://127.0.0.1/admin")]
    [InlineData("https://192.168.1.10/admin")]
    [InlineData("https://[::1]/admin")]
    public void Rejects_NonPublic_Or_NonHttps_Endpoints(string url)
    {
        Assert.Throws<UnauthorizedAccessException>(() => WebFetchTool.EnsureSafeUri(new Uri(url)));
    }

    [Fact]
    public void Accepts_Public_Https_Hostname()
    {
        WebFetchTool.EnsureSafeUri(new Uri("https://docs.example.com/guide"));
    }
}
