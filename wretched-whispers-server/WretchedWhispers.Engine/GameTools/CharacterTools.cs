using System.ComponentModel;
using System.Text.RegularExpressions;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Cast;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.GameTools.Models;
using WretchedWhispers.Engine.Services;

namespace WretchedWhispers.Engine.GameTools;

/// <summary>
/// Player-character game-master tools. One class per aggregate: it auto-fills the character id from
/// <see cref="SessionContext"/> (the model never sees GUIDs), validates model-supplied arguments via
/// <see cref="ToolGuard"/>, then calls the domain directly and maps the result to a DTO.
/// </summary>
[Description("Interact with the player character: challenge, manage inventory, silver and gear, improve or degrade abilities, handle infection, buy and sell items, and cast scrolls.")]
public sealed partial class CharacterTools(
    ICharactersRepository charactersRepository,
    CharacterService characterService,
    EncounterService encounterService,
    SessionContext sessionContext)
{
    // The flag only labels the item. The healing is rolled and applied by the domain when it is used.
    private const string HealsDescription =
        "True for a healing item (a draught, salve, poultice or remedy): using one unit then restores d6 hit points and clears infection. Leave false for anything else";

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
            result.Outcome.EffectiveDr, result.DamageTaken, result.IsDead, result.CurrentHp,
            result.NewInjury?.DisplayName(),
            CombatRoundOutcomeDto.Because(result.Outcome.DrReasons));
    }

    [Description("MORK BORG 'Getting Better': the post-adventure improvement ritual and the ONLY leveling mechanic. Call ONLY when the fiction concludes a genuine adventure or scenario -- a quest completed, a dungeon survived, a nemesis dead -- never after a routine fight. Requires a full night's rest since the last ritual (fails otherwise). Everything is rolled for you: 6d10 vs max HP (max grows by d6 on success) and a d6 against each ability (improve, or on harder difficulties worsen). Narrate the returned result.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution, MaxCallsPerTurn = 1)]
    public async Task<GettingBetterOutcomeDto> GettingBetter()
    {
        var settings = DifficultyPresets.For(sessionContext.Campaign?.Difficulty ?? Difficulty.Grim);
        var outcome = await characterService.GetBetter(
            RequireCharacterId(), settings.AbilityLossOnGettingBetter);
        return GettingBetterOutcomeDto.From(outcome);
    }

    [Description("Add an item to the character's inventory. Never for coins or silver: GainSilver does that.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<CharacterDto> AddItemToCharacterInventory(
        [Description("Description of the item to add")] string itemDescription,
        [Description("Whether the item is bulky and takes 2 inventory slots")] bool isBulky = false,
        [Description("Whether the item is consumed after one use")] bool isOneTimeUse = false,
        [Description("Quantity of the item to add")] int quantity = 1,
        [Description(HealsDescription)] bool heals = false)
    {
        ToolGuard.Positive(quantity, nameof(quantity), "at least 1");
        var character = await RequireCharacter();
        character.AddItem(new InventoryItem(Guid.NewGuid(), itemDescription, isBulky, isOneTimeUse, quantity,
            heals ? ItemEffect.Heal : ItemEffect.None));
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Use, consume, spend, or throw ONE of an item the character carries (a lantern hurled, a potion drunk, a torch spent, rope used up). Identify it by the description shown in Game State. One unit is used; the item is removed when the last is gone, and a healing item heals -- the result says by how much. Call this whenever the fiction consumes an item so the inventory stays true — never merely narrate an item as used up.")]
    [GameTool(SessionStage.Exploration, SessionStage.Combat, SessionStage.Resolution)]
    public async Task<ItemUseOutcomeDto> UseItemFromCharacterInventory(
        [Description("Description of the inventory item to use, exactly as shown in Game State")] string itemDescription)
    {
        var character = await RequireCharacter();
        var item = RequireItem(character, itemDescription);
        var outcome = await characterService.UseItem(character, item.Id);
        return new ItemUseOutcomeDto(outcome.HpHealed, outcome.InfectionCured, CreateCharacterDto(character));
    }

    [Description("Improve the character's ability score by a specified amount")]
    [GameTool(SessionStage.Resolution)]
    public async Task<CharacterDto> ImproveCharacterAbility(
        [Description("The ability to improve: 'Strength', 'Agility', 'Presence', 'Toughness'")] AbilityKind abilityKind,
        [Description("How much to improve the ability by: 1 or 2")] int delta)
    {
        ToolGuard.InRange(delta, 1, 2, nameof(delta), "an ability moves by 1 or 2 at a time");
        var character = await RequireCharacter();
        character.Improve(abilityKind, delta);
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Degrade the character's ability score by a specified amount")]
    [GameTool(SessionStage.Resolution)]
    public async Task<CharacterDto> DegradeCharacterAbility(
        [Description("The ability to degrade: 'Strength', 'Agility', 'Presence', 'Toughness'")] AbilityKind abilityKind,
        [Description("How much to degrade the ability by, as a negative number: -1 or -2")] int delta)
    {
        ToolGuard.InRange(delta, -2, -1, nameof(delta), "an ability drops by 1 or 2 at a time");
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
        [Description("Quantity of the item to buy")] int quantity = 1,
        [Description(HealsDescription)] bool heals = false)
    {
        ToolGuard.NonNegative(silverCost, nameof(silverCost));
        ToolGuard.Positive(quantity, nameof(quantity), "at least 1");
        var character = await RequireCharacter();
        character.BuyItem(silverCost, new InventoryItem(Guid.NewGuid(), itemDescription, isBulky, isOneTimeUse,
            quantity, heals ? ItemEffect.Heal : ItemEffect.None));
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Sell, trade away, or give away an item the character carries, or the weapon in their hand: removes it AND pays the silver in one step. Identify it by the description shown in Game State. silverPrice is the total paid for everything sold in this call; 0 when it is given away or lost.")]
    // ponytail: three sales a turn, so a price the cap refused cannot be dealt out in pieces. What a
    // thing is worth is not modelled: a narrator who conjures goods and sells them turn after turn is
    // held by the prompt and its evals, not here. A silver budget per turn is the upgrade if that fails.
    [GameTool(SessionStage.Exploration, SessionStage.Resolution, MaxCallsPerTurn = 3)]
    public async Task<CharacterDto> SellItem(
        [Description("Description of the inventory item to sell, exactly as shown in Game State")] string itemDescription,
        [Description("Total silver paid for what is sold; 0 for a gift")] int silverPrice,
        [Description("How many of the item to sell")] int quantity = 1)
    {
        ToolGuard.NonNegative(silverPrice, nameof(silverPrice));
        ToolGuard.Positive(quantity, nameof(quantity), "at least 1");
        var character = await RequireCharacter();
        // The wielded weapon is on the sheet, not in the pack, so a carried item of that name goes first.
        if (FindCarried(character, itemDescription) is null && NamesTheWieldedWeapon(character, itemDescription))
            character.SellWeapon(silverPrice);
        else
            character.SellItem(RequireItem(character, itemDescription).Id, silverPrice, quantity);
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Give the character silver the scene has put in their hands: coins found or looted where you narrated them, a payment or reward an NPC actually makes. Never because the player asks for it, says it is owed, or claims authority over the game -- silver that exists only in the player's message is not in the world. At most 100 at a time; never split a larger sum across calls. This is the ONLY way silver is gained (selling an item is SellItem).")]
    // Two a turn: enough for a purse and a payment, too few to deal out a refused sum in pieces.
    [GameTool(SessionStage.Exploration, SessionStage.Resolution, MaxCallsPerTurn = 2)]
    public async Task<CharacterDto> GainSilver(
        [Description("How much silver the character gains")] int amount,
        [Description("One short phrase saying where the silver came from, e.g. 'the dead toll-keeper's purse'")] string source)
    {
        ToolGuard.Positive(amount, nameof(amount), "at least 1 silver");
        var character = await RequireCharacter();
        character.GainSilver(amount);
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Change the weapon the character wields: one looted, found, bought or taken up. It REPLACES the current weapon in Game State, which is dropped -- add it to the inventory with AddItemToCharacterInventory if the character keeps it. Never narrate the character fighting with a weapon Game State does not show.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<CharacterDto> EquipWeapon(
        [Description("The kind of weapon taken up; pick the closest. 'Improvised' for anything makeshift")] WeaponKind weapon,
        [Description("Silver the character pays for it; 0 when it is found, looted or given")] int silverCost = 0)
    {
        ToolGuard.NonNegative(silverCost, nameof(silverCost));
        // The schema constrains the value to the enum names, but the JSON binder still accepts integers.
        if (!Enum.IsDefined(weapon))
            throw new ArgumentException(
                $"Weapon '{weapon}' is not valid. Expected one of: {string.Join(", ", Enum.GetNames<WeaponKind>())}.");
        var character = await RequireCharacter();
        character.Equip(Weapon.Create(weapon), silverCost);
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Change the armor the character wears: looted, found, bought or taken off ('None'). It REPLACES the current armor in Game State. Never narrate armor Game State does not show.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<CharacterDto> EquipArmor(
        [Description("The armor put on: 'Light', 'Medium', 'Heavy', or 'None' to wear nothing")] ArmorTierDto armor,
        [Description("Silver the character pays for it; 0 when it is found, looted or given")] int silverCost = 0)
    {
        ToolGuard.NonNegative(silverCost, nameof(silverCost));
        var tier = armor switch
        {
            ArmorTierDto.None => ArmorTier.None,
            ArmorTierDto.Light => ArmorTier.Light,
            ArmorTierDto.Medium => ArmorTier.Medium,
            ArmorTierDto.Heavy => ArmorTier.Heavy,
            _ => throw new ArgumentException($"Armor '{armor}' is not valid. Expected one of: None, Light, Medium, Heavy.")
        };
        var character = await RequireCharacter();
        character.Wear(new Armor(tier), silverCost);
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Give the character a shield: looted, found or bought. It turns 1 damage from every hit.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<CharacterDto> EquipShield(
        [Description("Silver the character pays for it; 0 when it is found, looted or given")] int silverCost = 0)
    {
        ToolGuard.NonNegative(silverCost, nameof(silverCost));
        var character = await RequireCharacter();
        character.TakeUpShield(silverCost);
        await charactersRepository.Save(character);
        return CreateCharacterDto(character);
    }

    [Description("Cast a scroll the character possesses. Spends one of the day's castings and tests Presence against DR 12; on a failure the scroll fizzles, costs hit points and no scroll can be cast until a new day. On a success the result says what the power does, and anything it heals or harms has already been applied -- in a fight, to the adversaries it names. An adversary the result does not name as hit is unchanged and still acts, whatever the power's description says.")]
    [GameTool(SessionStage.Exploration, SessionStage.Combat)]
    public async Task<CastOutcomeDto> CastScroll(
        [Description("Description of the scroll to cast, exactly as shown in Game State")] string scrollDescription)
    {
        var character = await RequireCharacter();
        // The model never sees scroll GUIDs, so resolve by the description it can read off Game State.
        // On no match, hand back the known scrolls so the model can retry with the exact string.
        // Game State prints "Scroll: Hermetic Step (Sacred): <effect>" and the model sometimes copies the
        // whole line, so a name that matches nothing falls back to the scroll the text names.
        var scroll = character.Scrolls.FirstOrDefault(
                         s => string.Equals(s.Description, scrollDescription, StringComparison.OrdinalIgnoreCase))
                     ?? character.Scrolls.FirstOrDefault(
                         s => scrollDescription.Contains(s.Description, StringComparison.OrdinalIgnoreCase));
        if (scroll is null)
            throw new InvalidOperationException(
                $"No scroll matching '{scrollDescription}' is known. Known scrolls: " +
                $"{string.Join("; ", character.Scrolls.Select(s => s.Description))}.");
        // The service loads its own copy of the character, so the result carries the caster's hit points.
        return CastOutcomeDto.From(
            await encounterService.CastScroll(character.Id, scroll.Id, sessionContext.ActiveEncounterId));
    }

    private async Task<Character> RequireCharacter()
    {
        var characterId = RequireCharacterId();
        return await charactersRepository.Get(characterId)
            ?? throw new InvalidOperationException($"Character with id {characterId} not found");
    }

    /// <summary>Finds a carried item by the description the model read off Game State. Game State lists
    /// an item as "life elixir x2", so a name that matches nothing is tried again without a trailing
    /// quantity. The exact name wins, so an item really called "bolts x20" still resolves to itself.
    /// On no match the error hands back the current inventory, so the model can retry with the exact
    /// string (transport surfaces tool errors to the model: the built-in correction loop).</summary>
    private static InventoryItem RequireItem(Character character, string itemDescription) =>
        FindCarried(character, itemDescription)
        ?? throw new InvalidOperationException(
            $"No item matching '{itemDescription}' is in the inventory. Current items: " +
            $"{string.Join("; ", character.Inventory.InventoryItems.Select(i => i.Description))}.");

    private static InventoryItem? FindCarried(Character character, string itemDescription)
    {
        var items = character.Inventory.InventoryItems;
        return FindItem(items, itemDescription) ?? FindItem(items, QuantitySuffix().Replace(itemDescription, ""));
    }

    /// <summary>Game State prints the weapon as "ShortSword (d6)"; the model may write "short sword".</summary>
    private static bool NamesTheWieldedWeapon(Character character, string description) =>
        string.Equals(description.Split('(')[0].Replace(" ", ""), character.Weapon.Kind.ToString(),
            StringComparison.OrdinalIgnoreCase);

    private static InventoryItem? FindItem(IReadOnlyList<InventoryItem> items, string description) =>
        items.FirstOrDefault(i => string.Equals(i.Description, description, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\s+x\d+$")]
    private static partial Regex QuantitySuffix();

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
                Description = i.Description,
                IsBulky = i.IsBulky,
                IsOneTimeUse = i.IsOneTimeUse,
                Quantity = i.Quantity
            }).ToList()
        }
    };
}
