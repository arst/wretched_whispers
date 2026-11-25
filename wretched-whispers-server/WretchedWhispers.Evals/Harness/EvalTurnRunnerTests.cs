using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Evals.Harness;

public class EvalTurnRunnerTests
{
    // After a turn the coordinator rolls the chronicle's summary forward on a detached task with its own
    // scope, while the eval goes on to read what the turn did. On one shared connection the second
    // scope's setup hit the first one's open statement: SQLite Error 5, in a different eval every run.
    [Fact]
    public async Task TwoScopes_CanReadAtTheSameTime()
    {
        await using var host = await EvalHost.CreateAsync(new ScriptedChatClient());

        var campaigns = await host.QueryAsync(async first =>
        {
            var db = first.GetRequiredService<WretchedWhispersDbContext>();
            await using var open = db.Campaigns.AsAsyncEnumerable().GetAsyncEnumerator();
            Assert.True(await open.MoveNextAsync()); // a statement is now active on the first scope

            return await host.QueryAsync(second =>
                second.GetRequiredService<WretchedWhispersDbContext>().Campaigns.CountAsync());
        });

        Assert.Equal(1, campaigns);
    }

    [Fact]
    public async Task SixQuietExplorationTurns_ThenAnHourSlipsBy_AndTheNarratorIsTold()
    {
        // The whole rule through the real coordinator: six turns in which nothing passes time, and the
        // seventh opens an hour later, with Game State saying so.
        static ChatResponse Says(string text) => new(new ChatMessage(ChatRole.Assistant, text));
        var client = new ScriptedChatClient(Enumerable.Range(1, 7).Select(i => Says($"Ash falls. ({i})")).ToArray());
        await using var host = await EvalHost.CreateExplorationAsync(client);
        var ct = TestContext.Current.CancellationToken;

        Task<int> HourAsync() => host.QueryAsync(async sp =>
            (await sp.GetRequiredService<ISessionContextLoader>().LoadAsync(host.SessionId, ct)).Campaign!.CurrentHour);

        for (var turn = 1; turn <= 6; turn++)
            Assert.Null((await host.CreateTurnRunner().RunTurnAsync("I look around.", ct)).Error);
        Assert.Equal(0, await HourAsync());
        Assert.DoesNotContain("slipped by", client.Instructions[^1]);

        var seventh = await host.CreateTurnRunner().RunTurnAsync("I look around.", ct);

        Assert.Null(seventh.Error);
        Assert.Equal(1, await HourAsync());
        Assert.Contains("An hour has slipped by", client.Instructions[^1]);
        Assert.Equal(1, seventh.Delta?.HoursElapsed);
    }

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
    public async Task ToolCalledAfterStartEncounter_IsRejected_AndTheTurnCommitsInCombat()
    {
        // Plumbing for Exploration-CombatEntry-StopsAtStart: a model that starts combat and then
        // reaches for an ability test to "resolve" the declared attack gets that call rejected; the
        // turn still commits, in Combat, with nothing resolved.
        static ChatResponse Call(string id, string name, Dictionary<string, object?> args) =>
            new(new ChatMessage(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent(id, name, args) }));

        var client = new ScriptedChatClient(
            Call("c1", "CreateEncounter", new()
            {
                ["name"] = "Road Ambush",
                ["description"] = "A bandit blocks the road",
                ["initialEncounterType"] = "Hostile",
            }),
            Call("c2", "AddAdversaryToEncounter", new()
            {
                ["adversary"] = new Dictionary<string, object?>
                {
                    ["name"] = "Bandit",
                    ["hitPoints"] = 4,
                    ["armorTier"] = "None",
                    ["morale"] = 7,
                    ["weaponDescription"] = "Rusty knife",
                    ["weaponDamageDie"] = "1d4",
                },
            }),
            Call("c3", "StartEncounter", new()),
            Call("c4", "ChallengeCharacter", new() { ["challengeDr"] = 12, ["abilityKind"] = "Strength", ["hoursTaken"] = 0 }),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "The bandit bares his teeth. Your move.")));

        await using var host = await EvalHost.CreateExplorationAsync(client);

        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "I swing my staff at the bandit!", TestContext.Current.CancellationToken);

        Assert.Null(outcome.Error);
        Assert.Equal(
            new[] { "CreateEncounter", "AddAdversaryToEncounter", "StartEncounter", "ChallengeCharacter" },
            outcome.ToolCalls);
        Assert.DoesNotContain("ended this turn's actions", outcome.ToolResults[2].Result.ToString());
        Assert.Contains("StartEncounter ended this turn's actions", outcome.ToolResults[3].Result.ToString());
        Assert.Equal(0, outcome.Delta?.HpChange);

        var stage = await host.QueryAsync(async sp =>
            (await sp.GetRequiredService<ISessionContextLoader>()
                .LoadAsync(host.SessionId, TestContext.Current.CancellationToken)).DeriveStage());
        Assert.Equal(SessionStage.Combat, stage);
    }

    [Fact]
    public async Task NextTurn_ShowsTheModelWhichToolsRanBefore_ButTheStoredChronicleStaysProse()
    {
        // Turn 1 runs a tool (and has one call refused). On turn 2 the model's request must carry an
        // engine record of the successful call right after turn 1's player message — while the
        // chronicle the player reads holds only the two plain messages.
        static ChatResponse Call(string id, string name, Dictionary<string, object?> args) =>
            new(new ChatMessage(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent(id, name, args) }));

        var client = new ScriptedChatClient(
            Call("c1", "Roll", new() { ["diceExpression"] = "banana" }), // refused by validation
            Call("c2", "Roll", new() { ["diceExpression"] = "d6" }),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "The bones clatter in the dust.")),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Nothing stirs.")));

        await using var host = await EvalHost.CreateExplorationAsync(client);
        var ct = TestContext.Current.CancellationToken;

        await host.CreateTurnRunner().RunTurnAsync("I cast the bones.", ct);
        await host.CreateTurnRunner().RunTurnAsync("I wait.", ct);

        // Turn 1's own requests show no record: there is nothing before it.
        Assert.DoesNotContain(client.Requests[0], m => m.Role == ChatRole.System);

        var turn2 = client.Requests[^1];
        Assert.Equal(new[] { "user", "system", "assistant", "user" }, turn2.Select(m => m.Role.Value));
        Assert.Equal("I cast the bones.", turn2[0].Text);
        Assert.Equal(
            "[Engine record, not shown to the player — tools run for the message above: Roll(diceExpression: \"d6\")]",
            turn2[1].Text);
        Assert.Equal("The bones clatter in the dust.", turn2[2].Text);

        // What is stored — and served to the player — is the conversation only.
        var stored = await host.QueryAsync(sp =>
            sp.GetRequiredService<IChatHistoryRepository>().LoadSession(host.ChatSessionId, ct));
        Assert.Equal(
            new[] { "I cast the bones.", "The bones clatter in the dust.", "I wait.", "Nothing stirs." },
            stored!.Select(m => m.Text));
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
