using WretchedWhispers.Api.GameTools;
using WretchedWhispers.Api.GameTools.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins.Adapters;

/// <summary>
/// Adapts EncounterPlugin to IEncounterOperations.
/// </summary>
public sealed class EncounterPluginAdapter(EncounterPlugin inner) : IEncounterOperations
{
    public Task<EncounterDto> CreateEncounter(string name, string description, string initialEncounterType) =>
        inner.CreateEncounter(name, description, initialEncounterType);

    public Task<EncounterDto> AddAdversaryToEncounter(Guid encounterId, NewAdversaryDto adversary) =>
        inner.AddAdversaryToEncounter(encounterId, adversary);

    public Task<EncounterDto> StartEncounter(Guid encounterId) => inner.StartEncounter(encounterId);

    public Task<AdversaryAttackOutcomeDto> AttackPlayer(Guid encounterId, Guid attackingAdversaryId, Guid playerBeingAttackedId) =>
        inner.AttackPlayer(encounterId, attackingAdversaryId, playerBeingAttackedId);

    public Task<CharacterAttackOutcomeDto> AttackAdversary(Guid encounterId, Guid attackingPlayerId, Guid adversaryBeingAttackedId) =>
        inner.AttackAdversary(encounterId, attackingPlayerId, adversaryBeingAttackedId);

    public Task<EncounterDto> EndEncounter(Guid encounterId) => inner.EndEncounter(encounterId);
}
