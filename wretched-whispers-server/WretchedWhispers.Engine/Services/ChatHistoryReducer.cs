using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Engine.Services;

/// <summary>Rolls a chronicle's summary forward when enough unsummarized history has built up.</summary>
public interface IHistorySummarizer
{
    /// <summary>Returns true when a new summary was saved.</summary>
    Task<bool> SummarizeIfDueAsync(Guid chatSessionId, CancellationToken ct);
}

/// <summary>
/// Keeps the model's working context bounded on long sessions by summarizing older messages.
///
/// Two separate steps, so the player never waits on a summary:
/// <list type="bullet">
/// <item><see cref="ComposeAsync"/> runs INSIDE the turn and only reads: the stored summary plus every
/// message it does not cover yet. No model call, no write.</item>
/// <item><see cref="SummarizeIfDueAsync"/> runs AFTER a turn has committed and its stream has ended,
/// outside the turn's transaction and session lock. When more than <see cref="ThresholdCount"/>
/// messages are uncovered, all but the most recent <see cref="TargetCount"/> are folded into the
/// summary and the watermark advances.</item>
/// </list>
/// The full history stays in the database — only what is sent to the model is reduced. The summary
/// is saved only if the watermark has not moved since it was read, so two summarizers (or a slow one
/// and a newer one) cannot overwrite each other; until a new summary lands, turns simply send the old
/// summary and a somewhat longer tail.
/// </summary>
public sealed class ChatHistoryReducer(
    IChatClient chatClient,
    IChatHistoryRepository chatHistoryRepository,
    ILogger<ChatHistoryReducer> logger) : IHistorySummarizer
{
    private const int TargetCount = 100;
    private const int ThresholdCount = 150;

    private const string SummarizationInstructions =
        """
        Summarize this MORK BORG game session so far. The summary replaces older messages, so it
        must let the Game Master continue seamlessly.

        Mechanical state (hit points, abilities, injuries, infection, omens, inventory, silver, time
        of day, miseries, adversary stats) is supplied separately and current on every turn. Do NOT
        restate any of it: a number written here goes stale and then contradicts the live state.

        Preserve:

        THE STORY SO FAR:
        - Character names and what they have done
        - Places visited and where the party is now
        - Recent encounters: who was fought or met, and how it ended
        - Important NPCs met and their relationships; promises, debts, and grudges
        - Current goals, quests, or destinations and unresolved plot hooks

        PRESERVE THE ATMOSPHERE:
        - The doom-laden, apocalyptic tone; the decaying world and mounting dread
        - Any omens, prophecies, or signs of the coming end; grim humour

        Discard repetitive description, resolved trivial beats, and rules chatter. Write the summary
        as terse narrative prose.
        """;

    private const string EpitaphInstructions =
        """
        A MORK BORG player character has died. Summarize their chronicle as a finished tale, in
        PAST TENSE and THIRD PERSON — the wretch is dead and stays dead. A new doomed soul will
        walk the same dying world; this summary is the history they inherit. Preserve:
        - The fallen wretch's name, their deeds, and how they died
        - NPCs met, their relationships and current dispositions
        - Locations visited and the state they were left in
        - Unresolved hooks, promises, quests, and threats still loose in the world
        - World events: miseries suffered, omens, the grinding approach of the end
        Do NOT carry over the dead character's hit points, inventory, or any second-person "you"
        phrasing. Write terse, doom-laden narrative prose.
        """;

    private const string RecapInstructions =
        """
        Write a 150-250 word "Previously on..." recap for the returning player of this MORK BORG
        campaign. Use the transcript for what happened and the final Current Game State message as
        authoritative when they disagree. Preserve major events, important NPCs and relationships,
        unresolved promises or threats, and the character's immediate situation. Write two to four
        short paragraphs of vivid, doom-laden narrative prose. Do not use headings, bullet points,
        raw statistics, rules language, or address the reader outside the fiction.
        """;

    /// <summary>What the model is sent for this chronicle: the stored summary (if any) followed by
    /// the messages it does not cover. Read-only.</summary>
    public async Task<IReadOnlyList<ChatMessage>> ComposeAsync(
        Guid chatSessionId, IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var stored = await chatHistoryRepository.GetSummary(chatSessionId, ct);
        return Compose(stored, history.Skip(stored?.CoveredCount ?? 0).ToList());
    }

    public async Task<bool> SummarizeIfDueAsync(Guid chatSessionId, CancellationToken ct)
    {
        var stored = await chatHistoryRepository.GetSummary(chatSessionId, ct);
        var covered = stored?.CoveredCount ?? 0;
        var history = await chatHistoryRepository.LoadSession(chatSessionId, ct) ?? [];
        var tail = history.Skip(covered).ToList();

        if (tail.Count <= ThresholdCount)
            return false;

        var olderCount = tail.Count - TargetCount;
        var toSummarize = new List<ChatMessage>(olderCount + 1);
        if (stored is not null)
            toSummarize.Add(SummaryMessage(stored.Text));
        toSummarize.AddRange(tail.Take(olderCount));

        var options = new ChatOptions { Instructions = SummarizationInstructions };
        var response = await chatClient.GetResponseAsync(toSummarize, options, ct);

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            // Nothing usable — the watermark stays put, so the next turn's pass retries.
            logger.LogWarning("History summarization returned empty for session {SessionId}", chatSessionId);
            return false;
        }

        var updated = new ChatSummary(response.Text, covered + olderCount);
        if (!await chatHistoryRepository.TrySaveSummary(chatSessionId, updated, expectedCoveredCount: covered, ct))
        {
            logger.LogInformation(
                "Summary for session {SessionId} was superseded while it was being written; discarded",
                chatSessionId);
            return false;
        }

        logger.LogInformation(
            "Rolled summary forward — covered {Covered} of {Total} messages",
            updated.CoveredCount, history.Count);
        return true;
    }

    /// <summary>
    /// Compacts a dead wretch's chronicle into a past-tense epitaph and seeds it as the successor
    /// chronicle's summary (coveredCount 0 — it precedes all of the new session's messages, which
    /// Compose then injects every turn). Best-effort: burial must never fail on a summarizer hiccup,
    /// because the world's hard state lives on the campaign and is re-injected every turn regardless.
    /// </summary>
    public async Task<bool> SeedEpitaphAsync(Guid fallenChronicleId, Guid newChronicleId, CancellationToken ct)
    {
        try
        {
            var history = await chatHistoryRepository.LoadSession(fallenChronicleId, ct);
            if (history is null || history.Count == 0)
                return false;

            var stored = await chatHistoryRepository.GetSummary(fallenChronicleId, ct);
            var toSummarize = new List<ChatMessage>();
            if (stored is not null)
                toSummarize.Add(SummaryMessage(stored.Text));
            toSummarize.AddRange(history.Skip(stored?.CoveredCount ?? 0));

            var options = new ChatOptions { Instructions = EpitaphInstructions };
            var response = await chatClient.GetResponseAsync(toSummarize, options, ct);
            if (string.IsNullOrWhiteSpace(response.Text))
            {
                logger.LogWarning("Epitaph summarization returned empty for chronicle {ChronicleId}", fallenChronicleId);
                return false;
            }

            await chatHistoryRepository.SaveSummary(newChronicleId, new ChatSummary(response.Text, 0), ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Epitaph summarization failed for chronicle {ChronicleId}", fallenChronicleId);
            return false;
        }
    }

    public async Task<string?> CreateRecapAsync(Guid chatSessionId, string currentState, CancellationToken ct)
    {
        try
        {
            var history = await chatHistoryRepository.LoadSession(chatSessionId, ct);
            if (history is null || history.Count == 0)
                return null;

            var stored = await chatHistoryRepository.GetSummary(chatSessionId, ct);
            var source = new List<ChatMessage>();
            if (stored is not null)
                source.Add(SummaryMessage(stored.Text));
            source.AddRange(history.Skip(stored?.CoveredCount ?? 0));
            source.Add(new ChatMessage(ChatRole.System, $"[Current Game State]\n{currentState}"));

            var response = await chatClient.GetResponseAsync(
                source,
                new ChatOptions { Instructions = RecapInstructions },
                ct);

            return string.IsNullOrWhiteSpace(response.Text) ? null : response.Text.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Recap generation failed for chronicle {ChronicleId}", chatSessionId);
            return null;
        }
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
