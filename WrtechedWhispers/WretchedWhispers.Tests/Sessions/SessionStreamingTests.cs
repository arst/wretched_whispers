using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Tests.Sessions;

public class SessionStreamingTests : IClassFixture<SessionStreamingTests.StreamingWebAppFactory>, IDisposable
{
    private readonly HttpClient _client;
    private readonly StreamingWebAppFactory _factory;

    public SessionStreamingTests(StreamingWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostAction_WithoutAuth_Returns401()
    {
        var sessionId = Guid.NewGuid();
        var content = new StringContent(
            JsonSerializer.Serialize(new { message = "Hello" }),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync($"/sessions/{sessionId}/actions", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAction_WithNonExistentSession_Returns404()
    {
        var token = await RegisterAndLogin("stream-nonexistent@test.com");
        var nonExistentId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/sessions/{nonExistentId}/actions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { message = "Hello" }),
            Encoding.UTF8,
            "application/json");

        var response = await _client.SendAsync(request);

        // Ownership check returns 404 before SSE headers (non-existent session not in user's campaigns)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostAction_ReturnsSSEContentType()
    {
        var token = await RegisterAndLogin("stream-content-type@test.com");

        // Create a session first
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResponse = await _client.SendAsync(createRequest);
        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createJson.GetProperty("sessionId").GetString()!;

        // POST action -- the response will start streaming with SSE content-type
        // Since no real LLM is configured, we expect either:
        // 1. SSE content-type with an error event (LLM not configured)
        // 2. An error about configuration
        var actionRequest = new HttpRequestMessage(HttpMethod.Post, $"/sessions/{sessionId}/actions");
        actionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        actionRequest.Content = new StringContent(
            JsonSerializer.Serialize(new { message = "Let us begin!" }),
            Encoding.UTF8,
            "application/json");

        var response = await _client.SendAsync(actionRequest, HttpCompletionOption.ResponseHeadersRead);

        // Content-Type should be text/event-stream (set before any errors)
        Assert.StartsWith("text/event-stream", response.Content.Headers.ContentType?.ToString() ?? "");

        // Read the body to ensure the stream completes
        var body = await response.Content.ReadAsStringAsync();

        // Should have at least some SSE events (error event at minimum due to no LLM)
        Assert.Contains("event:", body);
    }

    [Fact]
    public async Task PostAction_SessionOwnedByOtherUser_ReturnsError()
    {
        // User A creates a session
        var tokenA = await RegisterAndLogin("stream-owner-a@test.com");
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var createResponse = await _client.SendAsync(createRequest);
        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createJson.GetProperty("sessionId").GetString()!;

        // User B tries to post an action to User A's session
        var tokenB = await RegisterAndLogin("stream-owner-b@test.com");
        var actionRequest = new HttpRequestMessage(HttpMethod.Post, $"/sessions/{sessionId}/actions");
        actionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        actionRequest.Content = new StringContent(
            JsonSerializer.Serialize(new { message = "Intruder!" }),
            Encoding.UTF8,
            "application/json");

        var response = await _client.SendAsync(actionRequest);

        // Ownership check returns 404 before SSE headers -- not an SSE stream
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    public void Dispose()
    {
        _client.Dispose();
    }

    public class StreamingWebAppFactory : WebApplicationFactory<Program>
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

public class SessionConcurrencyGuardTests
{
    [Fact]
    public async Task TryAcquire_ReturnsTrue_OnFirstCall()
    {
        var guard = new SessionConcurrencyGuard();
        var sessionId = Guid.NewGuid();

        var result = await guard.TryAcquire(sessionId);

        Assert.True(result);
    }

    [Fact]
    public async Task TryAcquire_ReturnsFalse_OnSecondCallSameSession()
    {
        var guard = new SessionConcurrencyGuard();
        var sessionId = Guid.NewGuid();

        await guard.TryAcquire(sessionId);
        var result = await guard.TryAcquire(sessionId);

        Assert.False(result);
    }

    [Fact]
    public async Task TryAcquire_ReturnsTrue_AfterRelease()
    {
        var guard = new SessionConcurrencyGuard();
        var sessionId = Guid.NewGuid();

        await guard.TryAcquire(sessionId);
        guard.Release(sessionId);
        var result = await guard.TryAcquire(sessionId);

        Assert.True(result);
    }

    [Fact]
    public async Task TryAcquire_DifferentSessions_DoNotInterfere()
    {
        var guard = new SessionConcurrencyGuard();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();

        var resultA = await guard.TryAcquire(sessionA);
        var resultB = await guard.TryAcquire(sessionB);

        Assert.True(resultA);
        Assert.True(resultB);
    }

    [Fact]
    public async Task TryAcquire_SameSession_BlockedWhileOtherHoldsLock()
    {
        var guard = new SessionConcurrencyGuard();
        var sessionId = Guid.NewGuid();

        // First acquire succeeds
        var first = await guard.TryAcquire(sessionId);
        Assert.True(first);

        // Second acquire on same session fails
        var second = await guard.TryAcquire(sessionId);
        Assert.False(second);

        // Third session is independent
        var otherId = Guid.NewGuid();
        var third = await guard.TryAcquire(otherId);
        Assert.True(third);

        // Release first session
        guard.Release(sessionId);

        // Now first session can be acquired again
        var fourth = await guard.TryAcquire(sessionId);
        Assert.True(fourth);
    }
}
