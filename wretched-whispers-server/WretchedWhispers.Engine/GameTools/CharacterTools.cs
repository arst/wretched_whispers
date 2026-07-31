using System.ComponentModel;
using WretchedWhispers.Engine.GameTools.Models;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Engine.GameTools;

/// <summary>
/// Player-character game-master tools. One class per aggregate: it auto-fills the character id from
/// <see cref="SessionContext"/> (the model never sees GUIDs), validates model-supplied arguments via
/// <see cref="ToolGuard"/>, then calls the domain directly and maps the result to a DTO.
/// </summary>
[Description("Interact with the player character: challenge, manage inventory, improve or degrade abilities, handle infection, buy items, and cast scrolls.")]
public sealed class CharacterTools(
    ICharactersRepository charactersRepository,
    CharacterService characterService,
    Dice dice,
    SessionContext sessionContext)
{
    private Guid RequireCharacterId() =>
        sessionContext.CharacterId
        ?? throw new InvalidOperationException("No character exists for this session.");

    // There is deliberately no CreateCharacter tool. Name and class are player decisions collected by the
    // create-session and successor forms, and everything else is rolled by CharacterCreationService, so a
    // character always exists before the narrator's first turn. See SessionEndpoints.CreateSession.

    [Description("Challenge the character with an ability test against a difficulty rating. On failure, the chosen consequence is applied automatically as rolled damage.")]
    [GameTool(SessionStage.Exploration)]
    public async Task<ChallengeOutcomeDto> ChallengeCharacter(
        [Description("Level of the challenge, the higher the number the harder. Usually 12 for normal.")]
        int challengeDr,
        [Description("Ability kind to use: 'Strength', 'Agility', 'Presence', 'Toughness'.")]
        AbilityKind abilityKind,
        [Description("What failure costs, chosen like a GM: 'None' (no harm), 'Minor' (scrapes), 'Serious' (a real wound), 'Deadly' (can kill). Follow the difficulty guidance in your instructions when choosing.")]
        ChallengeConsequence consequenceOnFailure = ChallengeConsequence.None,
        [Description("Spend one omen to lower this test's DR by 4 - COSTS one of the player's omens (see Omens in Game State). Fails if no omens remain. Only use when the player asks to spend an omen, or at a truly dramatic moment.")]
        bool spendOmenToLowerDr = false)
    {
        ToolGuard.InRange(challengeDr, 2, 20, nameof(challengeDr), "12 is a normal challenge");
        var settings = DifficultyPresets.For(sessionContext.Campaign?.Difficulty ?? Difficulty.Grim);
        var result = await characterService.ChallengePlayer(
            RequireCharacterId(), new Dr(challengeDr), abilityKind, settings, consequenceOnFailure,
            spendOmenToLowerDr);
        return new ChallengeOutcomeDto(
            result.Outcome.IsSuccess, result.Outcome.Roll, result.Outcome.Modifier,
            result.Outcome.Roll + result.Outcome.Modifier,
            result.Outcome.EffectiveDr, result.DamageTaken, result.IsDead, result.CurrentHp);
    }

    [Description("MORK BORG 'Getting Better': the post-adventure improvement ritual and the ONLY leveling mechanic. Call ONLY when the fiction concludes a genuine adventure or scenario -- a quest completed, a dungeon survived, a nemesis dead -- never after a routine fight. Requires a full night's rest since the last ritual (fails otherwise). The domain rolls everything: 6d10 vs max HP (max grows by d6 on success) and a d6 against each ability (improve, or on harder difficulties worsen). Narrate the returned result.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<GettingBetterOutcomeDto> GettingBetter()
    {
        var settings = DifficultyPresets.For(sessionContext.Campaign?.Difficulty ?? Difficulty.Grim);
        var outcome = await characterService.GetBetter(
            RequireCharacterId(), settings.AbilityLossOnGettingBetter);
        return GettingBetterOutcomeDto.From(outcome);
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

    [Description("Use, consume, spend, or throw ONE of an item the character carries (a lantern hurled, a potion drunk, a torch spent, rope used up). Identify it by the description shown in Game State. One unit is used; the item is removed when the last is gone. Call this whenever the fiction consumes an item so the inventory stays true — never merely narrate an item as used up.")]
    [GameTool(SessionStage.Exploration, SessionStage.Combat, SessionStage.Resolution)]
    public async Task<CharacterDto> UseItemFromCharacterInventory(
        [Description("Description of the inventory item to use, exactly as shown in Game State")] string itemDescription)
    {
        var character = await RequireCharacter();
        // The model never sees item GUIDs, so resolve by the description it can read off Game State.
        // On no match, hand back the current inventory so the model can retry with the exact string
        // (transport surfaces tool errors to the model — the built-in correction loop).
        var item = character.Inventory.InventoryItems
            .FirstOrDefault(i => string.Equals(i.Description, itemDescription, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            throw new InvalidOperationException(
                $"No item matching '{itemDescription}' is in the inventory. Current items: " +
                $"{string.Join("; ", character.Inventory.InventoryItems.Select(i => i.Description))}.");
        character.ConsumeItem(item.Id);
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
    [GameTool(SessionStage.Exploration, SessionStage.Combat)]
    public async Task<CastOutcomeDto> CastScroll(
        [Description("Description of the scroll to cast, exactly as shown in Game State")] string scrollDescription)
    {
        var character = await RequireCharacter();
        // The model never sees scroll GUIDs, so resolve by the description it can read off Game State.
        // On no match, hand back the known scrolls so the model can retry with the exact string.
        var scroll = character.Scrolls.FirstOrDefault(
            s => string.Equals(s.Description, scrollDescription, StringComparison.OrdinalIgnoreCase));
        if (scroll is null)
            throw new InvalidOperationException(
                $"No scroll matching '{scrollDescription}' is known. Known scrolls: " +
                $"{string.Join("; ", character.Scrolls.Select(s => s.Description))}.");
        var outcome = character.Cast(scroll.Id, dice);
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
        PowersRemaining = character.Powers.UsesRemaining,
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
