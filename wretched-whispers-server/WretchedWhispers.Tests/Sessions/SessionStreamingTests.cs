using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public async Task PostAction_SessionOwnedByOtherUser_ReturnsError()
    {
        // User A creates a session
        var tokenA = await RegisterAndLogin("stream-owner-a@test.com");
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        createRequest.Content = JsonContent.Create(new { characterName = "Test Wretch" });
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

    private Task<string> RegisterAndLogin(string email) => AuthFlow.RegisterAndLogin(_client, email);

    public void Dispose()
    {
        _client.Dispose();
    }

    public class StreamingWebAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString =
            $"DataSource=ww-stream-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        private SqliteConnection? _keepAlive;

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

                _keepAlive = new SqliteConnection(_connectionString);
                _keepAlive.Open();

                services.AddDbContext<WretchedWhispersDbContext>(options =>
                    options.UseSqlite(_connectionString));

                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(new SessionEndpointTests.NoOpChatClient());
            });

            builder.UseEnvironment("Development");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _keepAlive?.Close();
            _keepAlive?.Dispose();
        }
    }
}
