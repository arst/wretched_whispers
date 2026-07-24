using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Create;

public class CharacterCreationDifficultyTests : TestBase
{
    private CharacterCreationService CreateService()
    {
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Save(It.IsAny<Character>())).Returns(Task.CompletedTask);
        return new CharacterCreationService(repo.Object, Dice);
    }

    [Fact]
    public async Task StoryMode_adds_eight_to_rolled_hp()
    {
        // Force every roll to its minimum so base HP is deterministic between the two characters.
        MockRandomService.Setup(r => r.GenerateRandomRoll(It.IsAny<int>())).Returns(0);
        var story = await CreateService().Create("A", Difficulty.StoryMode);

        MockRandomService.Setup(r => r.GenerateRandomRoll(It.IsAny<int>())).Returns(0);
        var grim = await CreateService().Create("B", Difficulty.Grim);

        Assert.Equal(grim.Hp.Max + 8, story.Hp.Max);
    }
}
