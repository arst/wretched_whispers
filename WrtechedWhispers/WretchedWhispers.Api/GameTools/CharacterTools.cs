using System.ComponentModel;
using WretchedWhispers.Api.GameTools.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Api.GameTools;

/// <summary>
/// Player-character game-master tools. One class per aggregate: it auto-fills the character id from
/// <see cref="SessionContext"/> (the model never sees GUIDs), validates model-supplied arguments via
/// <see cref="ToolGuard"/>, then calls the domain directly and maps the result to a DTO.
/// </summary>
[Description("Interact with the player character: create, challenge, manage inventory, improve or degrade abilities, handle infection, buy items, and cast scrolls.")]
public sealed class CharacterTools(
    ICharactersRepository charactersRepository,
    CharacterCreationService characterCreationService,
    CharacterService characterService,
    Dice dice,
    SessionContext sessionContext,
    CampaignService campaignService)
{
    private Guid RequireCharacterId() =>
        sessionContext.CharacterId
        ?? throw new InvalidOperationException("No character exists yet -- call CreateCharacter first.");

    [Description("Create a new character with starting stats and gear")]
    [GameTool(SessionStage.CharacterCreation)]
    public async Task<CharacterDto> CreateCharacter(
        [Description("Character name")] string name)
    {
        if (sessionContext.CharacterId is not null)
            throw new InvalidOperationException(
                "A character already exists for this session. You cannot create another one.");

        var character = await characterCreationService.Create(name);
        await charactersRepository.Save(character);
        sessionContext.SetCharacterId(character.Id);

        // Link the character to the session's campaign (single-player) via the domain service, but do
        // NOT start it. Starting the campaign is an explicit, model-visible step in the CampaignSetup
        // stage (StartCampaign tool) -- creating a character must not silently advance the stage machine.
        if (sessionContext.CampaignId is { } campaignId)
            await campaignService.JoinCampaign(campaignId, character.Id);

        return CreateCharacterDto(character);
    }

    [Description("Challenge the character with an ability test against a difficulty rating. On failure, the chosen consequence is applied automatically as rolled damage.")]
    [GameTool(SessionStage.Exploration)]
    public async Task<ChallengeOutcomeDto> ChallengeCharacter(
        [Description("Level of the challenge, the higher the number the harder. Usually 12 for normal.")]
        int challengeDr,
        [Description("Ability kind to use: 'Strength', 'Agility', 'Presence', 'Toughness'.")]
        AbilityKind abilityKind,
        [Description("What failure costs, chosen like a GM: 'None' (no harm), 'Minor' (d2 — scrapes), 'Serious' (d6 — a real wound), 'Deadly' (d10 — can kill). Match the fiction's stakes.")]
        ChallengeConsequence consequenceOnFailure = ChallengeConsequence.None)
    {
        ToolGuard.InRange(challengeDr, 2, 20, nameof(challengeDr), "12 is a normal challenge");
        var result = await characterService.ChallengePlayer(
            RequireCharacterId(), new Dr(challengeDr), abilityKind, consequenceOnFailure);
        return new ChallengeOutcomeDto(
            result.Outcome.IsSuccess, result.Outcome.Roll, result.Outcome.Modifier,
            result.Outcome.EffectiveDr, result.DamageTaken, result.IsDead);
    }

    [Description("Add an item to the character's inventory")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<CharacterDto> AddItemToCharacterInventory(
        [Description("Description of the item to add")] string itemDescription,
        [Description("Whether the item is bulky and takes 2 inventory slots")] bool isBulky = false,
        [Description("Whether the item is consumed after one use")] bool isOneTimeUse = false,
        [Description("Quantity of the item to add")] int quantity = 1)
    {
        ToolGuard.Quantity(quantity, nameof(quantity));
        var character = await RequireCharacter();
        character.AddItem(new InventoryItem(Guid.NewGuid(), itemDescription, isBulky, isOneTimeUse, quantity));
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Remove an item from the character's inventory")]
    [GameTool(SessionStage.Resolution)]
    public async Task<CharacterDto> RemoveItemFromCharacterInventory(
        [Description("Id of the inventory item to remove")] Guid itemId)
    {
        var character = await RequireCharacter();
        character.RemoveItem(itemId);
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Improve the character's ability score by a specified amount")]
    [GameTool(SessionStage.Resolution)]
    public async Task<CharacterDto> ImproveCharacterAbility(
        [Description("The ability to improve: 'Strength', 'Agility', 'Presence', 'Toughness'")] AbilityKind abilityKind,
        [Description("The positive amount to improve the ability by")] int delta)
    {
        ToolGuard.Positive(delta, nameof(delta), "e.g. 1 or 2");
        var character = await RequireCharacter();
        character.Improve(abilityKind, delta);
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Degrade the character's ability score by a specified amount")]
    [GameTool(SessionStage.Resolution)]
    public async Task<CharacterDto> DegradeCharacterAbility(
        [Description("The ability to degrade: 'Strength', 'Agility', 'Presence', 'Toughness'")] AbilityKind abilityKind,
        [Description("The negative amount to degrade the ability by")] int delta)
    {
        ToolGuard.Negative(delta, nameof(delta), "e.g. -1");
        var character = await RequireCharacter();
        character.Degrade(abilityKind, delta);
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Infect the character. Infection stops healing and causes daily damage.")]
    [GameTool(SessionStage.Resolution)]
    public async Task<CharacterDto> InfectCharacter()
    {
        var character = await RequireCharacter();
        character.Infect();
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Cure the character's infection. Requires prayers, unclean rituals, or rare remedies.")]
    [GameTool(SessionStage.Resolution)]
    public async Task<CharacterDto> CureInfection()
    {
        var character = await RequireCharacter();
        character.CureInfection();
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Buy an item for the character, deducting silver and adding the item to inventory")]
    [GameTool(SessionStage.Exploration)]
    public async Task<CharacterDto> BuyItem(
        [Description("Description of the item to buy")] string itemDescription,
        [Description("Cost of the item in silver")] int silverCost,
        [Description("Whether the item is bulky")] bool isBulky = false,
        [Description("Whether the item is consumed after one use")] bool isOneTimeUse = false,
        [Description("Quantity of the item to buy")] int quantity = 1)
    {
        ToolGuard.NonNegative(silverCost, nameof(silverCost));
        ToolGuard.Quantity(quantity, nameof(quantity));
        var character = await RequireCharacter();
        character.BuyItem(silverCost, new InventoryItem(Guid.NewGuid(), itemDescription, isBulky, isOneTimeUse, quantity));
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Cast a scroll spell that the character possesses")]
    [GameTool(SessionStage.Exploration)]
    public async Task<CastOutcomeDto> CastScroll(
        [Description("Id of the scroll to cast")] Guid scrollId)
    {
        var character = await RequireCharacter();
        var outcome = character.Cast(scrollId, dice);
        await charactersRepository.Save(character);

        return new CastOutcomeDto
        {
            Succeeded = outcome.Succeeded,
            Reason = outcome.Reason,
            PowerKey = outcome.PowerKey,
            HpLost = outcome.HpLost
        };
    }

    private async Task<Character> RequireCharacter()
    {
        var characterId = RequireCharacterId();
        return await charactersRepository.Get(characterId)
            ?? throw new InvalidOperationException($"Character with id {characterId} not found");
    }

    private static ArmorTierDto GetArmorTier(ArmorTier armorTier) => armorTier switch
    {
        ArmorTier.Heavy => ArmorTierDto.Heavy,
        ArmorTier.None => ArmorTierDto.None,
        ArmorTier.Light => ArmorTierDto.Light,
        ArmorTier.Medium => ArmorTierDto.Medium,
        _ => throw new ArgumentOutOfRangeException(nameof(armorTier), armorTier, null)
    };

    private static CharacterDto CreateCharacterDto(Character character) => new()
    {
        Id = character.Id,
        Name = character.Name,
        Silver = character.Silver,
        FoodDays = character.FoodDays,
        CurrentHp = character.Hp.Current,
        MaxHp = character.Hp.Max,
        Strength = character.Abilities.Strength.Modifier,
        Agility = character.Abilities.Agility.Modifier,
        Presence = character.Abilities.Presence.Modifier,
        Toughness = character.Abilities.Toughness.Modifier,
        WeaponKind = character.Weapon.Kind,
        ArmorTier = GetArmorTier(character.Armor.Tier),
        HasShield = character.Shield is not null,
        IsShieldBroken = character.Shield?.IsBroken ?? false,
        OmenCount = character.Omens.Count,
        PowersMax = character.Powers.MaxUses,
        PowersUsed = character.Powers.UsesRemaining,
        IsInfected = character.IsInfected,
        IsDizzyFromMagic = character.IsDizzyFromMagic,
        IsEncumbered = character.IsEncumbered,
        IsDead = character.IsDead,
        HasLostEye = character.HasLostEye,
        HasStabbedLung = character.HasStabbedLung,
        HasBrokenHand = character.HasBrokenHand,
        HasCrushedFoot = character.HasCrushedFoot,
        HasSeveredArm = character.HasSeveredArm,
        HasSmashedFace = character.HasSmashedFace,
        Scrolls = character.Scrolls.Select(s => new ScrollDto
        {
            Key = s.Description,
            School = s.School
        }).ToList(),
        Inventory = new InventoryDto
        {
            Container = character.Inventory.Container,
            MaxCapacity = character.Inventory.MaxCapacity,
            FreeSlots = character.Inventory.GetFreeSlots(),
            IsFull = character.Inventory.IsFull,
            Items = character.Inventory.InventoryItems.Select(i => new InventoryItemDto
            {
                Id = i.Id,
                Description = i.Description,
                IsBulky = i.IsBulky,
                IsOneTimeUse = i.IsOneTimeUse,
                Quantity = i.Quantity
            }).ToList()
        }
    };
}
