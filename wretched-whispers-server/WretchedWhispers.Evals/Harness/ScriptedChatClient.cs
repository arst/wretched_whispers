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
        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty));
        return Task.FromResult(response);
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
