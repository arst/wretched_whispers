using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
