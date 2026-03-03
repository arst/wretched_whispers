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

namespace WretchedWhispers.Tests.Sessions;

public class SessionEndpointTests : IClassFixture<SessionEndpointTests.SessionWebAppFactory>, IDisposable
{
    private readonly HttpClient _client;
    private readonly SessionWebAppFactory _factory;

    public SessionEndpointTests(SessionWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateSession_ReturnsCreatedWithSessionId()
    {
        var token = await RegisterAndLogin("create-session@test.com");
        var request = AuthPost("/sessions", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("sessionId", out var sessionId));
        Assert.NotEqual(Guid.Empty, Guid.Parse(sessionId.GetString()!));
        Assert.True(json.TryGetProperty("campaignId", out var campaignId));
        Assert.NotEqual(Guid.Empty, Guid.Parse(campaignId.GetString()!));
    }

    [Fact]
    public async Task CreateSession_WithoutAuth_Returns401()
    {
        var response = await _client.PostAsync("/sessions", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListSessions_ReturnsEmptyForNewUser()
    {
        var token = await RegisterAndLogin("empty-list@test.com");
        var request = AuthGet("/sessions", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal(0, json.GetArrayLength());
    }

    [Fact]
    public async Task ListSessions_ReturnsCreatedSession()
    {
        var token = await RegisterAndLogin("list-sessions@test.com");

        // Create a session
        var createRequest = AuthPost("/sessions", token);
        await _client.SendAsync(createRequest);

        // List sessions
        var listRequest = AuthGet("/sessions", token);
        var response = await _client.SendAsync(listRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetArrayLength());

        var session = json[0];
        Assert.Equal("New Campaign", session.GetProperty("campaignName").GetString());
        Assert.Equal("character-creation", session.GetProperty("status").GetString());
        Assert.Equal("A new journey into doom", session.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ListSessions_DoesNotShowOtherUsersSessions()
    {
        // User A creates a session
        var tokenA = await RegisterAndLogin("user-a-isolation@test.com");
        var createRequest = AuthPost("/sessions", tokenA);
        await _client.SendAsync(createRequest);

        // User B lists sessions - should see empty
        var tokenB = await RegisterAndLogin("user-b-isolation@test.com");
        var listRequest = AuthGet("/sessions", tokenB);
        var response = await _client.SendAsync(listRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetArrayLength());
    }

    [Fact]
    public async Task GetSessionDetail_ReturnsSessionState()
    {
        var token = await RegisterAndLogin("detail-session@test.com");

        // Create a session
        var createRequest = AuthPost("/sessions", token);
        var createResponse = await _client.SendAsync(createRequest);
        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createJson.GetProperty("sessionId").GetString()!;

        // Get session detail
        var detailRequest = AuthGet($"/sessions/{sessionId}", token);
        var response = await _client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sessionId, json.GetProperty("sessionId").GetString());
        Assert.Equal("New Campaign", json.GetProperty("campaignName").GetString());
        Assert.Equal(1, json.GetProperty("currentDay").GetInt32());
        Assert.Equal(0, json.GetProperty("currentHour").GetInt32());
        Assert.Equal("character-creation", json.GetProperty("status").GetString());
        Assert.Equal(0, json.GetProperty("totalMessages").GetInt32());

        var messages = json.GetProperty("messages");
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.Equal(0, messages.GetArrayLength());
    }

    [Fact]
    public async Task GetSessionDetail_ReturnsNotFoundForOtherUserSession()
    {
        // User A creates a session
        var tokenA = await RegisterAndLogin("user-a-detail@test.com");
        var createRequest = AuthPost("/sessions", tokenA);
        var createResponse = await _client.SendAsync(createRequest);
        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createJson.GetProperty("sessionId").GetString()!;

        // User B tries to get User A's session
        var tokenB = await RegisterAndLogin("user-b-detail@test.com");
        var detailRequest = AuthGet($"/sessions/{sessionId}", tokenB);
        var response = await _client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSessionMessages_ReturnsPaginatedMessages()
    {
        var token = await RegisterAndLogin("messages-session@test.com");

        // Create a session
        var createRequest = AuthPost("/sessions", token);
        var createResponse = await _client.SendAsync(createRequest);
        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createJson.GetProperty("sessionId").GetString()!;

        // Get messages
        var messagesRequest = AuthGet($"/sessions/{sessionId}/messages?page=1&pageSize=10", token);
        var response = await _client.SendAsync(messagesRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("totalMessages").GetInt32());
        Assert.Equal(1, json.GetProperty("page").GetInt32());
        Assert.Equal(10, json.GetProperty("pageSize").GetInt32());

        var messages = json.GetProperty("messages");
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.Equal(0, messages.GetArrayLength());
    }

    private async Task<string> RegisterAndLogin(string email)
    {
        await _client.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = "darkdoom42"
        });

        var loginResponse = await _client.PostAsJsonAsync("/auth/login?useCookies=false", new
        {
            email,
            password = "darkdoom42"
        });

        var loginJson = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        return loginJson.GetProperty("accessToken").GetString()!;
    }

    private static HttpRequestMessage AuthPost(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage AuthGet(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    public class SessionWebAppFactory : WebApplicationFactory<Program>
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
