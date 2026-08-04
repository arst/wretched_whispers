using System.Net;
using WretchedWhispers.Tests.Auth;
using Xunit;

namespace WretchedWhispers.Tests.Health;

public class HealthEndpointTests : IClassFixture<AuthEndpointTests.AuthWebAppFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(AuthEndpointTests.AuthWebAppFactory factory) =>
        _client = factory.CreateClient();

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthyServerIsAvailable(string path) =>
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(path)).StatusCode);
}
