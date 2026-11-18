namespace WretchedWhispers.Infrastructure.Persistence.Entities;

/// <summary>
/// One row = one game-master turn attempt (committed or not), captured for offline error analysis /
/// eval failure-mode discovery. Denormalized on purpose: everything needed to render and label a turn lives here, so the
/// exporter never has to reconstruct state. The three *Json columns hold already-serialized JSON.
/// </summary>
public class TurnTraceEntity
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ChatSessionId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int OrderIndex { get; set; }

    public string PlayerMessage { get; set; } = string.Empty;

    /// <summary>Serialized state_update the model saw as input for this turn (its game-state snapshot).</summary>
    public string? GameStateJson { get; set; }

    /// <summary>JSON array of { name, arguments } — the tools the model called this turn.</summary>
    public string ToolCallsJson { get; set; } = "[]";

    /// <summary>JSON array of { name, result } — what those tools returned.</summary>
    public string ToolResultsJson { get; set; } = "[]";

    /// <summary>
    /// Serialized <c>TurnDelta</c> — the authoritative diff of committed state before vs. after this turn
    /// (silver/HP/item/time changes). Null for a turn with no character yet (character creation). This is
    /// the ground truth for eval labelling: it says what the turn ACTUALLY changed, independent of what the
    /// narrative claimed — so a fabricated outcome shows up as a narrative with an all-zero delta.
    /// </summary>
    public string? TurnDeltaJson { get; set; }

    /// <summary>Pre-tool prose the fabrication guardrail suppressed (the model's attempted fabrication), if any.</summary>
    public string? SuppressedNarrative { get; set; }

    /// <summary>The narrative actually shown to the player.</summary>
    public string Narrative { get; set; } = string.Empty;

    // ---- Coverage and comparability. All nullable: rows written before these columns existed have
    // none of them, and a failed turn may not have reached the point where a value is known. ----

    /// <summary>The durable turn request this trace belongs to (null for turns run outside the queue).</summary>
    public Guid? TurnId { get; set; }

    /// <summary>Which attempt at the turn produced this trace (1 = first). A reclaimed turn has several.</summary>
    public int? Attempt { get; set; }

    /// <summary>How the turn ended: committed, failed, empty, refused, or conflict (see <see cref="TurnTraceOutcome"/>).</summary>
    public string? Outcome { get; set; }

    /// <summary>Why a non-committed turn ended (exception type and message, or the refusal reason).</summary>
    public string? Error { get; set; }

    /// <summary>The model that served the turn, as reported by the provider.</summary>
    public string? ModelId { get; set; }

    /// <summary>SHA-256 (hex) of the composed system instructions — which prompt version produced this turn.</summary>
    public string? PromptHash { get; set; }

    /// <summary>JSON array of the tool names the model was offered this turn.</summary>
    public string? ToolsOffered { get; set; }

    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }

    /// <summary>Wall-clock time of the turn, from start to trace.</summary>
    public long? DurationMs { get; set; }

    /// <summary>How many model requests the turn made (one per tool round, plus any empty-response retry).</summary>
    public int? Iterations { get; set; }
}

/// <summary>The values of <see cref="TurnTraceEntity.Outcome"/>.</summary>
public static class TurnTraceOutcome
{
    public const string Committed = "committed";
    public const string Failed = "failed";
    public const string Empty = "empty";
    public const string Refused = "refused";
    public const string Conflict = "conflict";
}
