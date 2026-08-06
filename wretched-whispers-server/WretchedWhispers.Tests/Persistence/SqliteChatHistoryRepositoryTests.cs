using Microsoft.Extensions.AI;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Repositories;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

public sealed class SqliteChatHistoryRepositoryTests : SqliteTestBase
{
    private readonly SqliteChatHistoryRepository _repo;

    public SqliteChatHistoryRepositoryTests()
    {
        _repo = new SqliteChatHistoryRepository(Db, TimeProvider.System);
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
    public async Task GetLastActivityForCampaigns_TracksMessagesAndFallsBackToSessionStart()
    {
        var campaignId = Guid.NewGuid();

        Assert.Empty(await _repo.GetLastActivityForCampaigns([campaignId]));

        var sessionId = await _repo.CreateSession(campaignId);
        Assert.True(
            (await _repo.GetLastActivityForCampaigns([campaignId])).ContainsKey(campaignId));

        await _repo.SaveMessage(sessionId, new ChatMessage(ChatRole.User, "hello"));
        var afterMessage = await _repo.GetLastActivityForCampaigns([campaignId]);
        var messageTimestamp = Db.ChatMessages.Single(m => m.SessionId == sessionId).Timestamp;
        Assert.Equal(messageTimestamp, afterMessage[campaignId]);
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
        var olderEntity = Db.ChatSessions.Single(s => s.Id == older);
        olderEntity.StartedAt = DateTime.UtcNow.AddMinutes(-10);
        await Db.SaveChangesAsync();

        var sessions = await _repo.GetSessionsForCampaign(campaignId);

        Assert.Equal(new[] { newer, older }, sessions);
    }

    [Fact]
    public async Task GetSummary_NoSummarySaved_ReturnsNull()
    {
        var sessionId = await _repo.CreateSession(Guid.NewGuid());
        Assert.Null(await _repo.GetSummary(sessionId));
    }

    [Fact]
    public async Task SaveSummary_ThenGet_RoundTrips()
    {
        var sessionId = await _repo.CreateSession(Guid.NewGuid());
        await _repo.SaveSummary(sessionId, new ChatSummary("the tale so far", 42));

        var summary = await _repo.GetSummary(sessionId);

        Assert.NotNull(summary);
        Assert.Equal("the tale so far", summary.Text);
        Assert.Equal(42, summary.CoveredCount);
    }

    [Fact]
    public async Task SaveSummary_UnknownSession_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.SaveSummary(Guid.NewGuid(), new ChatSummary("x", 1)));
    }

    [Fact]
    public async Task MarkOpened_UpdatesOpeningTimestamp()
    {
        var sessionId = await _repo.CreateSession(Guid.NewGuid());
        var openedAt = DateTime.UtcNow.AddDays(-1);

        await _repo.MarkOpened(sessionId, openedAt);

        Assert.Equal(openedAt, await _repo.GetLastOpened(sessionId));
    }

    [Fact]
    public async Task RecapCache_IsKeyedToActivity_NotOpening()
    {
        var sessionId = await _repo.CreateSession(Guid.NewGuid());
        var activity = await _repo.GetSessionLastActivity(sessionId);
        Assert.NotNull(activity);
        await _repo.SaveRecap(sessionId, new ChatRecap("cached doom", activity.Value));

        await _repo.MarkOpened(sessionId, DateTime.UtcNow.AddHours(1));

        Assert.Equal(new ChatRecap("cached doom", activity.Value), await _repo.GetRecap(sessionId));
        Assert.Equal(activity, await _repo.GetSessionLastActivity(sessionId));

        await _repo.SaveMessage(sessionId, new ChatMessage(ChatRole.User, "I disturb the world"));

        Assert.NotEqual(activity, await _repo.GetSessionLastActivity(sessionId));
    }
}
