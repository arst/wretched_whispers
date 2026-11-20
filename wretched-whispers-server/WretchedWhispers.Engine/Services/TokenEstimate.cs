using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// A rough, model-independent token count: about four characters per token for English prose. It is
/// only used to decide WHEN to compact context, where being off by a fifth is harmless; the real
/// usage of every turn is recorded in its trace (InputTokens) and is what to tune the budgets against.
/// </summary>
public static class TokenEstimate
{
    private const int CharsPerToken = 4;

    /// <summary>Per-message framing (role, separators) that the text itself does not account for.</summary>
    private const int MessageOverhead = 4;

    public static int For(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (text.Length + CharsPerToken - 1) / CharsPerToken;

    public static int For(ChatMessage message) => For(message.Text) + MessageOverhead;
}

/// <summary>
/// How much chat history a turn may send verbatim. When the messages not yet covered by the summary
/// exceed <see cref="TriggerTokens"/>, the older ones are folded into the summary and the most recent
/// <see cref="KeepTokens"/> worth stay word for word. The gap between the two is what keeps
/// summarizing occasional instead of every turn.
/// </summary>
public sealed record HistoryBudget(int TriggerTokens = 12_000, int KeepTokens = 8_000)
{
    public static HistoryBudget From(IConfiguration configuration)
    {
        var trigger = Math.Max(1_000, configuration.GetValue("GameSession:HistoryTriggerTokens", 12_000));
        var keep = configuration.GetValue("GameSession:HistoryKeepTokens", 8_000);
        // Keeping as much as (or more than) the trigger would summarize on every turn.
        return new HistoryBudget(trigger, Math.Clamp(keep, 500, trigger * 3 / 4));
    }
}
