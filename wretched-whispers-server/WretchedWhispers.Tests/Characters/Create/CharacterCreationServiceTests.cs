using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
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
    public async Task Create_ClassScroll_IsNamedForARealPower()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.EsotericHermit);

        Assert.NotEmpty(character.Scrolls);
        Assert.All(character.Scrolls, s => Assert.NotNull(ScrollPowers.Find(s.Description)));
    }

    [Fact]
    public async Task Create_GearTableScroll_IsNamedForAPowerOfItsSchool()
    {
        SetupDiceRoll(12, 4); // both gear tables roll 5: the first is an unclean scroll
        SetupDiceRoll(10, 9); // the power table rolls 10

        var character = await NewService().Create("Hero", Difficulty.Grim);

        var scroll = Assert.Single(character.Scrolls);
        Assert.Equal(ScrollSchool.Unclean, scroll.School);
        Assert.Equal("Death", scroll.Description);
    }

    [Theory]
    [InlineData(0, "life elixir")] // both gear tables roll 1: the second is the elixir
    [InlineData(6, "medicine chest (Presence + 4 uses)")] // both roll 7: the first is the chest
    public async Task Create_HealingGear_IsMarkedAsHealing(int zeroBasedD12, string healingItem)
    {
        SetupDiceRoll(12, zeroBasedD12);

        var character = await NewService().Create("Hero", Difficulty.Grim);

        Assert.All(character.Inventory.InventoryItems, item =>
            Assert.Equal(item.Description == healingItem ? ItemEffect.Heal : ItemEffect.None, item.Effect));
        Assert.Contains(character.Inventory.InventoryItems, item => item.Description == healingItem);
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
