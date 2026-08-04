using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public sealed class ChatHistoryReducerTests
{
    private readonly Mock<IChatClient> _chatClient = new();
    private readonly Mock<IChatHistoryRepository> _repo = new();
    private readonly Guid _sessionId = Guid.NewGuid();

    private ChatHistoryReducer CreateReducer() =>
        new(_chatClient.Object, _repo.Object, NullLogger<ChatHistoryReducer>.Instance);

    private static IReadOnlyList<ChatMessage> Messages(int count) =>
        Enumerable.Range(0, count).Select(i => new ChatMessage(ChatRole.User, $"msg {i}")).ToList();

    private void SetupSummarizerResponse(string text) =>
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

    [Fact]
    public async Task UnderThreshold_NoStoredSummary_ReturnsHistoryUnchanged_NoModelCall()
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);

        var result = await CreateReducer().ReduceAsync(_sessionId, Messages(10), CancellationToken.None);

        Assert.Equal(10, result.Count);
        _chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UnderThreshold_WithStoredSummary_PrependsSummary_SkipsCoveredMessages()
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSummary("earlier doom", 50));

        // 120 total, 50 covered -> tail of 70, under threshold: summary + 70 messages, no model call.
        var result = await CreateReducer().ReduceAsync(_sessionId, Messages(120), CancellationToken.None);

        Assert.Equal(71, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Contains("earlier doom", result[0].Text);
        Assert.Equal("msg 50", result[1].Text);
        _chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OverThreshold_SummarizesTail_AdvancesWatermark()
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);
        SetupSummarizerResponse("fresh summary");

        // 200 messages, none covered -> summarize oldest 100, keep 100.
        var result = await CreateReducer().ReduceAsync(_sessionId, Messages(200), CancellationToken.None);

        Assert.Equal(101, result.Count);
        Assert.Contains("fresh summary", result[0].Text);
        _repo.Verify(r => r.SaveSummary(
            _sessionId,
            It.Is<ChatSummary>(s => s.Text == "fresh summary" && s.CoveredCount == 100),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task OverThreshold_EmptySummarizerResponse_DoesNotAdvanceWatermark()
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSummary("kept", 20));
        SetupSummarizerResponse("   ");

        // 200 total, 20 covered -> tail 180 over threshold, but summarization fails.
        var result = await CreateReducer().ReduceAsync(_sessionId, Messages(200), CancellationToken.None);

        _repo.Verify(r => r.SaveSummary(It.IsAny<Guid>(), It.IsAny<ChatSummary>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Contains("kept", result[0].Text);   // stored summary still leads
        Assert.Equal(101, result.Count);           // stored summary + recent 100
    }

    [Fact]
    public async Task OverThreshold_SaveSummaryThrows_ReturnsFreshSummaryAnyway()
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);
        SetupSummarizerResponse("fresh summary");
        _repo.Setup(r => r.SaveSummary(It.IsAny<Guid>(), It.IsAny<ChatSummary>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        // 200 messages, none covered -> summarize oldest 100, keep 100; save fails but must not throw.
        var result = await CreateReducer().ReduceAsync(_sessionId, Messages(200), CancellationToken.None);

        Assert.Equal(101, result.Count);
        Assert.Contains("fresh summary", result[0].Text);
    }

    [Fact]
    public async Task SeedEpitaph_SummarizesFallenChronicle_SeedsNewChronicleAtZero()
    {
        var fallenId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        _repo.Setup(r => r.LoadSession(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Messages(30));
        _repo.Setup(r => r.GetSummary(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);
        SetupSummarizerResponse("Grimnir died screaming beneath Galgenbeck.");

        var seeded = await CreateReducer().SeedEpitaphAsync(fallenId, newId, CancellationToken.None);

        Assert.True(seeded);
        _repo.Verify(r => r.SaveSummary(
            newId,
            It.Is<ChatSummary>(s => s.Text.Contains("Grimnir") && s.CoveredCount == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedEpitaph_IncludesStoredRollingSummary_SkipsCoveredMessages()
    {
        var fallenId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        _repo.Setup(r => r.LoadSession(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Messages(120));
        _repo.Setup(r => r.GetSummary(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSummary("earlier doom", 50));
        IEnumerable<ChatMessage>? sent = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => sent = m)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "the tale, finished")));

        var seeded = await CreateReducer().SeedEpitaphAsync(fallenId, newId, CancellationToken.None);

        Assert.True(seeded);
        Assert.NotNull(sent);
        var sentList = sent.ToList();
        // stored summary + 70 uncovered messages, not the covered 50 again
        Assert.Equal(71, sentList.Count);
        Assert.Contains("earlier doom", sentList[0].Text);
    }

    [Fact]
    public async Task SeedEpitaph_EmptyChronicle_ReturnsFalse_NoModelCall()
    {
        var fallenId = Guid.NewGuid();
        _repo.Setup(r => r.LoadSession(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var seeded = await CreateReducer().SeedEpitaphAsync(fallenId, Guid.NewGuid(), CancellationToken.None);

        Assert.False(seeded);
        _chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SeedEpitaph_SummarizerThrows_ReturnsFalse()
    {
        var fallenId = Guid.NewGuid();
        _repo.Setup(r => r.LoadSession(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Messages(10));
        _repo.Setup(r => r.GetSummary(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model unavailable"));

        var seeded = await CreateReducer().SeedEpitaphAsync(fallenId, Guid.NewGuid(), CancellationToken.None);

        Assert.False(seeded);
        _repo.Verify(r => r.SaveSummary(It.IsAny<Guid>(), It.IsAny<ChatSummary>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRecap_UsesSummaryUncoveredMessagesAndCurrentState()
    {
        _repo.Setup(r => r.LoadSession(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Messages(3));
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSummary("earlier doom", 1));
        IEnumerable<ChatMessage>? sent = null;
        ChatOptions? sentOptions = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, options, _) =>
            {
                sent = messages;
                sentOptions = options;
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "  Doom remembered.  ")));

        var recap = await CreateReducer().CreateRecapAsync(
            _sessionId,
            "Party location: Galgenbeck",
            CancellationToken.None);

        Assert.Equal("Doom remembered.", recap);
        var source = Assert.IsAssignableFrom<IEnumerable<ChatMessage>>(sent).ToList();
        Assert.Equal(4, source.Count);
        Assert.Contains("earlier doom", source[0].Text);
        Assert.Equal("msg 1", source[1].Text);
        Assert.Contains("Galgenbeck", source[^1].Text);
        Assert.Contains("Previously on", sentOptions?.Instructions);
    }

    [Fact]
    public async Task CreateRecap_ModelFailure_ReturnsNull()
    {
        _repo.Setup(r => r.LoadSession(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Messages(1));
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model unavailable"));

        Assert.Null(await CreateReducer().CreateRecapAsync(_sessionId, "state", CancellationToken.None));
    }
}
