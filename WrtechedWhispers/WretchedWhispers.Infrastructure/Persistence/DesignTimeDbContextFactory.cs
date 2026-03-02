using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>
/// Factory used by EF Core design-time tools (dotnet ef migrations add, etc.)
/// to create a DbContext without requiring the full application startup.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WretchedWhispersDbContext>
{
    public WretchedWhispersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WretchedWhispersDbContext>()
            .UseSqlite("Data Source=./wretched-whispers.db")
            .Options;

        return new WretchedWhispersDbContext(options);
    }
}
