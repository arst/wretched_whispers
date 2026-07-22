using Microsoft.Extensions.AI;
using WretchedWhispers.Infrastructure.Persistence.Repositories;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

public class ChatHistoryRoundTripTests : IDisposable
{
    private readonly SqliteTestBase _db;
    private readonly SqliteChatHistoryRepository _repo;

    public ChatHistoryRoundTripTests()
    {
        _db = new SqliteTestBase();
        _repo = new SqliteChatHistoryRepository(_db.Db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task CreateSession_ReturnsNewGuid_AndGetSessionsForCampaignIncludesIt()
    {
        var campaignId = Guid.NewGuid();

        var sessionId = await _repo.CreateSession(campaignId);

        Assert.NotEqual(Guid.Empty, sessionId);
        var sessions = await _repo.GetSessionsForCampaign(campaignId);
        Assert.Contains(sessionId, sessions);
    }

    [Fact]
    public async Task GetLastActivity_TracksMessagesAndFallsBackToSessionStart()
    {
        var campaignId = Guid.NewGuid();

        Assert.Null(await _repo.GetLastActivity(campaignId));

        var sessionId = await _repo.CreateSession(campaignId);
        var afterCreate = await _repo.GetLastActivity(campaignId);
        Assert.NotNull(afterCreate);

        await _repo.SaveMessage(sessionId, new ChatMessage(ChatRole.User, "hello"));
        var afterMessage = await _repo.GetLastActivity(campaignId);
        Assert.NotNull(afterMessage);
        Assert.True(afterMessage >= afterCreate);
    }

    [Fact]
    public async Task SaveMessage_LoadSession_RoundTripsSimpleUserTextMessage()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = await _repo.CreateSession(campaignId);

        var original = new ChatMessage(ChatRole.User, "Hello, Game Master!");

        await _repo.SaveMessage(sessionId, original);
        var loaded = await _repo.LoadSession(sessionId);

        Assert.NotNull(loaded);
        Assert.Single(loaded);
        var msg = loaded[0];
        Assert.Equal(ChatRole.User, msg.Role);
        Assert.Equal("Hello, Game Master!", msg.Text);
    }

    [Fact]
    public async Task SaveMessage_LoadSession_RoundTripsAssistantMessageWithFunctionCallContent()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = await _repo.CreateSession(campaignId);

        // Create assistant message with function call content
        var functionCallContent = new FunctionCallContent(
            callId: "call_123",
            name: "CreateCharacter",
            arguments: new Dictionary<string, object?> { ["name"] = "Grim" });

        var original = new ChatMessage(ChatRole.Assistant, [functionCallContent])
        {
            AuthorName = "Game_Master"
        };

        await _repo.SaveMessage(sessionId, original);
        var loaded = await _repo.LoadSession(sessionId);

        Assert.NotNull(loaded);
        Assert.Single(loaded);
        var msg = loaded[0];
        Assert.Equal(ChatRole.Assistant, msg.Role);
        Assert.Equal("Game_Master", msg.AuthorName);

        // Verify function call content round-tripped
        var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
        Assert.Single(functionCalls);
        Assert.Equal("CreateCharacter", functionCalls[0].Name);
        Assert.Equal("call_123", functionCalls[0].CallId);
    }

    [Fact]
    public async Task MultipleMessages_LoadInCorrectOrder()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = await _repo.CreateSession(campaignId);

        var msg1 = new ChatMessage(ChatRole.System, "You are a Game Master.");
        var msg2 = new ChatMessage(ChatRole.User, "Let's play!");
        var msg3 = new ChatMessage(ChatRole.Assistant, "Welcome to the dying world.")
        {
            AuthorName = "Game_Master"
        };

        await _repo.SaveMessage(sessionId, msg1);
        await _repo.SaveMessage(sessionId, msg2);
        await _repo.SaveMessage(sessionId, msg3);

        var loaded = await _repo.LoadSession(sessionId);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Count);
        Assert.Equal(ChatRole.System, loaded[0].Role);
        Assert.Equal("You are a Game Master.", loaded[0].Text);
        Assert.Equal(ChatRole.User, loaded[1].Role);
        Assert.Equal("Let's play!", loaded[1].Text);
        Assert.Equal(ChatRole.Assistant, loaded[2].Role);
        Assert.Equal("Welcome to the dying world.", loaded[2].Text);
        Assert.Equal("Game_Master", loaded[2].AuthorName);
    }

    [Fact]
    public async Task LoadSession_ForNonExistentSession_ReturnsNull()
    {
        var loaded = await _repo.LoadSession(Guid.NewGuid());

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Messages_FromDifferentSessions_AreIsolated()
    {
        var campaignId = Guid.NewGuid();
        var sessionA = await _repo.CreateSession(campaignId);
        var sessionB = await _repo.CreateSession(campaignId);

        await _repo.SaveMessage(sessionA, new ChatMessage(ChatRole.User, "Session A message"));
        await _repo.SaveMessage(sessionB, new ChatMessage(ChatRole.User, "Session B message"));
        await _repo.SaveMessage(sessionA, new ChatMessage(ChatRole.Assistant, "Session A reply"));

        var loadedA = await _repo.LoadSession(sessionA);
        var loadedB = await _repo.LoadSession(sessionB);

        Assert.NotNull(loadedA);
        Assert.Equal(2, loadedA.Count);
        Assert.Equal("Session A message", loadedA[0].Text);
        Assert.Equal("Session A reply", loadedA[1].Text);

        Assert.NotNull(loadedB);
        Assert.Single(loadedB);
        Assert.Equal("Session B message", loadedB[0].Text);
    }

    [Fact]
    public async Task GetSessionsForCampaign_ReturnsNewestFirst()
    {
        var campaignId = Guid.NewGuid();
        var older = await _repo.CreateSession(campaignId);
        var newer = await _repo.CreateSession(campaignId);

        // Make ordering unambiguous regardless of clock resolution.
        var olderEntity = _db.Db.ChatSessions.Single(s => s.Id == older);
        olderEntity.StartedAt = DateTime.UtcNow.AddMinutes(-10);
        await _db.Db.SaveChangesAsync();

        var sessions = await _repo.GetSessionsForCampaign(campaignId);

        Assert.Equal(new[] { newer, older }, sessions);
    }
}
