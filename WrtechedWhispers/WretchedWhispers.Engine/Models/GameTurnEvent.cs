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

public record ToolResult(string Function, object Result) : GameTurnEvent("tool_result");

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
    bool WorldEnded) : GameTurnEvent("state_update");

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

public record TurnError(string Message) : GameTurnEvent("error");

public record TurnDone() : GameTurnEvent("done");

/// <summary>A single tool invocation the model made this turn, with its raw JSON arguments.</summary>
public record ToolCallTrace(string Name, string? Arguments);

/// <summary>
/// Out-of-band capture event emitted LAST by <see cref="Services.AgentExecutor"/> so the
/// <see cref="Services.TurnCoordinator"/> can persist a full turn trace (tool-call arguments + the
/// suppressed pre-tool prose — neither of which is exposed by the player-facing events). Deliberately
/// NOT registered as a <c>[JsonDerivedType]</c>: it must never be written to the SSE stream. The
/// TurnCoordinator captures it and drops it; other consumers ignore it.
/// </summary>
public record AgentTrace(
    IReadOnlyList<ToolCallTrace> ToolCalls,
    string? SuppressedNarrative) : GameTurnEvent("agent_trace");
