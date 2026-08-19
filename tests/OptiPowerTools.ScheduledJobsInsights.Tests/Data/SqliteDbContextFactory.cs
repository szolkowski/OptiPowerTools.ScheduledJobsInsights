using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Data;

/// <summary>
/// Test-only <see cref="IDbContextFactory{TContext}"/> backed by a Sqlite in-memory database, kept
/// alive for the lifetime of this instance via a single open connection. Sqlite is used instead of
/// the EF Core InMemory provider because <c>ExecuteDelete</c> (used by the cleanup job) isn't
/// supported by InMemory, and Sqlite enforces FK/cascade behavior InMemory ignores.
/// </summary>
/// <remarks>
/// This validates the C#-side query/repository/cascade logic only — the production DDL is SQL
/// Server-specific and is exercised only by running the <c>.Web</c> dev host against real SQL Server.
/// </remarks>
internal sealed class SqliteDbContextFactory : IDbContextFactory<ScheduledJobsInsightsDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ScheduledJobsInsightsDbContext> _options;

    public SqliteDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<ScheduledJobsInsightsDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var dbContext = CreateDbContext();
        dbContext.Database.EnsureCreated();
    }

    public ScheduledJobsInsightsDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
