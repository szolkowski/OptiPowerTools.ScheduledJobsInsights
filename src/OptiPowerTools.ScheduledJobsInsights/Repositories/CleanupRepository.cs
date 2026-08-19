using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Repositories;

/// <summary>Default <see cref="ICleanupRepository"/>.</summary>
internal sealed class CleanupRepository : ICleanupRepository
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;

    public CleanupRepository(IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public int DeleteExecutionsOlderThan(DateTimeOffset cutoff, int batchSize)
    {
        // No OrderBy: correctness of the convergent batch-delete loop in ScheduledJobsInsightsCleanupJob
        // doesn't depend on which rows go first, only that each batch shrinks the remaining set.
        using var dbContext = _dbContextFactory.CreateDbContext();
        return dbContext.JobExecutions
            .Where(e => e.StartedAt < cutoff)
            .Take(batchSize)
            .ExecuteDelete();
    }
}
