using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Bridges a producer delegate to an <see cref="IAsyncEnumerable{T}"/>. Exists to work around C#'s
/// rule that <c>yield return</c> cannot appear inside a <c>try/catch</c>: the producer writes events
/// into a channel (and may wrap its body in try/catch), while this method reads from the channel and
/// yields outside any try/catch. Domain-agnostic — it knows nothing about game events.
///
/// The producer is fire-and-forget; the channel is always completed (in a finally), and if the
/// producer throws, the channel is completed with that exception so the consumer rethrows it rather
/// than hanging.
/// </summary>
public static class AsyncStreamBridge
{
    public static async IAsyncEnumerable<T> Run<T>(
        Func<ChannelWriter<T>, CancellationToken, Task> produce,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        _ = ProduceAndCompleteAsync(produce, channel.Writer, ct);

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
            yield return item;
    }

    private static async Task ProduceAndCompleteAsync<T>(
        Func<ChannelWriter<T>, CancellationToken, Task> produce,
        ChannelWriter<T> writer,
        CancellationToken ct)
    {
        try
        {
            await produce(writer, ct);
            writer.Complete();
        }
        catch (Exception ex)
        {
            writer.Complete(ex);
        }
    }
}
