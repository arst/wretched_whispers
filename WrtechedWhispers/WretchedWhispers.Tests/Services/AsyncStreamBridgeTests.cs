using System.Threading.Channels;
using WretchedWhispers.Api.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class AsyncStreamBridgeTests
{
    [Fact]
    public async Task Run_YieldsItemsWrittenByProducer_InOrder_ThenCompletes()
    {
        var stream = AsyncStreamBridge.Run<int>(async (writer, _) =>
        {
            writer.TryWrite(1);
            writer.TryWrite(2);
            writer.TryWrite(3);
            await Task.CompletedTask;
        }, CancellationToken.None);

        var items = new List<int>();
        await foreach (var i in stream)
            items.Add(i);

        Assert.Equal(new[] { 1, 2, 3 }, items);
    }

    [Fact]
    public async Task Run_WhenProducerThrows_SurfacesExceptionToConsumer_WithoutHanging()
    {
        var stream = AsyncStreamBridge.Run<int>((writer, _) =>
        {
            writer.TryWrite(1);
            throw new InvalidOperationException("boom");
        }, CancellationToken.None);

        var items = new List<int>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var i in stream)
                items.Add(i);
        });

        Assert.Equal("boom", ex.Message);
        Assert.Equal(new[] { 1 }, items); // items written before the throw still drain
    }

    [Fact]
    public async Task Run_WhenTokenAlreadyCancelled_StopsConsumer()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var stream = AsyncStreamBridge.Run<int>(async (writer, _) =>
        {
            writer.TryWrite(1);
            await Task.CompletedTask;
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in stream)
            {
            }
        });
    }

    [Fact]
    public async Task Run_WhenProducerWritesNothing_YieldsEmptySequence_AndCompletes()
    {
        var stream = AsyncStreamBridge.Run<int>((_, _) => Task.CompletedTask, CancellationToken.None);

        var items = new List<int>();
        await foreach (var i in stream)
            items.Add(i);

        Assert.Empty(items);
    }
}
