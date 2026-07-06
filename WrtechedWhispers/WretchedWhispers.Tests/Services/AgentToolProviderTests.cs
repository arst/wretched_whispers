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
/// (the registered-function names mirror <see cref="GameToolCatalog"/>). Replaces the former
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

        // AgentToolProvider constructs the *Tools classes from these Core services.
        services.AddSingleton(charsRepo);
        services.AddSingleton(campsRepo);
        services.AddSingleton(encsRepo);
        services.AddSingleton(dice);
        services.AddSingleton(new CharacterCreationService(charsRepo, dice));
        services.AddSingleton(new CharacterService(charsRepo, dice));
        services.AddSingleton(new CampaignService(campsRepo, charsRepo, dice));
        services.AddSingleton(new EncounterService(dice, charsRepo, encsRepo));

        var sp = services.BuildServiceProvider();

        _provider = new AgentToolProvider(sp, NullLogger<AgentToolProvider>.Instance);
    }

    [Fact]
    public void CharacterCreation_ExposesCreateCharacterAndCampaignSetupTools()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, registered) = _provider.GetToolsForStage(ctx, SessionStage.CharacterCreation);

        Assert.Equal(2, registered.Length);
        Assert.Equal(2, tools.Count);
        Assert.Contains("Character.CreateCharacter", registered);
        Assert.Contains("Campaign.ConfigureCampaign", registered);
    }

    [Fact]
    public void CampaignSetup_HasExactly1Function()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, registered) = _provider.GetToolsForStage(ctx, SessionStage.CampaignSetup);

        Assert.Single(registered);
        Assert.Single(tools);
        Assert.Contains("Campaign.ConfigureCampaign", registered);
    }

    [Fact]
    public void Exploration_HasExactly12Functions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, registered) = _provider.GetToolsForStage(ctx, SessionStage.Exploration);

        Assert.Equal(12, registered.Length);
        Assert.Equal(12, tools.Count);
        Assert.Contains("Character.UseItemFromCharacterInventory", registered);
        Assert.Contains("Character.ChallengeCharacter", registered);
        Assert.Contains("Campaign.AdvanceTime", registered);
        Assert.Contains("Campaign.RecordJournalEntry", registered);
        Assert.Contains("Encounter.CreateEncounter", registered);
        Assert.Contains("Dice.Roll", registered);
        Assert.DoesNotContain("Character.CreateCharacter", registered);
        Assert.DoesNotContain("Campaign.ConfigureCampaign", registered);
    }

    [Fact]
    public void Combat_HasExactly5Functions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, registered) = _provider.GetToolsForStage(ctx, SessionStage.Combat);

        Assert.Equal(5, registered.Length);
        Assert.Equal(5, tools.Count);
        Assert.Contains("Character.UseItemFromCharacterInventory", registered);
        Assert.Contains("Encounter.ResolveCombatRound", registered);
        Assert.Contains("Character.CastScroll", registered);
        Assert.Contains("Dice.Roll", registered);
        Assert.Contains("Campaign.RecordJournalEntry", registered);
    }

    [Fact]
    public void Resolution_HasCorrectFunctions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (_, registered) = _provider.GetToolsForStage(ctx, SessionStage.Resolution);

        // CompleteResolution lives on EncounterTools, so its telemetry group is "Encounter".
        Assert.Contains("Encounter.CompleteResolution", registered);
        Assert.Contains("Campaign.AdvanceTime", registered);
        Assert.Contains("Campaign.RecordJournalEntry", registered);
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
