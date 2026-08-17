using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

public class TurnEventStoreTests : SqliteTestBase
{
    [Fact]
    public async Task TerminalEvents_AreUniquePerTurn()
    {
        var turnId = Guid.NewGuid();
        Db.TurnEvents.Add(new TurnEventEntity
        {
            Id = Guid.NewGuid(),
            TurnId = turnId,
            Sequence = 1,
            EventType = "done",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();

        Db.TurnEvents.Add(new TurnEventEntity
        {
            Id = Guid.NewGuid(),
            TurnId = turnId,
            Sequence = 2,
            EventType = "error",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }
}
