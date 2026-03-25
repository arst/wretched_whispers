#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Semantic;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class KernelFactoryTests
{
    private readonly KernelFactory _factory;

    public KernelFactoryTests()
    {
        var services = new ServiceCollection();

        // Mock repositories
        var charsRepo = new Mock<ICharactersRepository>().Object;
        var campsRepo = new Mock<ICampaignsRepository>().Object;
        var encsRepo = new Mock<IEncountersRepository>().Object;
        var dice = new Dice(new Mock<IRandomService>().Object);

        // Register concrete SK plugins with mock dependencies
        services.AddSingleton(_ => new CharacterPlugin(
            charsRepo,
            new CharacterCreationService(charsRepo, dice),
            new CharacterService(charsRepo, dice),
            dice));
        services.AddSingleton(_ => new CampaignPlugin(
            campsRepo, charsRepo,
            new CampaignService(campsRepo, charsRepo, dice)));
        services.AddSingleton(_ => new EncounterPlugin(
            new EncounterService(dice, charsRepo, encsRepo), encsRepo, dice));
        services.AddSingleton(_ => new DicePlugin(dice));
        services.AddSingleton(campsRepo);
        services.AddSingleton(encsRepo);

        var settings = Options.Create(new AzureOpenAiSettings
        {
            ChatModelDeployment = "test-deployment",
            Endpoint = "https://test.openai.azure.com/",
            ApiKey = "test-key"
        });

        var sp = services.BuildServiceProvider();

        _factory = new KernelFactory(
            sp,
            settings,
            NullLogger<KernelFactory>.Instance);
    }

    [Fact]
    public void CharacterCreation_HasExactly1Function()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, registered) = _factory.CreateForStage(ctx, SessionStage.CharacterCreation);

        Assert.Single(registered);
        Assert.Contains("Character.CreateCharacter", registered);
    }

    [Fact]
    public void CampaignSetup_HasExactly2Functions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, registered) = _factory.CreateForStage(ctx, SessionStage.CampaignSetup);

        Assert.Equal(2, registered.Length);
        Assert.Contains("Campaign.ConfigureCampaign", registered);
        Assert.Contains("Campaign.StartCampaign", registered);
    }

    [Fact]
    public void Exploration_HasExactly10Functions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, registered) = _factory.CreateForStage(ctx, SessionStage.Exploration);

        Assert.Equal(10, registered.Length);
        Assert.Contains("Character.ChallengeCharacter", registered);
        Assert.Contains("Campaign.AdvanceTime", registered);
        Assert.Contains("Encounter.CreateEncounter", registered);
        Assert.Contains("Dice.Roll", registered);
        Assert.DoesNotContain("Character.CreateCharacter", registered);
        Assert.DoesNotContain("Campaign.ConfigureCampaign", registered);
    }

    [Fact]
    public void Combat_HasExactly4Functions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, registered) = _factory.CreateForStage(ctx, SessionStage.Combat);

        Assert.Equal(4, registered.Length);
        Assert.Contains("Encounter.AttackPlayer", registered);
        Assert.Contains("Encounter.AttackAdversary", registered);
        Assert.Contains("Encounter.EndEncounter", registered);
        Assert.Contains("Dice.Roll", registered);
    }

    [Fact]
    public void Resolution_HasCorrectFunctions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, registered) = _factory.CreateForStage(ctx, SessionStage.Resolution);

        Assert.Contains("Resolution.CompleteResolution", registered);
        Assert.Contains("Campaign.AdvanceTime", registered);
        Assert.Contains("Character.AddItemToCharacterInventory", registered);
        Assert.DoesNotContain("Character.CreateCharacter", registered);
        Assert.DoesNotContain("Campaign.StartCampaign", registered);
    }

    [Fact]
    public void Ended_HasNoFunctions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, registered) = _factory.CreateForStage(ctx, SessionStage.Ended);

        Assert.Empty(registered);
    }
}
