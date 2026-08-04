using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class StateUpdateMapperTests
{
    // Fresh seeded dice per character so each test's construction roll is deterministic
    // regardless of test order (Character.Create consumes one d4 roll).
    private static Dice NewDice() => new(new SeededRandomService(1));

    [Fact]
    public void Map_WithNoCharacter_ReturnsNullCharacterFields()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.Campaign = Campaign.Create(Difficulty.Grim, "Test", "desc");

        var result = StateUpdateMapper.Map(context);

        Assert.Null(result.CharacterId);
        Assert.Null(result.CharacterName);
    }

    [Fact]
    public void Map_WithNoCampaign_ReturnsNullCampaignFields()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };

        var result = StateUpdateMapper.Map(context);

        Assert.Null(result.CampaignId);
        Assert.Equal(0, result.CurrentDay);
    }

    [Fact]
    public void Map_ExpandsInventoryQuantitiesToUnits()
    {
        // One entry per UNIT, not per item: the UI groups them back with a xN badge, and the
        // turn-delta multiset diff needs units so a quantity decrement (3 torches -> 2) surfaces
        // as one removed entry instead of vanishing.
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.Campaign = Campaign.Create(Difficulty.Grim, "Test", "desc");
        var character = TestCharacters.Create(NewDice());
        character.AddItem(new InventoryItem(Guid.NewGuid(), "torches", false, true, 3));
        character.AddItem(new InventoryItem(Guid.NewGuid(), "crowbar", false, false, 1));
        context.Character = character;

        var result = StateUpdateMapper.Map(context);

        Assert.Equal(
            new[] { "torches", "torches", "torches", "crowbar" },
            result.CharacterInventory);
        Assert.Equal(character.Silver, result.CharacterSilver);
    }

    [Fact]
    public void Map_WithAClassedCharacter_SendsTheClassDisplayName()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.Character = CreateClassedCharacter(CharacterClass.OccultHerbmaster);

        Assert.Equal("Occult Herbmaster", StateUpdateMapper.Map(context).CharacterClass);
    }

    /// <summary>Classless is the absence of a class, not a class named "Classless Scum" -- the UI keys off
    /// null to render no class line at all.</summary>
    [Fact]
    public void Map_WithAClasslessCharacter_SendsNoClass()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.Character = TestCharacters.Create(NewDice());

        Assert.Null(StateUpdateMapper.Map(context).CharacterClass);
    }

    /// <summary>The shared TestCharacters builder has no class knob, so classed characters get this one
    /// local delegate to Character.Create.</summary>
    private static Character CreateClassedCharacter(CharacterClass characterClass)
    {
        var abilities = new Abilities(
            agility: new AbilityScore(0),
            presence: new AbilityScore(0),
            strength: new AbilityScore(0),
            toughness: new AbilityScore(0));
        var equipment = new StartingEquipment(
            Silver: 120, FoodDays: 3, Container: "satchel",
            Gear1: null, Gear2: null,
            Weapon: Weapon.Create(WeaponKind.Staff),
            Armor: new Armor(ArmorTier.Medium),
            Shield: null, Scrolls: []);

        return Character.Create(Guid.NewGuid(), "Tuck", 2, abilities, equipment, NewDice(), 0, characterClass);
    }
}
