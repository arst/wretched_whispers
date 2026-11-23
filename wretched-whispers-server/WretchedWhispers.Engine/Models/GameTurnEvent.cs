using System.Text.Json.Serialization;

namespace WretchedWhispers.Engine.Models;

[JsonDerivedType(typeof(NarrativeChunk))]
[JsonDerivedType(typeof(ToolResult))]
[JsonDerivedType(typeof(TurnDelta))]
[JsonDerivedType(typeof(StateUpdate))]
[JsonDerivedType(typeof(TurnError))]
[JsonDerivedType(typeof(TurnDone))]
public abstract record GameTurnEvent(
    [property: JsonIgnore] string EventType);

public record NarrativeChunk(string Text) : GameTurnEvent("narrative");

/// <summary><paramref name="Arguments"/> (the call's raw JSON arguments) and <paramref name="Failed"/>
/// (the tool threw or was refused, so nothing changed) are for the server's own records — the turn
/// ledger — and are never sent to the client.</summary>
public record ToolResult(
    string Function,
    object Result,
    [property: JsonIgnore] string? Arguments = null,
    [property: JsonIgnore] bool Failed = false) : GameTurnEvent("tool_result");

public record StateUpdate(
    Guid? CampaignId,
    int CurrentDay,
    int CurrentHour,
    Guid? CharacterId,
    string? CharacterName,
    int? CharacterHp,
    int? CharacterMaxHp,
    int? CharacterStrength,
    int? CharacterAgility,
    int? CharacterPresence,
    int? CharacterToughness,
    string? CharacterWeapon,
    string? CharacterArmor,
    string[]? CharacterInventory,
    int? CharacterSilver,
    int MiseryCount,
    string Stage,
    string Status,
    bool HasLostEye,
    bool HasStabbedLung,
    bool HasBrokenHand,
    bool HasCrushedFoot,
    bool HasSeveredArm,
    bool HasSmashedFace,
    bool IsInfected,
    bool IsDizzyFromMagic,
    bool IsEncumbered,
    bool IsDead,
    string ArmorTier,
    bool HasShield,
    bool IsShieldBroken,
    bool WorldEnded,
    string? CurrentLocationName,
    int? CharacterOmens,
    string[]? CharacterScrolls,
    string[] MiseryPsalms,
    // Trailing and optional so the existing positional call sites keep compiling. Null for a classless
    // wretch, which is what the UI uses to show no class line at all.
    string? CharacterClass = null) : GameTurnEvent("state_update");

/// <summary>
/// The authoritative account of what THIS turn changed — a deterministic diff of the domain state
/// before and after the turn (see <see cref="Services.TurnDeltaMapper"/>). It is computed from
/// committed state, never written by the model, so it cannot be fabricated and — crucially — it
/// reports the ABSENCE of change too: a purchase the narration invented but no tool applied shows
/// SilverChange 0 / ItemsAdded []. The client renders this as the source of truth for the action's
/// outcome; the prose is colour beside it. Emitted only when a character already existed before the
/// turn (character creation is genesis, not a delta).
/// </summary>
public record TurnDelta(
    int SilverChange,
    int HpChange,
    string[] ItemsAdded,
    string[] ItemsRemoved,
    int HoursElapsed,
    int StrengthChange,
    int AgilityChange,
    int PresenceChange,
    int ToughnessChange,
    int MiseryChange,
    string[] NewAfflictions,
    bool Died,
    bool WorldEnded) : GameTurnEvent("turn_delta")
{
    /// <summary>True when the turn changed nothing the ledger tracks — the tell-tale of a narration
    /// that claimed an outcome no tool actually applied.</summary>
    [JsonIgnore]
    public bool IsNoOp =>
        SilverChange == 0 && HpChange == 0 && ItemsAdded.Length == 0 && ItemsRemoved.Length == 0 &&
        HoursElapsed == 0 && StrengthChange == 0 && AgilityChange == 0 && PresenceChange == 0 &&
        ToughnessChange == 0 && MiseryChange == 0 && NewAfflictions.Length == 0 && !Died && !WorldEnded;
}

/// <summary><paramref name="Retryable"/>: the failure was transient and the turn changed nothing, so
/// running it again is safe. A hint for the <see cref="Services.TurnWorker"/>; never sent to the client.</summary>
public record TurnError(string Message, [property: JsonIgnore] bool Retryable = false) : GameTurnEvent("error");

public record TurnDone() : GameTurnEvent("done");

/// <summary>A single tool invocation the model made this turn, with its raw JSON arguments.</summary>
public record ToolCallTrace(string Name, string? Arguments);

/// <summary>
/// Out-of-band capture event emitted LAST by <see cref="Services.AgentExecutor"/> so the
/// <see cref="Services.TurnCoordinator"/> can persist a full turn trace (tool-call arguments, model,
/// prompt and usage — none of which is exposed by the player-facing events). Deliberately NOT
/// registered as a <c>[JsonDerivedType]</c>: it must never be written to the SSE stream. The
/// TurnCoordinator captures it and drops it; other consumers ignore it.
/// </summary>
public record AgentTrace(
    IReadOnlyList<ToolCallTrace> ToolCalls,
    // Comparability: which model and prompt produced the turn, what it cost, and how many model
    // requests it took.
    string? ModelId = null,
    string? PromptHash = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    int Iterations = 0,
    IReadOnlyList<ModelCall>? ModelCalls = null) : GameTurnEvent("agent_trace");

/// <summary>
/// One model request of a turn (a turn makes one per tool round): which model answered, what it
/// cost, and how long it took. <paramref name="CachedInputTokens"/> is the part of
/// <paramref name="InputTokens"/> the provider served from its prompt cache;
/// <paramref name="ReasoningTokens"/> is the part of <paramref name="OutputTokens"/> spent on
/// hidden reasoning. <paramref name="FirstOutputMs"/> is the wait for the first text or tool call.
/// </summary>
public record ModelCall(
    string? ModelId,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long DurationMs,
    long? FirstOutputMs,
    string? FinishReason);

/// <summary>
/// Out-of-band capture event emitted FIRST by <see cref="Services.AgentExecutor"/>: what the turn
/// sends the model — instructions, messages and tool declarations — as JSON, for the turn trace.
/// First rather than with <see cref="AgentTrace"/>, so a turn that fails mid-run has still recorded
/// its input. Like <see cref="AgentTrace"/> it is not a <c>[JsonDerivedType]</c> and never reaches
/// the client.
/// </summary>
public record ModelRequest(string Json) : GameTurnEvent("model_request");
