namespace WretchedWhispers.Infrastructure.Persistence.Entities;

/// <summary>
/// One row = one complete game-master turn, captured for offline error analysis / eval failure-mode
/// discovery. Denormalized on purpose: everything needed to render and label a turn lives here, so the
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

    /// <summary>Pre-tool prose the fabrication guardrail suppressed (the model's attempted fabrication), if any.</summary>
    public string? SuppressedNarrative { get; set; }

    /// <summary>The narrative actually shown to the player.</summary>
    public string Narrative { get; set; } = string.Empty;
}
