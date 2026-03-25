using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using WretchedWhispers.Api.Plugins.GameMasterPlugins;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Encounters;
using Xunit;

#pragma warning disable SKEXP0001

namespace WretchedWhispers.Tests.StateMachine;

public class StageTransitionTests
{
    [Theory]
    [InlineData(SessionStage.CharacterCreation, "Character", "CreateCharacter", SessionStage.CampaignSetup)]
    [InlineData(SessionStage.CampaignSetup, "Campaign", "StartCampaign", SessionStage.Exploration)]
    [InlineData(SessionStage.Exploration, "Encounter", "StartEncounter", SessionStage.Combat)]
    [InlineData(SessionStage.Combat, "Encounter", "EndEncounter", SessionStage.Resolution)]
    [InlineData(SessionStage.Resolution, "Resolution", "CompleteResolution", SessionStage.Exploration)]
    public void GetNextStage_returns_correct_transition(
        SessionStage currentStage, string pluginName, string functionName, SessionStage expectedNext)
    {
        var result = StageTransitions.GetNextStage(currentStage, pluginName, functionName);

        Assert.NotNull(result);
        Assert.Equal(expectedNext, result.Value);
    }

    [Theory]
    [InlineData(SessionStage.Exploration, "Character", "CreateCharacter")]
    [InlineData(SessionStage.Combat, "Campaign", "StartCampaign")]
    [InlineData(SessionStage.CharacterCreation, "Encounter", "StartEncounter")]
    [InlineData(SessionStage.Exploration, "Dice", "RollDice")]
    [InlineData(SessionStage.Combat, "Character", "ChallengeCharacter")]
    public void GetNextStage_returns_null_for_non_transition(
        SessionStage currentStage, string pluginName, string functionName)
    {
        var result = StageTransitions.GetNextStage(currentStage, pluginName, functionName);

        Assert.Null(result);
    }

    [Fact]
    public void Transitions_map_contains_exactly_5_entries()
    {
        var transitions = new[]
        {
            (SessionStage.CharacterCreation, "Character", "CreateCharacter"),
            (SessionStage.CampaignSetup, "Campaign", "StartCampaign"),
            (SessionStage.Exploration, "Encounter", "StartEncounter"),
            (SessionStage.Combat, "Encounter", "EndEncounter"),
            (SessionStage.Resolution, "Resolution", "CompleteResolution"),
        };

        var validCount = 0;
        foreach (var (stage, plugin, func) in transitions)
        {
            if (StageTransitions.GetNextStage(stage, plugin, func) is not null)
                validCount++;
        }

        Assert.Equal(5, validCount);
    }

    [Fact]
    public async Task StageTransitionFilter_allows_function_in_locked_stage()
    {
        var kernel = BuildTestKernel();
        var createCharFunc = kernel.Plugins.GetFunction("Character", "CreateCharacter");
        var allowedFunctions = new StagePluginRegistry().GetFunctionsForStage(SessionStage.CharacterCreation, kernel);

        var filter = new StageTransitionFilter(SessionStage.CharacterCreation, allowedFunctions);
        var context = CreateContext(kernel, createCharFunc);

        var nextCalled = false;
        await filter.OnAutoFunctionInvocationAsync(context, async ctx =>
        {
            nextCalled = true;
            await Task.CompletedTask;
        });

        Assert.True(nextCalled, "Filter must call next() for allowed functions");
        Assert.False(context.Terminate);
    }

    [Fact]
    public async Task StageTransitionFilter_blocks_function_not_in_locked_stage()
    {
        var kernel = BuildTestKernel();
        var startCampaignFunc = kernel.Plugins.GetFunction("Campaign", "StartCampaign");
        // Lock to CharacterCreation — StartCampaign should be blocked
        var allowedFunctions = new StagePluginRegistry().GetFunctionsForStage(SessionStage.CharacterCreation, kernel);

        var filter = new StageTransitionFilter(SessionStage.CharacterCreation, allowedFunctions);
        var context = CreateContext(kernel, startCampaignFunc);

        var nextCalled = false;
        await filter.OnAutoFunctionInvocationAsync(context, async ctx =>
        {
            nextCalled = true;
            await Task.CompletedTask;
        });

        Assert.False(nextCalled, "Filter must NOT call next() for blocked functions");
        Assert.True(context.Terminate);
        Assert.Contains("[BLOCKED]", context.Result.ToString());
    }

    [Fact]
    public async Task StageTransitionFilter_blocks_AdvanceTime_during_CharacterCreation()
    {
        var kernel = BuildTestKernel();
        var advanceTimeFunc = kernel.Plugins.GetFunction("Campaign", "AdvanceTime");
        var allowedFunctions = new StagePluginRegistry().GetFunctionsForStage(SessionStage.CharacterCreation, kernel);

        var filter = new StageTransitionFilter(SessionStage.CharacterCreation, allowedFunctions);
        var context = CreateContext(kernel, advanceTimeFunc);

        var nextCalled = false;
        await filter.OnAutoFunctionInvocationAsync(context, async ctx =>
        {
            nextCalled = true;
            await Task.CompletedTask;
        });

        Assert.False(nextCalled, "AdvanceTime must be blocked during CharacterCreation");
        Assert.True(context.Terminate);
    }

    [Fact]
    public async Task StageTransitionFilter_stage_stays_locked_regardless_of_state_mutations()
    {
        // Even if domain state changes mid-turn, the filter uses the locked stage
        var kernel = BuildTestKernel();
        var createCharFunc = kernel.Plugins.GetFunction("Character", "CreateCharacter");
        var configCampaignFunc = kernel.Plugins.GetFunction("Campaign", "ConfigureCampaign");
        var allowedFunctions = new StagePluginRegistry().GetFunctionsForStage(SessionStage.CharacterCreation, kernel);

        var filter = new StageTransitionFilter(SessionStage.CharacterCreation, allowedFunctions);

        // First call: CreateCharacter allowed
        var ctx1 = CreateContext(kernel, createCharFunc);
        var next1Called = false;
        await filter.OnAutoFunctionInvocationAsync(ctx1, async ctx =>
        {
            next1Called = true;
            await Task.CompletedTask;
        });
        Assert.True(next1Called);

        // Second call: ConfigureCampaign blocked (even though domain state may have changed)
        var ctx2 = CreateContext(kernel, configCampaignFunc);
        var next2Called = false;
        await filter.OnAutoFunctionInvocationAsync(ctx2, async ctx =>
        {
            next2Called = true;
            await Task.CompletedTask;
        });
        Assert.False(next2Called, "ConfigureCampaign must be blocked even after CreateCharacter mutates state");
        Assert.True(ctx2.Terminate);
    }

    private static Kernel BuildTestKernel()
    {
        var kernel = new Kernel();
        var context = new SessionContext();
        var charOps = new Mock<ICharacterOperations>().Object;
        var campOps = new Mock<ICampaignOperations>().Object;
        var campRepo = new Mock<ICampaignsRepository>().Object;
        var encOps = new Mock<IEncounterOperations>().Object;
        var encRepo = new Mock<IEncountersRepository>().Object;
        var diceOps = new Mock<IDiceOperations>().Object;

        kernel.ImportPluginFromObject(new CharacterWrapperPlugin(charOps, context, campRepo), "Character");
        kernel.ImportPluginFromObject(new CampaignWrapperPlugin(campOps, campRepo, context), "Campaign");
        kernel.ImportPluginFromObject(new EncounterWrapperPlugin(encOps, context), "Encounter");
        kernel.ImportPluginFromObject(new DiceWrapperPlugin(diceOps), "Dice");
        kernel.ImportPluginFromObject(new ResolutionWrapperPlugin(context, encRepo), "Resolution");

        return kernel;
    }

    private static AutoFunctionInvocationContext CreateContext(Kernel kernel, KernelFunction function)
    {
        var chatHistory = new ChatHistory();
        var chatMessage = new ChatMessageContent(AuthorRole.Assistant, "test");
        var functionResult = new FunctionResult(function);
        return new AutoFunctionInvocationContext(kernel, function, functionResult, chatHistory, chatMessage);
    }
}
