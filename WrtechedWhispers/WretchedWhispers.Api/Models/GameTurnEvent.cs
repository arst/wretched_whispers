using System.Text.Json.Serialization;

namespace WretchedWhispers.Api.Models;

[JsonDerivedType(typeof(NarrativeChunk))]
[JsonDerivedType(typeof(ToolResult))]
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
