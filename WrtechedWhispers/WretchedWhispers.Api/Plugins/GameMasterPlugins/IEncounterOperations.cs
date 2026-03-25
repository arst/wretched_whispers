using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Contract for encounter operations that wrapper plugins delegate to.
/// Implemented by EncounterPlugin via an adapter.
/// </summary>
public interface IEncounterOperations
{
    Task<EncounterDto> CreateEncounter(string name, string description, string initialEncounterType);
    Task<EncounterDto> AddAdversaryToEncounter(Guid encounterId, NewAdversaryDto adversary);
    Task<EncounterDto> StartEncounter(Guid encounterId);
    Task<AdversaryAttackOutcomeDto> AttackPlayer(Guid encounterId, Guid attackingAdversaryId, Guid playerBeingAttackedId);
    Task<CharacterAttackOutcomeDto> AttackAdversary(Guid encounterId, Guid attackingPlayerId, Guid adversaryBeingAttackedId);
    Task<EncounterDto> EndEncounter(Guid encounterId);
}
