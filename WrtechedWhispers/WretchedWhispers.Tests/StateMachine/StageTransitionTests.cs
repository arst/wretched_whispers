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
    public async Task StageTransitionFilter_allows_function_in_current_stage()
    {
        // CharacterCreation stage — CreateCharacter should be allowed
        var (filter, kernel) = CreateFilterWithKernel();

        var createCharFunc = kernel.Plugins.GetFunction("Character", "CreateCharacter");
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
    public async Task StageTransitionFilter_blocks_function_not_in_current_stage()
    {
        // CharacterCreation stage — StartCampaign should be blocked
        var (filter, kernel) = CreateFilterWithKernel();

        var startCampaignFunc = kernel.Plugins.GetFunction("Campaign", "StartCampaign");
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
        var (filter, kernel) = CreateFilterWithKernel();

        var advanceTimeFunc = kernel.Plugins.GetFunction("Campaign", "AdvanceTime");
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

    private static (StageTransitionFilter filter, Kernel kernel) CreateFilterWithKernel()
    {
        var sessionContext = new SessionContext { SessionId = Guid.NewGuid() };
        // No character → DeriveStage returns CharacterCreation
        var registry = new StagePluginRegistry();

        var kernel = new Kernel();
        var charOps = new Mock<ICharacterOperations>().Object;
        var campOps = new Mock<ICampaignOperations>().Object;
        var campRepo = new Mock<ICampaignsRepository>().Object;
        var encOps = new Mock<IEncounterOperations>().Object;
        var encRepo = new Mock<IEncountersRepository>().Object;
        var diceOps = new Mock<IDiceOperations>().Object;

        kernel.ImportPluginFromObject(new CharacterWrapperPlugin(charOps, sessionContext, campRepo), "Character");
        kernel.ImportPluginFromObject(new CampaignWrapperPlugin(campOps, campRepo, sessionContext), "Campaign");
        kernel.ImportPluginFromObject(new EncounterWrapperPlugin(encOps, sessionContext), "Encounter");
        kernel.ImportPluginFromObject(new DiceWrapperPlugin(diceOps), "Dice");
        kernel.ImportPluginFromObject(new ResolutionWrapperPlugin(sessionContext, encRepo), "Resolution");

        var filter = new StageTransitionFilter(sessionContext, registry, kernel);
        return (filter, kernel);
    }

    private static AutoFunctionInvocationContext CreateContext(Kernel kernel, KernelFunction function)
    {
        var chatHistory = new ChatHistory();
        var chatMessage = new ChatMessageContent(AuthorRole.Assistant, "test");
        var functionResult = new FunctionResult(function);
        return new AutoFunctionInvocationContext(kernel, function, functionResult, chatHistory, chatMessage);
    }
}
