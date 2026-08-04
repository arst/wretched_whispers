using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Create;

public class CharacterCreationServiceTests : TestBase
{
    private readonly Mock<ICharactersRepository> _repo = new();

    private CharacterCreationService NewService() => new(_repo.Object, Dice);

    [Fact]
    public async Task Create_SetsIdentityAndAbilitiesInRange()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim);

        Assert.Equal("Hero", character.Name);
        Assert.InRange(character.Abilities.Agility.Modifier, -3, 6);
        Assert.InRange(character.Abilities.Presence.Modifier, -3, 6);
        Assert.InRange(character.Abilities.Strength.Modifier, -3, 6);
        Assert.InRange(character.Abilities.Toughness.Modifier, -3, 6);
    }

    [Fact]
    public async Task Create_EquipsAValidWeapon()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim);

        Assert.NotNull(character.Weapon);
        Assert.True(Enum.IsDefined(typeof(WeaponKind), character.Weapon.Kind));
    }

    [Fact]
    public async Task Create_HasPositiveHp_AndSaves()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim);

        Assert.True(character.Hp.Current >= 1);
        Assert.True(character.Hp.Max >= 1);
        _repo.Verify(r => r.Save(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Create_RollsD2ForStartingOmens()
    {
        // Only d2 rolls are mocked to land on 2; ability/HP/gear dice use the mock default (roll 1),
        // whose gear results involve no d2 — so the sole d2 in creation is the omen roll.
        SetupDiceRoll(2, 1);

        var character = await NewService().Create("Hero", Difficulty.Grim);

        Assert.Equal(2, character.Omens.Count);
    }

    [Fact]
    public async Task StoryMode_AddsEightToRolledHp()
    {
        // Force every roll to its minimum so base HP is deterministic between the two characters.
        MockRandomService.Setup(r => r.GenerateRandomRoll(It.IsAny<int>())).Returns(0);

        var story = await NewService().Create("A", Difficulty.StoryMode);
        var grim = await NewService().Create("B", Difficulty.Grim);

        Assert.Equal(grim.Hp.Max + 8, story.Hp.Max);
    }
}
