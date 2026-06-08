using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Api.GameTools;
using Xunit;

namespace WretchedWhispers.Tests.Services;

/// <summary>
/// Verifies the Agent Framework tool provider exposes exactly the stage-scoped tool set
/// (the registered-function names mirror <see cref="StageToolMap"/>). Replaces the former
/// KernelFactoryTests after the SK→Agent Framework migration.
/// </summary>
public class AgentToolProviderTests
{
    private readonly AgentToolProvider _provider;

    public AgentToolProviderTests()
    {
        var services = new ServiceCollection();

        var charsRepo = new Mock<ICharactersRepository>().Object;
        var campsRepo = new Mock<ICampaignsRepository>().Object;
        var encsRepo = new Mock<IEncountersRepository>().Object;
        var dice = new Dice(new Mock<IRandomService>().Object);

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

        var sp = services.BuildServiceProvider();

        _provider = new AgentToolProvider(sp, NullLogger<AgentToolProvider>.Instance);
    }

    [Fact]
    public void CharacterCreation_ExposesCreateCharacterAndCampaignSetupTools()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, registered) = _provider.GetToolsForStage(ctx, SessionStage.CharacterCreation);

        Assert.Equal(3, registered.Length);
        Assert.Equal(3, tools.Count);
        Assert.Contains("Character.CreateCharacter", registered);
        Assert.Contains("Campaign.ConfigureCampaign", registered);
        Assert.Contains("Campaign.StartCampaign", registered);
    }

    [Fact]
    public void CampaignSetup_HasExactly2Functions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, registered) = _provider.GetToolsForStage(ctx, SessionStage.CampaignSetup);

        Assert.Equal(2, registered.Length);
        Assert.Equal(2, tools.Count);
        Assert.Contains("Campaign.ConfigureCampaign", registered);
        Assert.Contains("Campaign.StartCampaign", registered);
    }

    [Fact]
    public void Exploration_HasExactly10Functions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, registered) = _provider.GetToolsForStage(ctx, SessionStage.Exploration);

        Assert.Equal(10, registered.Length);
        Assert.Equal(10, tools.Count);
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
        var (tools, registered) = _provider.GetToolsForStage(ctx, SessionStage.Combat);

        Assert.Equal(4, registered.Length);
        Assert.Equal(4, tools.Count);
        Assert.Contains("Encounter.AttackPlayer", registered);
        Assert.Contains("Encounter.AttackAdversary", registered);
        Assert.Contains("Encounter.EndEncounter", registered);
        Assert.Contains("Dice.Roll", registered);
    }

    [Fact]
    public void Resolution_HasCorrectFunctions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (_, registered) = _provider.GetToolsForStage(ctx, SessionStage.Resolution);

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
        var (tools, registered) = _provider.GetToolsForStage(ctx, SessionStage.Ended);

        Assert.Empty(registered);
        Assert.Empty(tools);
    }

    [Fact]
    public void ToolNames_AreBareFunctionNames_MatchingStagePrompts()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, _) = _provider.GetToolsForStage(ctx, SessionStage.Exploration);

        // The stage prompts instruct the model to call e.g. "ChallengeCharacter" (no plugin prefix),
        // so the actual tool names must be the bare method names.
        Assert.Contains(tools, t => t.Name == "ChallengeCharacter");
        Assert.Contains(tools, t => t.Name == "CreateEncounter");
        Assert.Contains(tools, t => t.Name == "Roll");
    }
}
