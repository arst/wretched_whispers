using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace WretchedWhispers.Evals.Harness;

/// <summary>Replays a fixed queue of ChatResponses — used only to test the harness plumbing without a real model.</summary>
public sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new(responses);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Fail loudly on an exhausted script: silently returning an empty response would read as
        // "the agent chose to say nothing" and mask a mis-scripted test.
        if (_responses.Count == 0)
            throw new InvalidOperationException(
                "ScriptedChatClient queue is empty — the harness made more model calls than the script provided.");
        return Task.FromResult(_responses.Dequeue());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var message in response.Messages)
            yield return new ChatResponseUpdate(message.Role, message.Contents);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
