using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using WretchedWhispers.Api.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

/// <summary>
/// Verifies the chat-history reducer that restores summarization parity after the SK→Agent
/// Framework migration: short sessions pass through untouched; long sessions are compacted to a
/// single summary plus the recent tail so the model's context stays bounded.
/// </summary>
public class ChatHistoryReducerTests
{
    private const int TargetCount = 100;
    private const int ThresholdCount = 150;

    private static List<ChatMessage> MakeHistory(int count)
    {
        var list = new List<ChatMessage>(count);
        for (var i = 0; i < count; i++)
        {
            var role = i % 2 == 0 ? ChatRole.User : ChatRole.Assistant;
            list.Add(new ChatMessage(role, $"message-{i}"));
        }
        return list;
    }

    [Fact]
    public async Task BelowThreshold_ReturnsHistoryUnchanged_AndNeverCallsModel()
    {
        var client = new CountingChatClient("should not be called");
        var reducer = new ChatHistoryReducer(client, NullLogger<ChatHistoryReducer>.Instance);

        var history = MakeHistory(ThresholdCount); // exactly at threshold => not reduced
        var result = await reducer.ReduceAsync(history, CancellationToken.None);

        Assert.Same(history, result);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task AboveThreshold_SummarizesOlder_KeepsRecentTail()
    {
        var client = new CountingChatClient("SUMMARY: the wretch bleeds in the dark.");
        var reducer = new ChatHistoryReducer(client, NullLogger<ChatHistoryReducer>.Instance);

        var history = MakeHistory(ThresholdCount + 20); // 170 messages
        var result = await reducer.ReduceAsync(history, CancellationToken.None);

        // 1 summary message + the most recent TargetCount messages.
        Assert.Equal(TargetCount + 1, result.Count);
        Assert.Equal(1, client.Calls);

        // First message is the summary.
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Contains("SUMMARY: the wretch bleeds", result[0].Text);

        // The recent tail is preserved verbatim and in order (last original message is last).
        Assert.Equal(history[^1].Text, result[^1].Text);
        Assert.Equal("message-169", result[^1].Text);
    }

    private sealed class CountingChatClient(string responseText) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, responseText);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
