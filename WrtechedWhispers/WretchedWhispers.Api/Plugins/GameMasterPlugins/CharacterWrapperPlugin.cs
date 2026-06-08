using System.ComponentModel;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Wraps CharacterPlugin to auto-fill characterId from SessionContext and add guardrails.
/// The model never sees GUID parameters -- IDs are resolved from session state.
/// </summary>
[Description("Interact with the player character: create, challenge, manage inventory, improve or degrade abilities, handle infection, buy items, and cast scrolls.")]
public sealed class CharacterWrapperPlugin(
    ICharacterOperations inner,
    SessionContext sessionContext,
    ICampaignsRepository campaignsRepository)
{
    private Guid RequireCharacterId() =>
        sessionContext.CharacterId
        ?? throw new InvalidOperationException("No character exists yet -- call CreateCharacter first.");

    [Description("Create a new character with starting stats and gear")]
    public async Task<CharacterDto> CreateCharacter(
        [Description("Character name")] string name)
    {
        if (sessionContext.CharacterId is not null)
            throw new InvalidOperationException(
                "A character already exists for this session. You cannot create another one.");

        var result = await inner.CreateCharacter(name);
        sessionContext.SetCharacterId(result.Id);

        // Link the character to the session's campaign (single-player), but do NOT start it.
        // Starting the campaign is an explicit, model-visible step in the CampaignSetup stage
        // (StartCampaign tool) — creating a character must not silently advance the stage machine.
        if (sessionContext.CampaignId is { } campaignId)
        {
            var campaign = await campaignsRepository.Get(campaignId);
            if (campaign is not null)
            {
                campaign.JoinGame(result.Id);
                await campaignsRepository.SaveCampaign(campaign);
            }
        }

        return result;
    }

    [Description("Challenge the character with an ability test against a specified difficulty rating")]
    public async Task<ChallengeOutcomeDto> ChallengeCharacter(
        [Description("Level of the challenge, the higher the number the harder. Usually 12 for normal.")]
        int challengeDr,
        [Description("Ability kind to use: 'Strength', 'Agility', 'Presence', 'Toughness'.")]
        AbilityKind abilityKind)
    {
        ToolGuard.InRange(challengeDr, 2, 20, nameof(challengeDr), "12 is a normal challenge");
        return await inner.ChallengeCharacter(RequireCharacterId(), challengeDr, abilityKind);
    }

    [Description("Add an item to the character's inventory")]
    public async Task<CharacterDto> AddItemToCharacterInventory(
        [Description("Description of the item to add")] string itemDescription,
        [Description("Whether the item is bulky and takes 2 inventory slots")] bool isBulky = false,
        [Description("Whether the item is consumed after one use")] bool isOneTimeUse = false,
        [Description("Quantity of the item to add")] int quantity = 1)
    {
        ToolGuard.Quantity(quantity, nameof(quantity));
        return await inner.AddItemToCharacterInventory(RequireCharacterId(), itemDescription, isBulky, isOneTimeUse, quantity);
    }

    [Description("Remove an item from the character's inventory")]
    public async Task<CharacterDto> RemoveItemFromCharacterInventory(
        [Description("Id of the inventory item to remove")] Guid itemId)
    {
        return await inner.RemoveItemFromCharacterInventory(RequireCharacterId(), itemId);
    }

    [Description("Improve the character's ability score by a specified amount")]
    public async Task<CharacterDto> ImproveCharacterAbility(
        [Description("The ability to improve: 'Strength', 'Agility', 'Presence', 'Toughness'")] AbilityKind abilityKind,
        [Description("The positive amount to improve the ability by")] int delta)
    {
        ToolGuard.Positive(delta, nameof(delta), "e.g. 1 or 2");
        return await inner.ImproveCharacterAbility(RequireCharacterId(), abilityKind, delta);
    }

    [Description("Degrade the character's ability score by a specified amount")]
    public async Task<CharacterDto> DegradeCharacterAbility(
        [Description("The ability to degrade: 'Strength', 'Agility', 'Presence', 'Toughness'")] AbilityKind abilityKind,
        [Description("The negative amount to degrade the ability by")] int delta)
    {
        ToolGuard.Negative(delta, nameof(delta), "e.g. -1");
        return await inner.DegradeCharacterAbility(RequireCharacterId(), abilityKind, delta);
    }

    [Description("Infect the character. Infection stops healing and causes daily damage.")]
    public async Task<CharacterDto> InfectCharacter()
    {
        return await inner.InfectCharacter(RequireCharacterId());
    }

    [Description("Cure the character's infection. Requires prayers, unclean rituals, or rare remedies.")]
    public async Task<CharacterDto> CureInfection()
    {
        return await inner.CureInfection(RequireCharacterId());
    }

    [Description("Buy an item for the character, deducting silver and adding the item to inventory")]
    public async Task<CharacterDto> BuyItem(
        [Description("Description of the item to buy")] string itemDescription,
        [Description("Cost of the item in silver")] int silverCost,
        [Description("Whether the item is bulky")] bool isBulky = false,
        [Description("Whether the item is consumed after one use")] bool isOneTimeUse = false,
        [Description("Quantity of the item to buy")] int quantity = 1)
    {
        ToolGuard.NonNegative(silverCost, nameof(silverCost));
        ToolGuard.Quantity(quantity, nameof(quantity));
        return await inner.BuyItem(RequireCharacterId(), itemDescription, silverCost, isBulky, isOneTimeUse, quantity);
    }

    [Description("Cast a scroll spell that the character possesses")]
    public async Task<CastOutcomeDto> CastScroll(
        [Description("Id of the scroll to cast")] Guid scrollId)
    {
        return await inner.CastScroll(RequireCharacterId(), scrollId);
    }
}
