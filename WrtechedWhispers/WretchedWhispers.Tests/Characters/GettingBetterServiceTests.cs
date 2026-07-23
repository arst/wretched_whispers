using Moq;
using WretchedWhispers.Core.Characters;
using Xunit;

namespace WretchedWhispers.Tests.Characters;

public sealed class GettingBetterServiceTests : TestBase
{
    [Fact]
    public async Task GetBetter_RollsAndSavesOnce()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        SetupDiceRolls(0 /* heal d6 */);
        character.Rest(8, Dice);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(character.Id)).ReturnsAsync(character);
        var service = new CharacterService(repo.Object, Dice);
        SetupDiceRolls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0); // 6d10 fail, four ability d6s

        var outcome = await service.GetBetter(character.Id, allowAbilityLoss: true);

        Assert.Equal(6, outcome.HpRoll);
        Assert.False(character.CanGetBetter);
        repo.Verify(r => r.Save(character), Times.Once);
    }

    [Fact]
    public async Task GetBetter_UnknownCharacter_Throws()
    {
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Character?)null);
        var service = new CharacterService(repo.Object, Dice);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetBetter(Guid.NewGuid(), allowAbilityLoss: true));
    }
}
