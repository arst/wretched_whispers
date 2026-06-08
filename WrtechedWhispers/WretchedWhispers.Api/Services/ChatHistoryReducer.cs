using Microsoft.Extensions.AI;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Keeps the model's working context bounded on long sessions by summarizing older messages,
/// restoring the parity lost when the Semantic Kernel ChatHistorySummarizationReducer was dropped
/// during the Agent Framework migration.
///
/// When the loaded history exceeds <see cref="ThresholdCount"/> messages, the oldest messages
/// (all but the most recent <see cref="TargetCount"/>) are summarized into a single system message
/// that preserves MORK BORG game state and tone; the recent tail is kept verbatim. The full history
/// remains in the database — only what is sent to the model is reduced.
/// </summary>
public sealed class ChatHistoryReducer(IChatClient chatClient, ILogger<ChatHistoryReducer> logger)
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
        IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        if (history.Count <= ThresholdCount)
            return history;

        var olderCount = history.Count - TargetCount;
        var older = history.Take(olderCount).ToList();
        var recent = history.Skip(olderCount).ToList();

        var options = new ChatOptions { Instructions = SummarizationInstructions };
        var response = await chatClient.GetResponseAsync(older, options, ct);
        var summaryText = response.Text;

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            // Summarization produced nothing usable — keep the recent tail rather than risk losing
            // all context, and leave the older messages out (context still bounded).
            logger.LogWarning("History summarization returned empty; keeping recent {Count} messages", recent.Count);
            return recent;
        }

        logger.LogInformation(
            "Reduced chat history {Original} -> {Reduced} messages (summarized {Older} older into 1)",
            history.Count, recent.Count + 1, olderCount);

        var reduced = new List<ChatMessage>(recent.Count + 1)
        {
            new(ChatRole.System, $"[Summary of the session so far]\n{summaryText}")
        };
        reduced.AddRange(recent);
        return reduced;
    }
}
