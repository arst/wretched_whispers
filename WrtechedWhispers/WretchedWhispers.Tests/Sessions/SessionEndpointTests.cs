using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
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
    public async Task CreateSession_WithoutBody_DefaultsToGrimDifficulty()
    {
        var token = await RegisterAndLogin("no-body-difficulty@test.com");
        var request = AuthPost("/sessions", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var listResponse = await _client.SendAsync(AuthGet("/sessions", token));
        var json = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Grim", json[0].GetProperty("difficulty").GetString());
    }

    [Fact]
    public async Task CreateSession_WithDifficulty_ReturnsSelectedDifficultyOnPreviewAndDetail()
    {
        var token = await RegisterAndLogin("difficulty-session@test.com");
        var request = AuthPost("/sessions", token);
        request.Content = JsonContent.Create(new { difficulty = "Hardcore" });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var createJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createJson.GetProperty("sessionId").GetString()!;

        var listResponse = await _client.SendAsync(AuthGet("/sessions", token));
        var listRaw = await listResponse.Content.ReadAsStringAsync();
        // Pins the wire casing: HTTP layer has no global enum converter registered (unlike the domain
        // blob's AggregateJsonOptions), so the Difficulty type's own [JsonConverter] attribute applies
        // and emits the exact enum member spelling (PascalCase), not camelCase.
        Assert.Contains("\"difficulty\":\"Hardcore\"", listRaw);

        var detailResponse = await _client.SendAsync(AuthGet($"/sessions/{sessionId}", token));
        var detailJson = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Hardcore", detailJson.GetProperty("difficulty").GetString());
    }

    [Fact]
    public async Task CreateSession_AcceptsLowercaseDifficulty_RequestReadIsCaseInsensitive()
    {
        var token = await RegisterAndLogin("lowercase-difficulty@test.com");
        var request = AuthPost("/sessions", token);
        request.Content = JsonContent.Create(new { difficulty = "hardcore" });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var listResponse = await _client.SendAsync(AuthGet("/sessions", token));
        var json = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Hardcore", json[0].GetProperty("difficulty").GetString());
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

    [Fact]
    public async Task GetSessionJournal_ReturnsEntriesForOwner_NotFoundForOtherUser()
    {
        var tokenA = await RegisterAndLogin("journal-owner@test.com");
        var createResponse = await _client.SendAsync(AuthPost("/sessions", tokenA));
        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createJson.GetProperty("sessionId").GetString()!;
        var campaignId = Guid.Parse(createJson.GetProperty("campaignId").GetString()!);

        // Record a journal entry through the domain, same scoped-DI path the game tools use
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<WretchedWhispersDbContext>();
            var entity = await db.Campaigns.FindAsync(campaignId);
            sp.GetRequiredService<ITenantContext>().SetUserId(entity!.UserId);
            var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
            var campaign = await campaignsRepo.Get(campaignId);
            campaign!.RecordJournalEntry(JournalCategory.Npc, "Met the grave-priest Ulmt");
            await campaignsRepo.SaveCampaign(campaign);
        }

        var response = await _client.SendAsync(AuthGet($"/sessions/{sessionId}/journal", tokenA));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entries = json.GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal("Npc", entries[0].GetProperty("category").GetString());
        Assert.Equal("Met the grave-priest Ulmt", entries[0].GetProperty("text").GetString());
        Assert.Equal(1, entries[0].GetProperty("day").GetInt32());
        Assert.Equal(0, entries[0].GetProperty("hour").GetInt32());

        // Other user gets 404, not 403
        var tokenB = await RegisterAndLogin("journal-other@test.com");
        var otherResponse = await _client.SendAsync(AuthGet($"/sessions/{sessionId}/journal", tokenB));
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
    }

    [Fact]
    public async Task GetSessionMap_ReturnsPoisForOwner_NotFoundForOtherUser()
    {
        var tokenA = await RegisterAndLogin("map-owner@test.com");
        var createResponse = await _client.SendAsync(AuthPost("/sessions", tokenA));
        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createJson.GetProperty("sessionId").GetString()!;
        var campaignId = Guid.Parse(createJson.GetProperty("campaignId").GetString()!);

        // Chart POIs through the domain, same scoped-DI path the game tools use
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<WretchedWhispersDbContext>();
            var entity = await db.Campaigns.FindAsync(campaignId);
            sp.GetRequiredService<ITenantContext>().SetUserId(entity!.UserId);
            var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
            var campaign = await campaignsRepo.Get(campaignId);
            campaign!.RecordPointOfInterest(PoiType.Town, "Galgenbeck", 48, 30);
            campaign.RecordPointOfInterest(PoiType.Dungeon, "Rot-Black Sludge", 60, 42, "Galgenbeck");
            campaign.SetPartyLocation("Galgenbeck");
            await campaignsRepo.SaveCampaign(campaign);
        }

        var response = await _client.SendAsync(AuthGet($"/sessions/{sessionId}/map", tokenA));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Galgenbeck", json.GetProperty("currentLocationName").GetString());
        var pois = json.GetProperty("pois");
        Assert.Equal(2, pois.GetArrayLength());
        Assert.Equal("Galgenbeck", pois[0].GetProperty("name").GetString());
        Assert.Equal("Town", pois[0].GetProperty("type").GetString());
        Assert.Equal(48, pois[0].GetProperty("x").GetInt32());
        Assert.Equal(30, pois[0].GetProperty("y").GetInt32());
        Assert.Equal("Galgenbeck", pois[1].GetProperty("connectedTo").GetString());

        // Other user gets 404, not 403
        var tokenB = await RegisterAndLogin("map-other@test.com");
        var otherResponse = await _client.SendAsync(AuthGet($"/sessions/{sessionId}/map", tokenB));
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
    }

    [Fact]
    public async Task CampaignsRepository_ParameterlessSaveCampaign_PreservesTenantUserId_ThroughScopedDI()
    {
        // Arrange: Create a DI scope from the real application (same as the turn pipeline)
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // Set tenant context (same as endpoint filter does from JWT claims)
        var tenantContext = sp.GetRequiredService<ITenantContext>();
        tenantContext.SetUserId("e2e-test-user");

        // Resolve the repository from the SAME scope. The game tools save via the parameterless
        // SaveCampaign overload, which must stamp the entity with the scoped tenant's UserId.
        var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();

        // Act
        var campaign = Campaign.Create(Difficulty.Grim, "E2E Tenant Test", "Verifying tenant propagation");
        await campaignsRepo.SaveCampaign(campaign);

        // Assert: Check the database entity has the correct UserId
        var db = sp.GetRequiredService<WretchedWhispersDbContext>();
        var entity = await db.Campaigns.FindAsync(campaign.Id);
        Assert.NotNull(entity);
        Assert.Equal("e2e-test-user", entity!.UserId);
    }

    [Fact]
    public async Task SessionSurvivesPluginSave_UserIdPreservedAfterCampaignModification()
    {
        // Arrange: User A creates a session via HTTP
        var tokenA = await RegisterAndLogin("plugin-save-test@test.com");
        var createRequest = AuthPost("/sessions", tokenA);
        var createResponse = await _client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var campaignId = Guid.Parse(createJson.GetProperty("campaignId").GetString()!);

        // Act: Simulate what happens during an agent turn --
        // resolve services from a scoped DI container with tenant context set
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<WretchedWhispersDbContext>();
        var entityBefore = await db.Campaigns.FindAsync(campaignId);
        Assert.NotNull(entityBefore);
        var originalUserId = entityBefore!.UserId;
        Assert.False(string.IsNullOrEmpty(originalUserId), "UserId should be set from session creation");

        // Set tenant to same userId as the original creator, then call parameterless save
        var tenantContext = sp.GetRequiredService<ITenantContext>();
        tenantContext.SetUserId(originalUserId);
        var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
        var campaign = await campaignsRepo.Get(campaignId);
        Assert.NotNull(campaign);

        // Save via parameterless (the previously broken path)
        await campaignsRepo.SaveCampaign(campaign!);

        // Assert: UserId is preserved after parameterless save
        db.ChangeTracker.Clear();
        var entityAfter = await db.Campaigns.FindAsync(campaignId);
        Assert.NotNull(entityAfter);
        Assert.Equal(originalUserId, entityAfter!.UserId);

        // Final assertion: User A can still see the session via HTTP (UserId survived)
        var listRequest = AuthGet("/sessions", tokenA);
        var listResponse = await _client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(listJson.GetArrayLength() >= 1, "User A should still see their session after plugin save");
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
