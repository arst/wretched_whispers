using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Tests.Sessions;
using Xunit;

namespace WretchedWhispers.Tests.Auth;

public class AuthEndpointTests : IClassFixture<AuthEndpointTests.AuthWebAppFactory>, IDisposable
{
    private readonly HttpClient _client;
    private readonly AuthWebAppFactory _factory;

    public AuthEndpointTests(AuthWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AuthMe_WithValidBearerToken_ReturnsUserId()
    {
        var accessToken = await AuthFlow.RegisterAndLoginWithBearerToken(_client, "authme@test.com");

        // Call /auth/me with bearer token
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var meResponse = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var meJson = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(meJson.TryGetProperty("userId", out var userId));
        Assert.False(string.IsNullOrEmpty(userId.GetString()));
    }

    [Fact]
    public async Task AuthMe_WithoutBearerToken_Returns401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>CSRF protection is unconditional, so a bearer token — which has no antiforgery cookie
    /// to pair with — can read but not write. Deliberate: the web app authenticates with cookies, and
    /// the exemption that used to let bearer clients through had to name Identity's cookie to spot
    /// them. Supporting bearer API clients again means giving them a token flow, not an exemption.</summary>
    [Fact]
    public async Task BearerToken_CannotMutate()
    {
        var accessToken = await AuthFlow.RegisterAndLoginWithBearerToken(_client, "bearer-write@test.com");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/sessions")
        {
            Content = JsonContent.Create(new { characterName = "Tokenbearer" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task CookieLogin_UsesCsrfProtectedLogout()
    {
        const string email = "cookie-auth@test.com";
        const string password = AuthFlow.Password;
        await _client.PostAsJsonAsync("/api/auth/register", new { email, password });

        var login = await _client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/auth/me")).StatusCode);

        var csrf = await _client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task CookieSessionMutation_RequiresCsrfToken()
    {
        const string email = "cookie-csrf@test.com";
        const string password = AuthFlow.Password;
        await _client.PostAsJsonAsync("/api/auth/register", new { email, password });
        await _client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password });

        var body = JsonContent.Create(new { characterName = "Cookie Wretch" });
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/sessions", body)).StatusCode);

        var csrf = await _client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/sessions")
        {
            Content = JsonContent.Create(new { characterName = "Cookie Wretch" })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(request)).StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    public class AuthWebAppFactory : WebApplicationFactory<Program>
    {
        private SqliteConnection? _connection;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove all DbContext-related registrations
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<WretchedWhispersDbContext>)
                             || d.ServiceType == typeof(WretchedWhispersDbContext))
                    .ToList();
                foreach (var descriptor in descriptors)
                    services.Remove(descriptor);

                // Use in-memory SQLite for tests
                _connection = new SqliteConnection("DataSource=:memory:");
                _connection.Open();

                services.AddDbContext<WretchedWhispersDbContext>(options =>
                    options.UseSqlite(_connection));
            });

            builder.UseEnvironment("Development");
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>().Database.Migrate();
            return host;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
