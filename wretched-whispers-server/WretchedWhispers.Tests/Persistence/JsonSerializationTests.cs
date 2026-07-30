using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Infrastructure.Persistence.Serialization;

namespace WretchedWhispers.Tests.Persistence;

public class JsonSerializationTests : TestBase
{
    private readonly JsonSerializerOptions _options = AggregateJsonOptions.Create();

    [Fact]
    public void Character_RoundTrips_Class()
    {
        SetupDiceRolls(3);
        var character = ClassedCharacter(CharacterClass.CursedSkinwalker);

        var json = JsonSerializer.Serialize(character, _options);
        var deserialized = JsonSerializer.Deserialize<Character>(json, _options)!;

        Assert.Equal(CharacterClass.CursedSkinwalker, deserialized.Class);
    }

    /// <summary>Characters saved before classes existed have no "class" or "powerDie" keys at all. They must
    /// still load, as classless wretches on the original d4 power die -- there is no migration to fix them up,
    /// the Characters table is a single JSON blob.
    /// <para>
    /// The legacy shape is produced by deleting those keys rather than pasting a frozen blob, so this keeps
    /// testing absent-key handling as the rest of the character schema moves.
    /// </para></summary>
    [Fact]
    public void Character_WithoutClassKeys_DeserializesAsClasslessOnAD4()
    {
        SetupDiceRolls(3);
        var json = JsonSerializer.Serialize(ClassedCharacter(CharacterClass.EsotericHermit), _options);

        var node = JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("class"), "expected a 'class' key to remove");
        Assert.True(node["powers"]!.AsObject().Remove("powerDie"), "expected a 'powerDie' key to remove");

        var deserialized = JsonSerializer.Deserialize<Character>(node.ToJsonString(), _options)!;

        Assert.Equal(CharacterClass.Classless, deserialized.Class);
        Assert.Null(deserialized.Powers.PowerDie);
        // Still usable: the null die falls back to d4, so a new dawn does not crash.
        deserialized.Powers.ResetForNewDay(deserialized.Abilities, Dice);
        Assert.True(deserialized.Powers.MaxUses >= 1);
    }

    private Character ClassedCharacter(CharacterClass characterClass) => Character.Create(
        Guid.NewGuid(), "TestHero", 10,
        new Abilities(new AbilityScore(1), new AbilityScore(2), new AbilityScore(0), new AbilityScore(-1)),
        new StartingEquipment(50, 3, "Sack", null, null,
            Weapon.Create(WeaponKind.Sword), new Armor(ArmorTier.Light), null, []),
        Dice, 0, characterClass);

    [Fact]
    public void Character_RoundTrips_BasicProperties()
    {
        SetupDiceRolls(3); // PowerPool.Create needs d4 roll
        var abilities = new Abilities(
            new AbilityScore(1),
            new AbilityScore(2),
            new AbilityScore(0),
            new AbilityScore(-1));

        var character = Character.Create(
            Guid.NewGuid(), "TestHero", 10, abilities,
            new StartingEquipment(50, 3, "Sack", null, null,
                Weapon.Create(WeaponKind.Sword),
                new Armor(ArmorTier.Light),
                null, []), Dice);

        var json = JsonSerializer.Serialize(character, _options);
        var deserialized = JsonSerializer.Deserialize<Character>(json, _options)!;

        Assert.Equal(character.Id, deserialized.Id);
        Assert.Equal(character.Name, deserialized.Name);
        Assert.Equal(character.Hp.Current, deserialized.Hp.Current);
        Assert.Equal(character.Hp.Max, deserialized.Hp.Max);
        Assert.Equal(character.Abilities.Agility.Modifier, deserialized.Abilities.Agility.Modifier);
        Assert.Equal(character.Abilities.Presence.Modifier, deserialized.Abilities.Presence.Modifier);
        Assert.Equal(character.Abilities.Strength.Modifier, deserialized.Abilities.Strength.Modifier);
        Assert.Equal(character.Abilities.Toughness.Modifier, deserialized.Abilities.Toughness.Modifier);
        Assert.Equal(character.Silver, deserialized.Silver);
        Assert.Equal(character.FoodDays, deserialized.FoodDays);
    }

    [Fact]
    public void Character_RoundTrips_StatusFlags()
    {
        SetupDiceRolls(3); // PowerPool
        var abilities = new Abilities(
            new AbilityScore(0), new AbilityScore(0),
            new AbilityScore(0), new AbilityScore(0));
        var character = Character.Create(
            Guid.NewGuid(), "BrokenHero", 10, abilities,
            new StartingEquipment(0, 1, "Sack", null, null,
                Weapon.Create(WeaponKind.Knife),
                new Armor(ArmorTier.None), null, []), Dice);

        character.Infect();

        var json = JsonSerializer.Serialize(character, _options);
        var deserialized = JsonSerializer.Deserialize<Character>(json, _options)!;

        Assert.True(deserialized.IsInfected);
        Assert.False(deserialized.IsDead);
    }

    [Fact]
    public void Character_RoundTrips_FullEquipmentLoadout()
    {
        SetupDiceRolls(3); // PowerPool
        var abilities = new Abilities(
            new AbilityScore(1), new AbilityScore(2),
            new AbilityScore(1), new AbilityScore(0));

        var scrolls = new List<Scroll>
        {
            new(Guid.NewGuid(), ScrollSchool.Sacred, "Heal"),
            new(Guid.NewGuid(), ScrollSchool.Unclean, "Curse")
        };

        var gear1 = new InventoryItem(Guid.NewGuid(), "Rope 30ft", false, false);
        var gear2 = new InventoryItem(Guid.NewGuid(), "Torches", false, true, 4);

        var character = Character.Create(
            Guid.NewGuid(), "FullLoadout", 12, abilities,
            new StartingEquipment(100, 5, "Backpack",
                gear1, gear2,
                Weapon.Create(WeaponKind.Zweihander),
                new Armor(ArmorTier.Heavy),
                new Shield(), scrolls), Dice);

        var json = JsonSerializer.Serialize(character, _options);
        var deserialized = JsonSerializer.Deserialize<Character>(json, _options)!;

        Assert.Equal(character.Weapon.Kind, deserialized.Weapon.Kind);
        Assert.Equal(character.Weapon.DamageDie, deserialized.Weapon.DamageDie);
        Assert.Equal(ArmorTier.Heavy, deserialized.Armor.Tier);
        Assert.NotNull(deserialized.Shield);
        Assert.Equal(2, deserialized.Scrolls.Count);
        Assert.Equal(character.Inventory.InventoryItems.Count, deserialized.Inventory.InventoryItems.Count);
        Assert.Equal(character.Powers.MaxUses, deserialized.Powers.MaxUses);
        Assert.Equal(character.Powers.UsesRemaining, deserialized.Powers.UsesRemaining);
    }

    [Fact]
    public void Character_RoundTrips_InjurySet()
    {
        // Dice sequence (values are 0-based, actual roll = 1 + value):
        // 1. PowerPool.Create: returns 3 -> roll 4
        // 2. Defend -> ResolveDefence -> Challenge D20: returns 4 -> roll 5 (fail, not nat1/nat20)
        // 3. CalculateDamageAfterDefense -> attack D4: returns 0 -> roll 1 (1 damage, enough to reach 0 HP)
        // 4. ReceiveDamage -> ResolveBroken -> D4: returns 2 -> roll 3 (not dead, proceed to injury)
        // 5. ResolveBroken -> D6: returns 4 -> roll 5 (BrokenHand)
        SetupDiceRolls(3, 4, 0, 2, 4);

        var abilities = new Abilities(
            new AbilityScore(0), new AbilityScore(0),
            new AbilityScore(0), new AbilityScore(0));

        var character = Character.Create(
            Guid.NewGuid(), "InjuredHero", 1, abilities,
            new StartingEquipment(0, 1, "Sack", null, null,
                Weapon.Create(WeaponKind.Knife),
                new Armor(ArmorTier.None), null, []), Dice);

        // Force injury by defending with 1 HP against an attack
        character.Defend(DiceExpr.D4, Dice);

        // Verify the injury was applied
        Assert.True(character.HasBrokenHand);
        Assert.True(character.Injuries.Has(InjuryKind.BrokenHand));

        // Serialize and deserialize with AggregateJsonOptions
        var json = JsonSerializer.Serialize(character, _options);
        var deserialized = JsonSerializer.Deserialize<Character>(json, _options)!;

        // Verify injury round-trips through JSON
        Assert.True(deserialized.Injuries.Has(InjuryKind.BrokenHand));
        Assert.True(deserialized.HasBrokenHand);
        Assert.False(deserialized.HasLostEye);
        Assert.False(deserialized.HasSeveredArm);

        // Verify other state preserved
        Assert.Equal(character.Id, deserialized.Id);
        Assert.Equal(character.Name, deserialized.Name);
        Assert.Equal(character.Hp.Current, deserialized.Hp.Current);
    }

    [Fact]
    public void Campaign_RoundTrips_PreservingState()
    {
        SetupDiceRolls(3); // dawnDice not needed for Create, but just in case
        var campaign = Campaign.Create(Difficulty.Grim, "DoomCampaign", "The end is nigh");
        var charId = Guid.NewGuid();
        campaign.JoinGame(charId);
        campaign.Configure( "DoomCampaign", "The end is nigh");

        var json = JsonSerializer.Serialize(campaign, _options);
        var deserialized = JsonSerializer.Deserialize<Campaign>(json, _options)!;

        Assert.Equal(campaign.Id, deserialized.Id);
        Assert.Equal(campaign.Name, deserialized.Name);
        Assert.Equal(campaign.Description, deserialized.Description);
        Assert.Equal(campaign.CurrentDay, deserialized.CurrentDay);
        Assert.Equal(campaign.CurrentHour, deserialized.CurrentHour);
        Assert.Contains(charId, deserialized.Players);
        Assert.True(deserialized.IsConfigured);
    }

    [Fact]
    public void Encounter_RoundTrips_PreservingAdversaries()
    {
        SetupDiceRolls(7, 7); // Initial reaction roll (2d6 = 7+7 = 14 = Helpful), then for any other dice
        var encounter = Encounter.Create("Dark Cave", "A cave full of evil",
            EncounterType.Hostile, Dice);

        var adversary = new Adversary("Goblin",
            new HitPoints(5, 5),
            new Armor(ArmorTier.Light),
            7,
            new AttackProfile("Claw", DiceExpr.D4));
        encounter.AddAdversary(adversary);

        var json = JsonSerializer.Serialize(encounter, _options);
        var deserialized = JsonSerializer.Deserialize<Encounter>(json, _options)!;

        Assert.Equal(encounter.Id, deserialized.Id);
        Assert.Equal(encounter.InitialType, deserialized.InitialType);
        Assert.Equal(encounter.Name, deserialized.Name);
        Assert.Single(deserialized.Adversaries);
        Assert.Equal("Goblin", deserialized.Adversaries[0].Name);
        Assert.Equal(5, deserialized.Adversaries[0].Hp.Current);
    }

    [Theory]
    [InlineData("heavy", ArmorTier.Heavy)]
    [InlineData("medium", ArmorTier.Medium)]
    [InlineData("light", ArmorTier.Light)]
    [InlineData("none", ArmorTier.None)]
    public void ArmorTier_RoundTrips(string expectedJson, ArmorTier tier)
    {
        var json = JsonSerializer.Serialize(tier, _options);
        Assert.Equal($"\"{expectedJson}\"", json);

        var deserialized = JsonSerializer.Deserialize<ArmorTier>(json, _options)!;
        Assert.Equal(tier, deserialized);
        Assert.Equal(tier.DefencePenalty(), deserialized.DefencePenalty());
        Assert.Equal(tier.AgilityPenalty(), deserialized.AgilityPenalty());
    }
}
