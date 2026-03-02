using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
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
    public async Task SaveMessage_LoadSession_RoundTripsSimpleUserTextMessage()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = await _repo.CreateSession(campaignId);

        var original = new ChatMessageContent(AuthorRole.User, "Hello, Game Master!");

        await _repo.SaveMessage(sessionId, original);
        var loaded = await _repo.LoadSession(sessionId);

        Assert.NotNull(loaded);
        Assert.Single(loaded);
        var msg = loaded[0];
        Assert.Equal(AuthorRole.User, msg.Role);
        Assert.Equal("Hello, Game Master!", msg.Content);
    }

    [Fact]
    public async Task SaveMessage_LoadSession_RoundTripsAssistantMessageWithFunctionCallContent()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = await _repo.CreateSession(campaignId);

        // Create assistant message with function call content
        var functionCallContent = new FunctionCallContent(
            functionName: "CreateCharacter",
            pluginName: "Character",
            id: "call_123",
            arguments: new KernelArguments { ["name"] = "Grim" });

        var original = new ChatMessageContent(AuthorRole.Assistant, items: [functionCallContent])
        {
            AuthorName = "Game_Master"
        };

        await _repo.SaveMessage(sessionId, original);
        var loaded = await _repo.LoadSession(sessionId);

        Assert.NotNull(loaded);
        Assert.Single(loaded);
        var msg = loaded[0];
        Assert.Equal(AuthorRole.Assistant, msg.Role);
        Assert.Equal("Game_Master", msg.AuthorName);

        // Verify function call content round-tripped
        var functionCalls = msg.Items.OfType<FunctionCallContent>().ToList();
        Assert.Single(functionCalls);
        Assert.Equal("CreateCharacter", functionCalls[0].FunctionName);
        Assert.Equal("Character", functionCalls[0].PluginName);
        Assert.Equal("call_123", functionCalls[0].Id);
    }

    [Fact]
    public async Task MultipleMessages_LoadInCorrectOrder()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = await _repo.CreateSession(campaignId);

        var msg1 = new ChatMessageContent(AuthorRole.System, "You are a Game Master.");
        var msg2 = new ChatMessageContent(AuthorRole.User, "Let's play!");
        var msg3 = new ChatMessageContent(AuthorRole.Assistant, "Welcome to the dying world.")
        {
            AuthorName = "Game_Master"
        };

        await _repo.SaveMessage(sessionId, msg1);
        await _repo.SaveMessage(sessionId, msg2);
        await _repo.SaveMessage(sessionId, msg3);

        var loaded = await _repo.LoadSession(sessionId);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Count);
        Assert.Equal(AuthorRole.System, loaded[0].Role);
        Assert.Equal("You are a Game Master.", loaded[0].Content);
        Assert.Equal(AuthorRole.User, loaded[1].Role);
        Assert.Equal("Let's play!", loaded[1].Content);
        Assert.Equal(AuthorRole.Assistant, loaded[2].Role);
        Assert.Equal("Welcome to the dying world.", loaded[2].Content);
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

        await _repo.SaveMessage(sessionA, new ChatMessageContent(AuthorRole.User, "Session A message"));
        await _repo.SaveMessage(sessionB, new ChatMessageContent(AuthorRole.User, "Session B message"));
        await _repo.SaveMessage(sessionA, new ChatMessageContent(AuthorRole.Assistant, "Session A reply"));

        var loadedA = await _repo.LoadSession(sessionA);
        var loadedB = await _repo.LoadSession(sessionB);

        Assert.NotNull(loadedA);
        Assert.Equal(2, loadedA.Count);
        Assert.Equal("Session A message", loadedA[0].Content);
        Assert.Equal("Session A reply", loadedA[1].Content);

        Assert.NotNull(loadedB);
        Assert.Single(loadedB);
        Assert.Equal("Session B message", loadedB[0].Content);
    }
}
