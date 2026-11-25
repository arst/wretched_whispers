using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace WretchedWhispers.Evals.Harness;

/// <summary>
/// Answers one model request with a tool call the scenario chose, and passes every other request to
/// the real model. For evals about how a result is TOLD: whether the narrator calls the tool, and with
/// what arguments, is then not left to chance, and only the telling is the model's.
/// </summary>
public sealed class ForcedCallChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    private ChatResponse? _next;

    /// <summary>The next request is answered with this call instead of being sent to the model.</summary>
    public void ForceNextCall(string tool, Dictionary<string, object?> arguments) =>
        _next = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("forced_call", tool, arguments)]));

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Take() is { } forced ? Task.FromResult(forced) : base.GetResponseAsync(messages, options, cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Take() is { } forced)
        {
            foreach (var update in forced.ToChatResponseUpdates())
                yield return update;
            yield break;
        }

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            yield return update;
    }

    private ChatResponse? Take()
    {
        var forced = _next;
        _next = null;
        return forced;
    }
}
