using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Evals.Harness;

public class EvalTurnRunnerTests
{
    [Fact]
    public async Task RunTurn_CapturesToolCallOrder_FromExecutorEvents()
    {
        // The scripted model: first call requests CreateCharacter, second call narrates the result.
        var client = new ScriptedChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call_1", "CreateCharacter", new Dictionary<string, object?> { ["name"] = "Grim" })
            })),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Grim claws free of the muck.")));

        await using var host = await EvalHost.CreateAsync(client);
        var runner = host.CreateTurnRunner();

        var outcome = await runner.RunTurnAsync("Grim", TestContext.Current.CancellationToken);

        Assert.Null(outcome.Error);
        Assert.Equal(new[] { "CreateCharacter" }, outcome.ToolCalls);
        var packagedNames = outcome.Response.Messages
            .SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.Name);
        Assert.Equal(new[] { "CreateCharacter" }, packagedNames);
    }

    [Fact]
    public async Task RunTurn_ExposesToolCallArguments()
    {
        var client = new ScriptedChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call_1", "CreateCharacter", new Dictionary<string, object?> { ["name"] = "Grim" })
            })),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Grim claws free of the muck.")));

        await using var host = await EvalHost.CreateAsync(client);

        var outcome = await host.CreateTurnRunner().RunTurnAsync("Grim", TestContext.Current.CancellationToken);

        var call = Assert.Single(outcome.Calls);
        Assert.Equal("CreateCharacter", call.Name);
        Assert.NotNull(call.Arguments);
        using (var args = JsonDocument.Parse(call.Arguments))
            Assert.Equal("Grim", args.RootElement.GetProperty("name").GetString());

        // The evaluator-facing response carries the same arguments.
        var packaged = outcome.Response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("Grim", ((JsonElement)packaged.Arguments!["name"]!).GetString());
    }

    [Fact]
    public async Task RunTurn_ModelSeesEachPlayerMessageExactlyOnce()
    {
        var client = new ScriptedChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "What name is carved into your hide?")),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Grim, then. The mud remembers.")));

        await using var host = await EvalHost.CreateAsync(client);

        await host.CreateTurnRunner().RunTurnAsync("begin", TestContext.Current.CancellationToken);
        await host.CreateTurnRunner().RunTurnAsync("Grim", TestContext.Current.CancellationToken);

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(1, CountUserMessages(client.Requests[0], "begin"));
        // Turn 2 sees turn 1's message from history once, and its own message once.
        Assert.Equal(1, CountUserMessages(client.Requests[1], "begin"));
        Assert.Equal(1, CountUserMessages(client.Requests[1], "Grim"));
    }

    [Fact]
    public async Task RunTurn_EmptyModelResponse_ReportsErrorAndPersistsNothing()
    {
        var client = new ScriptedChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

        await using var host = await EvalHost.CreateAsync(client);

        var outcome = await host.CreateTurnRunner().RunTurnAsync("begin", TestContext.Current.CancellationToken);

        Assert.NotNull(outcome.Error);
        Assert.Empty(outcome.Calls);
        Assert.Empty(outcome.Narrative);
        var history = await host.QueryAsync(sp => sp.GetRequiredService<IChatHistoryRepository>()
            .LoadSession(host.ChatSessionId, TestContext.Current.CancellationToken));
        Assert.Empty(history ?? []);
    }

    [Fact]
    public async Task TwoAdversaryCombat_PromptListsBothByName_AndTargetArgumentIsCaptured()
    {
        // Plumbing for Combat-TwoAdversaries-TargetsByName: the model must SEE both adversary names in
        // Game State, and the target it picks must reach the scenario through TurnOutcome.Calls.
        var client = new ScriptedChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call_1", "ResolveCombatRound", new Dictionary<string, object?>
                {
                    ["action"] = "Attack",
                    ["targetAdversaryName"] = "Grave Hound",
                })
            })),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Your staff cracks across the hound's spine.")));

        await using var host = await EvalHost.CreateTwoAdversaryCombatAsync(client);

        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "I bring my staff down on the snarling dog!", TestContext.Current.CancellationToken);

        Assert.Null(outcome.Error);
        Assert.Contains("Plague Priest — HP 4/", client.Instructions[0]);
        Assert.Contains("Grave Hound — HP 3/", client.Instructions[0]);
        var round = Assert.Single(outcome.Calls);
        Assert.Equal("ResolveCombatRound", round.Name);
        using var args = JsonDocument.Parse(round.Arguments!);
        Assert.Equal("Grave Hound", args.RootElement.GetProperty("targetAdversaryName").GetString());
    }

    [Fact]
    public async Task SecondResolveCombatRoundInATurn_IsRejected_ThroughTheRealPipeline()
    {
        // Plumbing for Combat-DoubleAction-OneRound: a model that asks for two rounds in one turn gets
        // the second rejected with a readable error, and the outcome still records both attempts.
        var attack = new Dictionary<string, object?> { ["action"] = "Attack", ["targetAdversaryName"] = "Priest" };
        var client = new ScriptedChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call_1", "ResolveCombatRound", attack),
                new FunctionCallContent("call_2", "ResolveCombatRound", attack),
            })),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Your staff finds the priest's skull.")));

        await using var host = await EvalHost.CreateCombatAsync(client);

        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "I smash the priest twice!", TestContext.Current.CancellationToken);

        Assert.Null(outcome.Error);
        Assert.Equal(new[] { "ResolveCombatRound", "ResolveCombatRound" }, outcome.ToolCalls);
        Assert.Equal(2, outcome.ToolResults.Count);
        Assert.DoesNotContain("already called this turn", outcome.ToolResults[0].Result.ToString());
        Assert.Contains("ResolveCombatRound was already called this turn", outcome.ToolResults[1].Result.ToString());
    }

    [Fact]
    public async Task Narrative_IsEverythingShown_AndTheProseBeforeTheToolIsSeparable()
    {
        // Plumbing for Combat-NoOutcomeBeforeTheRoll: nothing the model writes is withheld, and the
        // part it wrote before any tool result is available to judge by itself.
        var client = new ScriptedChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new TextContent("You raise your staff."),
                new FunctionCallContent("call_1", "ResolveCombatRound", new Dictionary<string, object?>
                {
                    ["action"] = "Attack",
                    ["targetAdversaryName"] = "Priest",
                })
            })),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "The blow lands true.")));

        await using var host = await EvalHost.CreateCombatAsync(client);

        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "I strike the priest!", TestContext.Current.CancellationToken);

        Assert.Null(outcome.Error);
        Assert.Equal("You raise your staff.", outcome.NarrativeBeforeTools);
        Assert.Equal("You raise your staff.\n\nThe blow lands true.", outcome.Narrative);
    }

    [Fact]
    public async Task NoToolTurn_HasNoNarrativeBeforeTools()
    {
        var client = new ScriptedChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "The priest watches you, unblinking.")));

        await using var host = await EvalHost.CreateCombatAsync(client);

        var outcome = await host.CreateTurnRunner().RunTurnAsync("I wait.", TestContext.Current.CancellationToken);

        Assert.Equal("", outcome.NarrativeBeforeTools);
        Assert.Equal("The priest watches you, unblinking.", outcome.Narrative);
    }

    [Fact]
    public async Task LongChronicle_IsSummarizedAfterTheTurn_AndTheNextTurnUsesTheSummary()
    {
        // 160 stored messages of ~100 tokens each is past the summarizer's 12,000-token trigger. The
        // turn itself must not summarize: its one model request carries the whole history. The summary
        // is written afterwards, and the following turn sends it in place of the messages it covers.
        var client = new ScriptedChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "The road goes on.")),      // turn 1
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "SUMMARY: much walking.")), // post-turn summarizer
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "And on.")));               // turn 2

        await using var host = await EvalHost.CreateExplorationAsync(client);
        var ct = TestContext.Current.CancellationToken;
        await host.QueryAsync(async sp =>
        {
            var repo = sp.GetRequiredService<IChatHistoryRepository>();
            for (var i = 0; i < 160; i++)
                await repo.SaveMessage(host.ChatSessionId,
                    new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant,
                        $"old message {i} ".PadRight(384, '.')), ct);
            return 0;
        });

        var turn1 = await host.CreateTurnRunner().RunTurnAsync("I walk on.", ct);

        Assert.Null(turn1.Error);
        Assert.Equal(161, client.Requests[0].Count); // 160 stored + this message, nothing summarized yet
        Assert.DoesNotContain(client.Requests[0], m => m.Role == ChatRole.System);

        // The summary lands after the turn has ended.
        ChatSummary? summary = null;
        for (var i = 0; i < 200 && summary is null; i++)
        {
            summary = await host.QueryAsync(sp =>
                sp.GetRequiredService<IChatHistoryRepository>().GetSummary(host.ChatSessionId, ct));
            if (summary is null) await Task.Delay(25, ct);
        }
        Assert.NotNull(summary);
        Assert.Equal("SUMMARY: much walking.", summary!.Text);

        // 162 messages are stored after turn 1. The newest that fit the 8,000-token keep budget stay
        // verbatim (the two short turn-1 messages plus 79 of the ~100-token ones); the rest are covered.
        var kept = 162 - summary.CoveredCount;
        Assert.InRange(kept, 70, 90);

        await host.CreateTurnRunner().RunTurnAsync("Still walking.", ct);

        var turn2Request = client.Requests[2];
        Assert.Contains("SUMMARY: much walking.", turn2Request[0].Text);
        Assert.Equal(1 + kept + 1, turn2Request.Count); // summary + uncovered tail + this message
        Assert.True(turn2Request.Skip(1).Sum(TokenEstimate.For) <= 8_000 + 50);
    }

    [Fact]
    public async Task TwoTurns_ShareState_AcrossSeparateScopes()
    {
        // Turn 1: the model asks for a name, no tools (1 response).
        // Turn 2: the model creates the character (2 responses: tool-call + narration).
        var client = new ScriptedChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "What name is carved into your hide?")),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call_1", "CreateCharacter", new Dictionary<string, object?> { ["name"] = "Grim" })
            })),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Grim claws free of the muck.")));

        await using var host = await EvalHost.CreateAsync(client);

        var turn1 = await host.CreateTurnRunner().RunTurnAsync("begin", TestContext.Current.CancellationToken);
        Assert.Empty(turn1.ToolCalls);

        var turn2 = await host.CreateTurnRunner().RunTurnAsync("Grim", TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "CreateCharacter" }, turn2.ToolCalls);
    }

    private static int CountUserMessages(IReadOnlyList<ChatMessage> messages, string text) =>
        messages.Count(m => m.Role == ChatRole.User && m.Text == text);
}
