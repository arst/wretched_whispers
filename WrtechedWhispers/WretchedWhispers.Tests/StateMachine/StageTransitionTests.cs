using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using WretchedWhispers.Api.Services;
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
    public async Task StageTransitionFilter_calls_next_before_checking_transition()
    {
        var sessionContext = new SessionContext { SessionId = Guid.NewGuid() };
        var filter = new StageTransitionFilter(sessionContext);

        // Build a kernel function with a known plugin name
        var kernel = new Kernel();
        var function = KernelFunctionFactory.CreateFromMethod(() => "result", "TestFunction");

        var chatHistory = new ChatHistory();
        var chatMessage = new ChatMessageContent(AuthorRole.Assistant, "test");
        var functionResult = new FunctionResult(function);
        var context = new AutoFunctionInvocationContext(kernel, function, functionResult, chatHistory, chatMessage);

        var nextCalled = false;
        await filter.OnAutoFunctionInvocationAsync(context, async ctx =>
        {
            nextCalled = true;
            await Task.CompletedTask;
        });

        Assert.True(nextCalled, "Filter must call next() before checking transitions");
    }

    [Fact]
    public async Task StageTransitionFilter_does_not_throw_for_non_transition_function()
    {
        var sessionContext = new SessionContext { SessionId = Guid.NewGuid() };
        var filter = new StageTransitionFilter(sessionContext);

        var kernel = new Kernel();
        var function = KernelFunctionFactory.CreateFromMethod(() => "result", "SomeFunction");

        var chatHistory = new ChatHistory();
        var chatMessage = new ChatMessageContent(AuthorRole.Assistant, "test");
        var functionResult = new FunctionResult(function);
        var context = new AutoFunctionInvocationContext(kernel, function, functionResult, chatHistory, chatMessage);

        // Should complete without throwing
        await filter.OnAutoFunctionInvocationAsync(context, ctx => Task.CompletedTask);
    }
}
