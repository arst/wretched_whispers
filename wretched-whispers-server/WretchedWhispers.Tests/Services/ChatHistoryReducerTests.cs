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

    private void VerifyNoModelCall() =>
        _chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private void SetupStored(ChatSummary? summary, int messages)
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(summary);
        _repo.Setup(r => r.LoadSession(_sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(Messages(messages));
    }

    // ---- ComposeAsync: what a turn sends the model. Read-only, whatever the history's length. ----

    [Fact]
    public async Task Compose_NoStoredSummary_ReturnsHistoryUnchanged()
    {
        SetupStored(null, 0);

        var result = await CreateReducer().ComposeAsync(_sessionId, Messages(10), CancellationToken.None);

        Assert.Equal(10, result.Count);
        VerifyNoModelCall();
    }

    [Fact]
    public async Task Compose_WithStoredSummary_PrependsSummary_SkipsCoveredMessages()
    {
        SetupStored(new ChatSummary("earlier doom", 50), 0);

        var result = await CreateReducer().ComposeAsync(_sessionId, Messages(120), CancellationToken.None);

        Assert.Equal(71, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Contains("earlier doom", result[0].Text);
        Assert.Equal("msg 50", result[1].Text);
        VerifyNoModelCall();
    }

    [Fact]
    public async Task Compose_OverThreshold_StillNeverSummarizesOrWrites()
    {
        // The turn must not wait on a summary: even far past the threshold, composing only reads.
        SetupStored(new ChatSummary("earlier doom", 20), 0);

        var result = await CreateReducer().ComposeAsync(_sessionId, Messages(200), CancellationToken.None);

        Assert.Equal(181, result.Count); // the stored summary + all 180 uncovered messages
        VerifyNoModelCall();
        _repo.Verify(r => r.SaveSummary(It.IsAny<Guid>(), It.IsAny<ChatSummary>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repo.Verify(r => r.TrySaveSummary(
            It.IsAny<Guid>(), It.IsAny<ChatSummary>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- SummarizeIfDueAsync: the post-turn pass ----

    [Fact]
    public async Task Summarize_UnderThreshold_DoesNothing()
    {
        SetupStored(new ChatSummary("earlier doom", 50), messages: 120); // 70 uncovered

        Assert.False(await CreateReducer().SummarizeIfDueAsync(_sessionId, CancellationToken.None));

        VerifyNoModelCall();
    }

    [Fact]
    public async Task Summarize_OverThreshold_FoldsOlderMessagesIn_AndAdvancesTheWatermarkFromWhatItRead()
    {
        SetupStored(new ChatSummary("earlier doom", 20), messages: 200); // 180 uncovered -> fold 80, keep 100
        SetupSummarizerResponse("fresh summary");
        IEnumerable<ChatMessage>? sent = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => sent = m.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "fresh summary")));
        _repo.Setup(r => r.TrySaveSummary(
                _sessionId, It.IsAny<ChatSummary>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.True(await CreateReducer().SummarizeIfDueAsync(_sessionId, CancellationToken.None));

        // The previous summary plus messages 20..99 went to the summarizer.
        Assert.NotNull(sent);
        Assert.Equal(81, sent!.Count());
        Assert.Contains("earlier doom", sent.First().Text);
        Assert.Equal("msg 20", sent.ElementAt(1).Text);
        Assert.Equal("msg 99", sent.Last().Text);
        _repo.Verify(r => r.TrySaveSummary(
            _sessionId,
            It.Is<ChatSummary>(s => s.Text == "fresh summary" && s.CoveredCount == 100),
            20,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Summarize_EmptySummarizerResponse_SavesNothing()
    {
        SetupStored(new ChatSummary("kept", 20), messages: 200);
        SetupSummarizerResponse("   ");

        Assert.False(await CreateReducer().SummarizeIfDueAsync(_sessionId, CancellationToken.None));

        _repo.Verify(r => r.TrySaveSummary(
            It.IsAny<Guid>(), It.IsAny<ChatSummary>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Summarize_WatermarkMovedMeanwhile_ReportsNothingSaved()
    {
        // Another pass finished first: the guarded save writes nothing, and this result is dropped.
        SetupStored(null, messages: 200);
        SetupSummarizerResponse("late summary");
        _repo.Setup(r => r.TrySaveSummary(
                _sessionId, It.IsAny<ChatSummary>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Assert.False(await CreateReducer().SummarizeIfDueAsync(_sessionId, CancellationToken.None));
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
