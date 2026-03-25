using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Wraps EncounterPlugin to auto-fill encounterId/characterId from SessionContext.
/// The model never sees encounter or player GUID parameters -- only adversary selection IDs remain.
/// </summary>
[Description("Manage encounters: create them, add adversaries, start combat, execute attacks, and end encounters.")]
public sealed class EncounterWrapperPlugin(IEncounterOperations inner, SessionContext sessionContext)
{
    private Guid RequireEncounterId() =>
        sessionContext.ActiveEncounterId
        ?? throw new InvalidOperationException("No active encounter -- call CreateEncounter first.");

    private Guid RequireCharacterId() =>
        sessionContext.CharacterId
        ?? throw new InvalidOperationException("No character exists yet -- call CreateCharacter first.");

    [KernelFunction]
    [Description("Create a new encounter with the specified name, description, and initial type")]
    public async Task<EncounterDto> CreateEncounter(
        [Description("The name of the encounter")] string name,
        [Description("A description of the encounter setting or narrative context")] string description,
        [Description("Initial type: Friendly, Hostile, or Unknown")] string initialEncounterType)
    {
        var result = await inner.CreateEncounter(name, description, initialEncounterType);
        sessionContext.SetActiveEncounterId(result.Id);
        return result;
    }

    [KernelFunction]
    [Description("Add an adversary to the current encounter")]
    public async Task<EncounterDto> AddAdversaryToEncounter(
        [Description("The adversary to add")] NewAdversaryDto adversary)
    {
        return await inner.AddAdversaryToEncounter(RequireEncounterId(), adversary);
    }

    [KernelFunction]
    [Description("Start the current encounter")]
    public async Task<EncounterDto> StartEncounter()
    {
        return await inner.StartEncounter(RequireEncounterId());
    }

    [KernelFunction]
    [Description("Execute an attack from an adversary against the player character")]
    public async Task<AdversaryAttackOutcomeDto> AttackPlayer(
        [Description("The identifier of the adversary performing the attack")] Guid attackingAdversaryId)
    {
        return await inner.AttackPlayer(RequireEncounterId(), attackingAdversaryId, RequireCharacterId());
    }

    [KernelFunction]
    [Description("Execute an attack from the player character against an adversary")]
    public async Task<CharacterAttackOutcomeDto> AttackAdversary(
        [Description("The identifier of the adversary being attacked")] Guid adversaryBeingAttackedId)
    {
        return await inner.AttackAdversary(RequireEncounterId(), RequireCharacterId(), adversaryBeingAttackedId);
    }

    [KernelFunction]
    [Description("End the current encounter")]
    public async Task<EncounterDto> EndEncounter()
    {
        return await inner.EndEncounter(RequireEncounterId());
    }
}
