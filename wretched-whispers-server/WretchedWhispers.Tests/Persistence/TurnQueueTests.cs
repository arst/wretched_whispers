using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

/// <summary>
/// The (user, client request id) contract. Reusing an id for a different action is a client bug, and
/// it is reported through the result rather than thrown: it used to raise an InvalidOperationException
/// that the endpoint caught alongside every other one and echoed straight back to the caller.
/// </summary>
public class TurnQueueTests : SqliteTestBase
{
    private const string UserId = "test-user";

    [Fact]
    public async Task Enqueue_CreatesOneTurn()
    {
        var queue = new TurnQueue(Db);

        var result = await queue.EnqueueAsync(
            Guid.NewGuid(), UserId, Guid.NewGuid(), "I open the door.", CancellationToken.None);

        Assert.True(result.Created);
        Assert.NotNull(result.Turn);
    }

    [Fact]
    public async Task Enqueue_ReplayingTheSameSubmission_ReturnsTheOriginalTurn()
    {
        var queue = new TurnQueue(Db);
        var campaignId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var first = await queue.EnqueueAsync(
            campaignId, UserId, requestId, "I open the door.", CancellationToken.None);
        var replay = await queue.EnqueueAsync(
            campaignId, UserId, requestId, "I open the door.", CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.Turn!.Id, replay.Turn!.Id);
    }

    [Theory]
    [InlineData(true, false)]  // same id, different campaign
    [InlineData(false, true)]  // same id, different message
    public async Task Enqueue_ReusingARequestIdForADifferentAction_ReturnsNoTurn(
        bool differentCampaign, bool differentMessage)
    {
        var queue = new TurnQueue(Db);
        var campaignId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await queue.EnqueueAsync(campaignId, UserId, requestId, "I open the door.", CancellationToken.None);

        var reused = await queue.EnqueueAsync(
            differentCampaign ? Guid.NewGuid() : campaignId,
            UserId,
            requestId,
            differentMessage ? "I flee instead." : "I open the door.",
            CancellationToken.None);

        Assert.Null(reused.Turn);
        Assert.False(reused.Created);
    }

    [Fact]
    public async Task Enqueue_SameRequestIdFromADifferentUser_IsItsOwnTurn()
    {
        var queue = new TurnQueue(Db);
        var requestId = Guid.NewGuid();

        var mine = await queue.EnqueueAsync(
            Guid.NewGuid(), UserId, requestId, "I open the door.", CancellationToken.None);
        var theirs = await queue.EnqueueAsync(
            Guid.NewGuid(), "someone-else", requestId, "I open the door.", CancellationToken.None);

        Assert.True(theirs.Created);
        Assert.NotEqual(mine.Turn!.Id, theirs.Turn!.Id);
    }
}
