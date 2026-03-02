using System.Text.Json;
using Xunit;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Infrastructure.Persistence.Serialization;

namespace WretchedWhispers.Tests.Persistence;

public class JsonSerializationTests : TestBase
{
    private readonly JsonSerializerOptions _options = AggregateJsonOptions.Create();

    [Fact]
    public void AggregateJsonOptions_Create_ReturnsOptionsWithArmorTierConverter()
    {
        var options = AggregateJsonOptions.Create();
        Assert.Contains(options.Converters, c => c is ArmorTierConverter);
    }

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
                new Armor(LightArmorTier.Instance),
                null, []));

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
                new Armor(NoArmorTier.Instance), null, []));

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
                new Armor(HeavyArmorTier.Instance),
                new Shield(), scrolls));

        var json = JsonSerializer.Serialize(character, _options);
        var deserialized = JsonSerializer.Deserialize<Character>(json, _options)!;

        Assert.Equal(character.Weapon.Kind, deserialized.Weapon.Kind);
        Assert.Equal(character.Weapon.DamageDie, deserialized.Weapon.DamageDie);
        Assert.IsType<HeavyArmorTier>(deserialized.Armor.Tier);
        Assert.NotNull(deserialized.Shield);
        Assert.Equal(2, deserialized.Scrolls.Count);
        Assert.Equal(character.Inventory.InventoryItems.Count, deserialized.Inventory.InventoryItems.Count);
        Assert.Equal(character.Powers.MaxUses, deserialized.Powers.MaxUses);
        Assert.Equal(character.Powers.UsesRemaining, deserialized.Powers.UsesRemaining);
    }

    [Fact]
    public void Campaign_RoundTrips_PreservingState()
    {
        SetupDiceRolls(3); // dawnDice not needed for Create, but just in case
        var campaign = Campaign.Create(DiceExpr.D6, "DoomCampaign", "The end is nigh");
        var charId = Guid.NewGuid();
        campaign.JoinGame(charId);

        var json = JsonSerializer.Serialize(campaign, _options);
        var deserialized = JsonSerializer.Deserialize<Campaign>(json, _options)!;

        Assert.Equal(campaign.Id, deserialized.Id);
        Assert.Equal(campaign.Name, deserialized.Name);
        Assert.Equal(campaign.Description, deserialized.Description);
        Assert.Equal(campaign.CurrentDay, deserialized.CurrentDay);
        Assert.Equal(campaign.CurrentHour, deserialized.CurrentHour);
        Assert.Contains(charId, deserialized.Players);
    }

    [Fact]
    public void Encounter_RoundTrips_PreservingAdversaries()
    {
        SetupDiceRolls(7, 7); // Initial reaction roll (2d6 = 7+7 = 14 = Helpful), then for any other dice
        var encounter = Encounter.Create("Dark Cave", "A cave full of evil",
            EncounterType.Hostile, MockRandomService.Object);

        var adversary = new Adversary("Goblin",
            new HitPoints(5, 5),
            new Armor(LightArmorTier.Instance),
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
    [InlineData("heavy", typeof(HeavyArmorTier))]
    [InlineData("medium", typeof(MediumArmorTier))]
    [InlineData("light", typeof(LightArmorTier))]
    [InlineData("none", typeof(NoArmorTier))]
    public void ArmorTier_Polymorphic_RoundTrips(string expectedDiscriminator, Type expectedType)
    {
        ArmorTier tier = expectedDiscriminator switch
        {
            "heavy" => HeavyArmorTier.Instance,
            "medium" => MediumArmorTier.Instance,
            "light" => LightArmorTier.Instance,
            "none" => NoArmorTier.Instance,
            _ => throw new ArgumentException()
        };

        var json = JsonSerializer.Serialize(tier, _options);
        Assert.Contains($"\"$type\":\"{expectedDiscriminator}\"", json);

        var deserialized = JsonSerializer.Deserialize<ArmorTier>(json, _options)!;
        Assert.IsType(expectedType, deserialized);
        Assert.Equal(tier.DefencePenalty, deserialized.DefencePenalty);
        Assert.Equal(tier.AgilityPenalty, deserialized.AgilityPenalty);
    }
}
