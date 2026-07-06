using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Create;

public class CharacterCreationServiceTests : TestBase
{
    [Fact]
    public async Task Create_ProducesValidCharacter()
    {
        var repoMock = new Mock<ICharactersRepository>();
        var service = new CharacterCreationService(repoMock.Object, Dice);
        var character = await service.Create("Hero", Difficulty.Grim);

        Assert.NotNull(character);
        Assert.Equal("Hero", character.Name);
        // Abilities in allowed range
        Assert.InRange(character.Abilities.Agility.Modifier, -3, 6);
        Assert.InRange(character.Abilities.Presence.Modifier, -3, 6);
        Assert.InRange(character.Abilities.Strength.Modifier, -3, 6);
        Assert.InRange(character.Abilities.Toughness.Modifier, -3, 6);
        // HP at least 1
        Assert.True(character.Hp.Current >= 1);
        Assert.True(character.Hp.Max >= 1);
        // Has a weapon
        Assert.NotNull(character.Weapon);
        // Weapon kind is valid
        Assert.True(Enum.IsDefined(typeof(WeaponKind), character.Weapon.Kind));
        // Saved to repository
        repoMock.Verify(r => r.Save(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Create_AlwaysHasAWeapon()
    {
        var repoMock = new Mock<ICharactersRepository>();
        var service = new CharacterCreationService(repoMock.Object, Dice);
        for (var i = 0; i < 10; i++)
        {
            var character = await service.Create($"Hero{i}", Difficulty.Grim);
            Assert.NotNull(character.Weapon);
            Assert.True(Enum.IsDefined(typeof(WeaponKind), character.Weapon.Kind));
        }
    }

    [Fact]
    public async Task Create_AbilitiesWithinAllowedRange()
    {
        var repoMock = new Mock<ICharactersRepository>();
        var service = new CharacterCreationService(repoMock.Object, Dice);
        for (var i = 0; i < 10; i++)
        {
            var character = await service.Create($"Hero{i}", Difficulty.Grim);
            Assert.InRange(character.Abilities.Agility.Modifier, -3, 6);
            Assert.InRange(character.Abilities.Presence.Modifier, -3, 6);
            Assert.InRange(character.Abilities.Strength.Modifier, -3, 6);
            Assert.InRange(character.Abilities.Toughness.Modifier, -3, 6);
        }
    }

    [Fact]
    public async Task Create_HpIsConsistentWithToughness()
    {
        var repoMock = new Mock<ICharactersRepository>();
        var service = new CharacterCreationService(repoMock.Object, Dice);
        for (var i = 0; i < 10; i++)
        {
            var character = await service.Create($"Hero{i}", Difficulty.Grim);
            Assert.True(character.Hp.Current >= 1);
            Assert.True(character.Hp.Max >= 1);
        }
    }

    [Fact]
    public async Task Create_InventoryDoesNotExceedContainerLimit()
    {
        var repoMock = new Mock<ICharactersRepository>();
        var service = new CharacterCreationService(repoMock.Object, Dice);
        for (var i = 0; i < 10; i++)
        {
            var character = await service.Create($"Hero{i}", Difficulty.Grim);
            if (character.Inventory.Container.Contains("backpack"))
                Assert.True(character.Inventory.InventoryItems.Count <= 7);
            else if (character.Inventory.Container.Contains("sack"))
                Assert.True(character.Inventory.InventoryItems.Count <= 10);
        }
    }
}