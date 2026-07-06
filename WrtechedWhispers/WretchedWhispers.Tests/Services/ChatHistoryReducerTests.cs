using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Api.Services;
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
        _chatClient.VerifyNoOtherCalls();
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
        _chatClient.VerifyNoOtherCalls();
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
}
