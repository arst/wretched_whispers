using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Wraps EncounterPlugin to auto-fill encounterId/characterId from SessionContext.
/// The model never sees encounter or player GUID parameters -- only adversary selection IDs remain.
/// </summary>
[Description("Manage encounters: create them, add adversaries, start combat, execute attacks, and end encounters.")]
public sealed class EncounterWrapperPlugin(
    IEncounterOperations inner,
    SessionContext sessionContext,
    ICampaignsRepository campaignsRepository)
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

        // Link encounter to campaign so SessionContextLoader finds it on next turn
        if (sessionContext.CampaignId is { } campaignId)
        {
            var campaign = await campaignsRepository.Get(campaignId);
            if (campaign is not null)
            {
                campaign.AddEncounter(result.Id);
                await campaignsRepository.SaveCampaign(campaign);
            }
        }

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
    [Description("A living adversary attacks the player character. The adversary is auto-selected.")]
    public async Task<AdversaryAttackOutcomeDto> AttackPlayer()
    {
        var encounter = sessionContext.ActiveEncounter
            ?? throw new InvalidOperationException("No active encounter.");
        var adversary = encounter.LivingAdversaries.FirstOrDefault()
            ?? throw new InvalidOperationException("No living adversaries remain.");
        return await inner.AttackPlayer(RequireEncounterId(), adversary.Id, RequireCharacterId());
    }

    [KernelFunction]
    [Description("The player character attacks an adversary by name.")]
    public async Task<CharacterAttackOutcomeDto> AttackAdversary(
        [Description("Name of the adversary to attack")] string adversaryName)
    {
        var encounter = sessionContext.ActiveEncounter
            ?? throw new InvalidOperationException("No active encounter.");
        var adversary = encounter.LivingAdversaries
            .FirstOrDefault(a => a.Name.Equals(adversaryName, StringComparison.OrdinalIgnoreCase))
            ?? encounter.LivingAdversaries.FirstOrDefault()
            ?? throw new InvalidOperationException("No living adversaries remain.");
        return await inner.AttackAdversary(RequireEncounterId(), RequireCharacterId(), adversary.Id);
    }

    [KernelFunction]
    [Description("End the current encounter")]
    public async Task<EncounterDto> EndEncounter()
    {
        return await inner.EndEncounter(RequireEncounterId());
    }
}
