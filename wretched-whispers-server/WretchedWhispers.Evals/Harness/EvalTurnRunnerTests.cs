using Microsoft.Extensions.AI;
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

        Assert.Equal(new[] { "CreateCharacter" }, outcome.ToolCalls);
        var packagedNames = outcome.Response.Messages
            .SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.Name);
        Assert.Equal(new[] { "CreateCharacter" }, packagedNames);
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
}
