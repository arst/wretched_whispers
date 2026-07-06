using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure;
using System.Text.Json;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class StateUpdateMapperTests
{
    [Fact]
    public void Map_WithNoCharacter_ReturnsNullCharacterFields()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Test", "desc");
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.Campaign = campaign;

        var result = StateUpdateMapper.Map(context);

        Assert.NotNull(result);
        Assert.Null(result.CharacterId);
        Assert.Null(result.CharacterName);
        Assert.Equal("charactercreation", result.Stage);
        Assert.Equal("character-creation", result.Status);
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
    public void Map_WithCharacter_ReturnsSilver()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.Campaign = Campaign.Create(Difficulty.Grim, "Test", "desc");
        context.Character = CreateCharacter();

        var result = StateUpdateMapper.Map(context);

        Assert.Equal(120, result.CharacterSilver);
    }

    [Fact]
    public void Map_WithCharacter_SerializesSilverAsCamelCase()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.Campaign = Campaign.Create(Difficulty.Grim, "Test", "desc");
        context.Character = CreateCharacter();

        var json = JsonSerializer.Serialize(
            StateUpdateMapper.Map(context),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var document = JsonDocument.Parse(json);
        Assert.Equal(120, document.RootElement.GetProperty("characterSilver").GetInt32());
    }

    private static Character CreateCharacter()
    {
        var abilities = new Abilities(
            agility: new AbilityScore(0),
            presence: new AbilityScore(0),
            strength: new AbilityScore(0),
            toughness: new AbilityScore(0));
        var equipment = new StartingEquipment(
            Silver: 120,
            FoodDays: 3,
            Container: "satchel",
            Gear1: null,
            Gear2: null,
            Weapon: Weapon.Create(WeaponKind.Staff),
            Armor: new Armor(ArmorTier.Medium),
            Shield: null,
            Scrolls: []);

        return Character.Create(Guid.NewGuid(), "Tuck", 2, abilities, equipment, new Dice(new SeededRandomService(1)));
    }
}
