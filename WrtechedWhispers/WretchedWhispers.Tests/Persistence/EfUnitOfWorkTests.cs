using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

public class EfUnitOfWorkTests : SqliteTestBase
{
    [Fact]
    public async Task Commit_PersistsChanges()
    {
        var id = Guid.NewGuid();
        var uow = new EfUnitOfWork(Db);

        await using (var scope = await uow.BeginAsync(CancellationToken.None))
        {
            Db.Campaigns.Add(new CampaignEntity { Id = id, Data = "x", UserId = "u", Version = Guid.NewGuid() });
            await Db.SaveChangesAsync();
            await scope.CommitAsync(CancellationToken.None);
        }

        using var other = CreateSeparateContext();
        Assert.NotNull(await other.Campaigns.FindAsync(id));
    }

    [Fact]
    public async Task DisposeWithoutCommit_RollsBack()
    {
        var id = Guid.NewGuid();
        var uow = new EfUnitOfWork(Db);

        await using (var scope = await uow.BeginAsync(CancellationToken.None))
        {
            Db.Campaigns.Add(new CampaignEntity { Id = id, Data = "x", UserId = "u", Version = Guid.NewGuid() });
            await Db.SaveChangesAsync();
            // no CommitAsync — disposal must roll back
        }

        using var other = CreateSeparateContext();
        Assert.Null(await other.Campaigns.FindAsync(id));
    }
}
