using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Infrastructure.Persistence;
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
    public async Task Register_WithValidCredentials_Returns200()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new
        {
            email = "register@test.com",
            password = "darkdoom42"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessTokenAndRefreshToken()
    {
        // Register first
        await _client.PostAsJsonAsync("/auth/register", new
        {
            email = "login@test.com",
            password = "darkdoom42"
        });

        // Login
        var response = await _client.PostAsJsonAsync("/auth/login?useCookies=false", new
        {
            email = "login@test.com",
            password = "darkdoom42"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("accessToken", out var accessToken));
        Assert.True(json.TryGetProperty("refreshToken", out var refreshToken));
        Assert.False(string.IsNullOrEmpty(accessToken.GetString()));
        Assert.False(string.IsNullOrEmpty(refreshToken.GetString()));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        // Register first
        await _client.PostAsJsonAsync("/auth/register", new
        {
            email = "wrongpwd@test.com",
            password = "darkdoom42"
        });

        // Login with wrong password
        var response = await _client.PostAsJsonAsync("/auth/login?useCookies=false", new
        {
            email = "wrongpwd@test.com",
            password = "wrongpassword"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthMe_WithValidBearerToken_ReturnsUserId()
    {
        // Register + Login
        await _client.PostAsJsonAsync("/auth/register", new
        {
            email = "authme@test.com",
            password = "darkdoom42"
        });

        var loginResponse = await _client.PostAsJsonAsync("/auth/login?useCookies=false", new
        {
            email = "authme@test.com",
            password = "darkdoom42"
        });

        var loginJson = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginJson.GetProperty("accessToken").GetString()!;

        // Call /auth/me with bearer token
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
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
        var response = await _client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

                // Create database schema
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
                db.Database.EnsureCreated();
            });

            builder.UseEnvironment("Development");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
