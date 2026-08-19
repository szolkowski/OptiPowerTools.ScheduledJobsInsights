using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OptiPowerTools.ScheduledJobsInsights.Data;

/// <summary>
/// Design-time factory used only by <c>dotnet ef migrations add</c> — this library has no
/// <c>Startup</c>/<c>Program</c> of its own for the EF Core tooling to discover services from.
/// The connection string here is never used at runtime; it only needs to be valid enough for the
/// SQL Server provider to generate migration SQL.
/// </summary>
internal sealed class ScheduledJobsInsightsDbContextFactory : IDesignTimeDbContextFactory<ScheduledJobsInsightsDbContext>
{
    public ScheduledJobsInsightsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ScheduledJobsInsightsDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost;Database=DesignTime;Trusted_Connection=True;");
        return new ScheduledJobsInsightsDbContext(optionsBuilder.Options);
    }
}
