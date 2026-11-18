using Microsoft.Extensions.AI;

namespace WretchedWhispers.Evals.Harness;

/// <summary>Counts the model calls that pass through it, so a scenario can assert that a turn never
/// reached the model (e.g. the coordinator refused it).</summary>
public sealed class RequestCountingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }
}
