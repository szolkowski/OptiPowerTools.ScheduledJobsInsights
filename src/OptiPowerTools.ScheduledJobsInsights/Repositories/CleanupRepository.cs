using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Repositories;

/// <summary>Default <see cref="ICleanupRepository"/>.</summary>
/// <remarks>
/// <para>
/// Neither delete uses an <c>OrderBy</c>: the convergent batch loop in
/// <see cref="Jobs.ScheduledJobsInsightsCleanupJob"/> only needs each batch to shrink the remaining
/// set, not to shrink it in any particular order.
/// </para>
/// <para>
/// Cancellation is checked before a batch is issued rather than passed to EF Core. The synchronous
/// <c>ExecuteDelete</c> has no token overload, and the async one would surface a stop as an
/// exception thrown into a job this package is only supposed to observe. Declining to start the
/// next batch stops the sweep just as promptly and leaves no batch half-applied.
/// </para>
/// </remarks>
internal sealed class CleanupRepository : ICleanupRepository
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;

    public CleanupRepository(IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public int DeleteExecutionsOlderThan(
        DateTimeOffset cutoff,
        int batchSize,
        IReadOnlyCollection<string> excludedJobTypeNames,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return 0;

        using var dbContext = _dbContextFactory.CreateDbContext();

        var query = dbContext.JobExecutions.Where(e => e.StartedAt < cutoff);

        // Translated to NOT IN. The list is the number of jobs with their own rule — a handful in
        // practice — so this stays a small predicate rather than a large parameter list.
        if (excludedJobTypeNames.Count > 0)
            query = query.Where(e => !excludedJobTypeNames.Contains(e.JobTypeName));

        return query.Take(batchSize).ExecuteDelete();
    }

    public int DeleteExecutionsOlderThan(
        string jobTypeName,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return 0;

        using var dbContext = _dbContextFactory.CreateDbContext();

        // Seeks the (JobTypeName, StartedAt) index. Without it this scans the age range and filters,
        // which is the bad case whenever a job's retention is shorter than the default.
        return dbContext.JobExecutions
            .Where(e => e.JobTypeName == jobTypeName && e.StartedAt < cutoff)
            .Take(batchSize)
            .ExecuteDelete();
    }
}
