using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Keeps the model's working context bounded on long sessions by summarizing older messages,
/// restoring the parity lost when the Semantic Kernel ChatHistorySummarizationReducer was dropped
/// during the Agent Framework migration.
///
/// When the loaded history exceeds <see cref="ThresholdCount"/> messages, the oldest messages
/// (all but the most recent <see cref="TargetCount"/>) are summarized into a single system message
/// that preserves MORK BORG game state and tone; the recent tail is kept verbatim. The full history
/// remains in the database — only what is sent to the model is reduced, and the summary watermark
/// is persisted so later turns summarize incrementally instead of re-summarizing the whole prefix.
/// </summary>
public sealed class ChatHistoryReducer(
    IChatClient chatClient,
    IChatHistoryRepository chatHistoryRepository,
    ILogger<ChatHistoryReducer> logger)
{
    private const int TargetCount = 100;
    private const int ThresholdCount = 150;

    private const string SummarizationInstructions =
        """
        Summarize this MORK BORG game session so far. The summary replaces older messages, so it
        must let the Game Master continue seamlessly. Preserve:

        ESSENTIAL GAME STATE:
        - Character names, current hit points, abilities, scars/injuries, infection, and omens
        - Current campaign location and time of day; misery/dawn progress toward the world's end
        - Active or recent encounters (adversaries, their status, ongoing combat)
        - Important NPCs met and their relationships
        - Key items, weapons, scrolls, and silver in possession
        - Current goals, quests, or destinations and unresolved plot hooks

        PRESERVE THE ATMOSPHERE:
        - The doom-laden, apocalyptic tone; the decaying world and mounting dread
        - Any omens, prophecies, or signs of the coming end; grim humour

        Discard repetitive description, resolved trivial beats, and rules chatter. Write the summary
        as terse narrative prose that clearly states the current game state.
        """;

    public async Task<IReadOnlyList<ChatMessage>> ReduceAsync(
        Guid chatSessionId, IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var stored = await chatHistoryRepository.GetSummary(chatSessionId, ct);
        var covered = stored?.CoveredCount ?? 0;
        var tail = history.Skip(covered).ToList();

        if (tail.Count <= ThresholdCount)
            return Compose(stored, tail);

        var olderCount = tail.Count - TargetCount;
        var toSummarize = new List<ChatMessage>(olderCount + 1);
        if (stored is not null)
            toSummarize.Add(SummaryMessage(stored.Text));
        toSummarize.AddRange(tail.Take(olderCount));

        var options = new ChatOptions { Instructions = SummarizationInstructions };
        var response = await chatClient.GetResponseAsync(toSummarize, options, ct);
        var summaryText = response.Text;
        var recent = tail.Skip(olderCount).ToList();

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            // Summarization produced nothing usable — keep the stored summary and the recent tail;
            // the watermark stays put so the next turn retries.
            logger.LogWarning("History summarization returned empty; keeping recent {Count} messages", recent.Count);
            return Compose(stored, recent);
        }

        var updated = new ChatSummary(summaryText, covered + olderCount);
        try
        {
            await chatHistoryRepository.SaveSummary(chatSessionId, updated, ct);
        }
        catch (Exception ex)
        {
            // Persisting the watermark failed — degrade gracefully rather than fail the player's
            // turn. The fresh summary is still used for this turn's response; since the watermark
            // did not advance, the next turn re-summarizes the same prefix and retries the save.
            logger.LogWarning(ex, "Failed to persist summary watermark for session {SessionId}", chatSessionId);
            return Compose(updated, recent);
        }

        logger.LogInformation(
            "Rolled summary forward — covered {Covered} of {Total} messages, sending {Sent}",
            updated.CoveredCount, history.Count, recent.Count + 1);

        return Compose(updated, recent);
    }

    private static ChatMessage SummaryMessage(string text) =>
        new(ChatRole.System, $"[Summary of the session so far]\n{text}");

    private static IReadOnlyList<ChatMessage> Compose(ChatSummary? summary, List<ChatMessage> tail)
    {
        if (summary is null)
            return tail;
        var result = new List<ChatMessage>(tail.Count + 1) { SummaryMessage(summary.Text) };
        result.AddRange(tail);
        return result;
    }
}
