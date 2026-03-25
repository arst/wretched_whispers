using Microsoft.SemanticKernel;
using Moq;
using WretchedWhispers.Api.Plugins.GameMasterPlugins;
using WretchedWhispers.Api.Services;
using Xunit;

namespace WretchedWhispers.Tests.StateMachine;

public class StagePluginRegistryTests
{
    private readonly StagePluginRegistry _registry = new();
    private readonly Kernel _kernel;

    public StagePluginRegistryTests()
    {
        var builder = Kernel.CreateBuilder();
        _kernel = builder.Build();

        // Import wrapper plugins with mock dependencies
        var context = new SessionContext();
        var charOps = new Mock<ICharacterOperations>().Object;
        var campOps = new Mock<ICampaignOperations>().Object;
        var encOps = new Mock<IEncounterOperations>().Object;
        var diceOps = new Mock<IDiceOperations>().Object;

        _kernel.ImportPluginFromObject(new CharacterWrapperPlugin(charOps, context), "Character");
        _kernel.ImportPluginFromObject(new CampaignWrapperPlugin(campOps, context), "Campaign");
        _kernel.ImportPluginFromObject(new EncounterWrapperPlugin(encOps, context), "Encounter");
        _kernel.ImportPluginFromObject(new DiceWrapperPlugin(diceOps), "Dice");
        _kernel.ImportPluginFromObject(new ResolutionWrapperPlugin(context), "Resolution");
    }

    [Fact]
    public void CharacterCreation_ReturnsExactlyOneFunction()
    {
        var functions = _registry.GetFunctionsForStage(SessionStage.CharacterCreation, _kernel);

        Assert.Single(functions);
        Assert.Equal("CreateCharacter", functions[0].Name);
    }

    [Fact]
    public void CampaignSetup_ReturnsExactlyThreeFunctions()
    {
        var functions = _registry.GetFunctionsForStage(SessionStage.CampaignSetup, _kernel);

        Assert.Equal(3, functions.Count);
        var names = functions.Select(f => f.Name).ToList();
        Assert.Contains("CreateCampaign", names);
        Assert.Contains("AddCharacterToCampaign", names);
        Assert.Contains("StartCampaign", names);
    }

    [Fact]
    public void Exploration_ReturnsTenOrMoreFunctions()
    {
        var functions = _registry.GetFunctionsForStage(SessionStage.Exploration, _kernel);

        Assert.True(functions.Count >= 10, $"Expected >= 10 functions for Exploration, got {functions.Count}");
        var names = functions.Select(f => f.Name).ToList();
        Assert.Contains("CreateEncounter", names);
        Assert.Contains("AddAdversaryToEncounter", names);
        Assert.Contains("StartEncounter", names);
        Assert.Contains("AdvanceTime", names);
        Assert.Contains("Rest", names);
        Assert.Contains("ChallengeCharacter", names);
        Assert.Contains("Roll", names);
    }

    [Fact]
    public void Combat_ReturnsExactlyFourFunctions()
    {
        var functions = _registry.GetFunctionsForStage(SessionStage.Combat, _kernel);

        Assert.Equal(4, functions.Count);
        var names = functions.Select(f => f.Name).ToList();
        Assert.Contains("AttackPlayer", names);
        Assert.Contains("AttackAdversary", names);
        Assert.Contains("EndEncounter", names);
        Assert.Contains("Roll", names);
    }

    [Fact]
    public void Resolution_ReturnsEightOrMoreFunctions()
    {
        var functions = _registry.GetFunctionsForStage(SessionStage.Resolution, _kernel);

        Assert.True(functions.Count >= 8, $"Expected >= 8 functions for Resolution, got {functions.Count}");
        var names = functions.Select(f => f.Name).ToList();
        Assert.Contains("AddItemToCharacterInventory", names);
        Assert.Contains("RemoveItemFromCharacterInventory", names);
        Assert.Contains("InfectCharacter", names);
        Assert.Contains("CureInfection", names);
        Assert.Contains("ImproveCharacterAbility", names);
        Assert.Contains("DegradeCharacterAbility", names);
        Assert.Contains("AdvanceTime", names);
        Assert.Contains("CompleteResolution", names);
    }

    [Fact]
    public void Ended_ReturnsEmptyList()
    {
        var functions = _registry.GetFunctionsForStage(SessionStage.Ended, _kernel);

        Assert.Empty(functions);
    }

    [Fact]
    public void AllReturnedFunctions_AreResolvableFromKernel()
    {
        // Verify no KeyNotFoundException is thrown for any stage
        foreach (var stage in Enum.GetValues<SessionStage>())
        {
            var functions = _registry.GetFunctionsForStage(stage, _kernel);
            Assert.All(functions, f => Assert.NotNull(f));
        }
    }
}
