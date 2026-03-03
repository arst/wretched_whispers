using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Semantic;

[Description(
    "Allow interacting with characters, such as creating a new character or viewing character stats, performing actions on characters, and more.")]
public sealed class CharacterPlugin(
    ICharactersRepository charactersRepository,
    CharacterCreationService characterCreationService,
    CharacterService characterService,
    Dice dice)
{
    [KernelFunction]
    [Description("Create a new character with starting stats and gear")]
    public async Task<CharacterDto> CreateCharacter([Description("Character name")] string name)
    {
        var character = await characterCreationService.Create(name);
        await charactersRepository.Save(character);

        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description("Challenge a character with an ability test against a specified difficulty rating")]
    public async Task<ChallengeOutcomeDto> ChallengeCharacter(
        [Description("Id of the character to challenge")]
        Guid characterId,
        [Description(
            "Level of the challenge, the higher the number the harder the challenge. Usually equal to 12 for a normal challenge.")]
        int challengeDr,
        [Description("Ability kind to use for the challenge, one of: 'Strength', 'Agility', 'Presence', 'Toughness'.")]
        AbilityKind abilityKind)
    {
        var outcome = await characterService.ChallengePlayer(characterId, new Dr(challengeDr), abilityKind);
        return new ChallengeOutcomeDto(outcome.IsSuccess);
    }

    [KernelFunction]
    [Description("Add an item to a character's inventory")]
    public async Task<CharacterDto> AddItemToCharacterInventory(
        [Description("Id of the character to add item to")]
        Guid characterId,
        [Description("Description of the item to add")]
        string itemDescription,
        [Description("Whether the item is bulky and takes 2 inventory slots instead of 1")]
        bool isBulky = false,
        [Description("Whether the item is consumed after one use")]
        bool isOneTimeUse = false,
        [Description("Quantity of the item to add")]
        int quantity = 1)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

        var newItem = new InventoryItem(Guid.NewGuid(), itemDescription, isBulky, isOneTimeUse, quantity);
        character.AddItem(newItem);

        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description("Remove an item from a character's inventory")]
    public async Task<CharacterDto> RemoveItemFromCharacterInventory(
        [Description("Id of the character to remove item from")]
        Guid characterId,
        [Description("Id of the inventory item to remove")]
        Guid itemId)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

        character.RemoveItem(itemId);

        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description(
        "Consume one unit of an item from a character's inventory, reducing its quantity by 1 or removing it completely if quantity reaches 0")]
    public async Task<CharacterDto> ConsumeItemFromCharacterInventory(
        [Description("Id of the character whose item to consume")]
        Guid characterId,
        [Description("Id of the inventory item to consume")]
        Guid itemId)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

        var wasConsumed = character.ConsumeItem(itemId);
        if (!wasConsumed) throw new InvalidOperationException($"Item with id {itemId} has no quantity left to consume");

        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description("Replenish an existing item in a character's inventory by adding to its quantity")]
    public async Task<CharacterDto> ReplenishItemInCharacterInventory(
        [Description("Id of the character whose item to replenish")]
        Guid characterId,
        [Description("Id of the inventory item to replenish")]
        Guid itemId,
        [Description("Amount to add to the item's quantity")]
        int amount = 1)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

        character.ReplenishItem(itemId, amount);

        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description(
        "Improve a character's ability score by a specified amount, increasing their effectiveness in that ability")]
    public async Task<CharacterDto> ImproveCharacterAbility(
        [Description("Id of the character whose ability to improve")]
        Guid characterId,
        [Description("The ability to improve - one of: 'Strength', 'Agility', 'Presence', 'Toughness'")]
        AbilityKind abilityKind,
        [Description("The positive amount to improve the ability by")]
        int delta)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

        if (delta <= 0) throw new InvalidOperationException("Improvement delta must be positive");

        character.Improve(abilityKind, delta);

        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description(
        "Degrade a character's ability score by a specified amount, reducing their effectiveness in that ability")]
    public async Task<CharacterDto> DegradeCharacterAbility(
        [Description("Id of the character whose ability to degrade")]
        Guid characterId,
        [Description("The ability to degrade - one of: 'Strength', 'Agility', 'Presence', 'Toughness'")]
        AbilityKind abilityKind,
        [Description("The negative amount to degrade the ability by (must be negative)")]
        int delta)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

        if (delta >= 0) throw new InvalidOperationException("Degradation delta must be negative");

        character.Degrade(abilityKind, delta);

        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description(
        "Infect a character. Common causes: falling to 0 HP with untreated festering wounds, exposure to rot/corruption/sewage/blighted lands, failed Toughness/Presence saves after nasty injuries, bites/claws from diseased creatures (vermin/undead/horrors). Infection stops healing and causes daily damage.")]
    public async Task<CharacterDto> InfectCharacter(
        [Description("Id of the character to infect")]
        Guid characterId)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

        character.Infect();

        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description(
        "Cure a character's infection. No natural recovery - requires prayers, unclean rituals, or rare remedies. Common methods: sacred/occult healing (priest's prayer, esoteric ritual, unholy pact), rare ingredients (boiled crow's tongue, powdered saint's bone, bizarre tonics), or NPC healer (surgeon/witch) often for a terrible price.")]
    public async Task<CharacterDto> CureInfection(
        [Description("Id of the character to cure infection from")]
        Guid characterId)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

        character.CureInfection();

        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description("Buy an item for a character, deducting silver and adding the item to inventory")]
    public async Task<CharacterDto> BuyItem(
        [Description("Id of the character buying the item")]
        Guid characterId,
        [Description("Description of the item to buy")]
        string itemDescription,
        [Description("Cost of the item in silver")]
        int silverCost,
        [Description("Whether the item is bulky and takes 2 inventory slots instead of 1")]
        bool isBulky = false,
        [Description("Whether the item is consumed after one use")]
        bool isOneTimeUse = false,
        [Description("Quantity of the item to buy")]
        int quantity = 1)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

        var newItem = new InventoryItem(Guid.NewGuid(), itemDescription, isBulky, isOneTimeUse, quantity);
        character.BuyItem(silverCost, newItem);

        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [KernelFunction]
    [Description(
        "Cast a scroll spell that the character possesses. Requires daily power uses and cannot be done if dizzy from prior magic failure, wearing heavy armor, or wielding two-handed weapons. Success casts the spell, failure causes HP loss and dizziness.")]
    public async Task<CastOutcomeDto> CastScroll(
        [Description("Id of the character casting the scroll")]
        Guid characterId,
        [Description("Id of the scroll to cast")]
        Guid scrollId)
    {
        var character = await charactersRepository.Get(characterId);
        if (character == null) throw new InvalidOperationException($"Character with id {characterId} not found");

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

    private static ArmorTierDto GetArmorTier(ArmorTier armorTier)
    {
        return armorTier switch
        {
            HeavyArmorTier => ArmorTierDto.Heavy,
            NoArmorTier => ArmorTierDto.None,
            LightArmorTier => ArmorTierDto.Light,
            MediumArmorTier => ArmorTierDto.Medium,
            _ => throw new ArgumentOutOfRangeException(nameof(armorTier), armorTier, null)
        };
    }

    private static CharacterDto CreateCharacterDto(Character character)
    {
        return new CharacterDto
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
}