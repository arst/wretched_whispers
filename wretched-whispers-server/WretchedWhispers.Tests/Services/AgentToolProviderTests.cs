using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

/// <summary>
/// Verifies the Agent Framework tool provider exposes exactly the stage-scoped tool set
/// (the registered-function names mirror <see cref="GameToolCatalog"/>). Replaces the former
/// KernelFactoryTests after the SK→Agent Framework migration. Which tools belong to which stage is
/// pinned (with explicit lists) in GameToolCatalogTests — here we only prove the provider builds
/// what the catalog dictates.
/// </summary>
public class AgentToolProviderTests
{
    private readonly AgentToolProvider _provider;

    public AgentToolProviderTests()
    {
        var charsRepo = new Mock<ICharactersRepository>().Object;
        var campsRepo = new Mock<ICampaignsRepository>().Object;
        var encsRepo = new Mock<IEncountersRepository>().Object;
        var dice = new Dice(new Mock<IRandomService>().Object);

        _provider = new AgentToolProvider(
            charsRepo,
            encsRepo,
            new CharacterService(charsRepo, dice),
            new CampaignService(campsRepo, charsRepo, dice),
            new EncounterService(dice, charsRepo, encsRepo),
            dice,
            NullLogger<AgentToolProvider>.Instance);
    }

    [Theory]
    [InlineData(SessionStage.CharacterCreation)]
    [InlineData(SessionStage.CampaignSetup)]
    [InlineData(SessionStage.Exploration)]
    [InlineData(SessionStage.Combat)]
    [InlineData(SessionStage.Resolution)]
    [InlineData(SessionStage.Ended)]
    public void EveryStage_BuildsExactlyTheCatalogToolSet(SessionStage stage)
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };

        var (tools, registered) = _provider.GetToolsForStage(ctx, stage);

        var expected = GameToolCatalog.ForStage(stage);
        Assert.Equal(expected.Count, tools.Count);
        Assert.Equal(expected.Select(d => $"{d.Group}.{d.Name}").ToArray(), registered);
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
