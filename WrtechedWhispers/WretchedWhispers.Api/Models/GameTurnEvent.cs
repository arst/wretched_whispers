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
