using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
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
/// Neither delete touches a <see cref="ExecutionStatus.Running"/> row, whatever its age. A job can
/// legitimately run for longer than its own retention — a 25-hour import under a one-day rule — and
/// deleting the row underneath it loses the run's whole history: the still-buffered log lines then
/// violate the foreign key, which poisons the batch they travel in and takes other jobs' lines with
/// them, while <c>Complete</c> updates nothing and reports nothing. Age alone cannot distinguish
/// "stranded" from "still working"; that is what <see cref="MarkInterruptedExecutions"/> is for, and
/// it runs first.
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
    /// <summary>
    /// Most job-type names inlined into a <c>NOT IN</c> before switching to the include-list form.
    /// </summary>
    /// <remarks>
    /// SQL Server allows roughly 2,100 parameters per statement, and each excluded name is one. Well
    /// under that here, because the failure mode is a batch that cannot be issued at all — the default
    /// sweep would stop working with nothing to say why.
    /// </remarks>
    private const int MaxInlinedExclusions = 500;

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

        var query = dbContext.JobExecutions
            .Where(e => e.StartedAt < cutoff && e.Status != ExecutionStatus.Running);

        // Translated to NOT IN, one parameter per name. The list is the number of job types with a
        // rule of their own — a handful in practice — but it also includes every type present only in
        // history, which is unbounded over an installation's lifetime. Past SQL Server's ~2,100
        // parameter limit the batch would fail outright and the default sweep would silently stop
        // working, so an oversized list is joined against the temp-table form instead of inlined.
        if (excludedJobTypeNames.Count > MaxInlinedExclusions)
        {
            var excluded = excludedJobTypeNames.ToHashSet(StringComparer.Ordinal);

            // Evaluated locally: reading the distinct names is one indexed scan, and the alternative
            // is a statement that cannot be issued at all.
            var deletable = query
                .Select(e => e.JobTypeName)
                .Distinct()
                .ToList()
                .Where(name => !excluded.Contains(name))
                .Take(MaxInlinedExclusions)
                .ToList();

            if (deletable.Count == 0)
                return 0;

            return query.Where(e => deletable.Contains(e.JobTypeName)).Take(batchSize).ExecuteDelete();
        }

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
            .Where(e => e.JobTypeName == jobTypeName
                && e.StartedAt < cutoff
                && e.Status != ExecutionStatus.Running)
            .Take(batchSize)
            .ExecuteDelete();
    }

    public int MarkInterruptedExecutions(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return 0;

        using var dbContext = _dbContextFactory.CreateDbContext();

        // CompletedAt is left null on purpose: nothing is known about when the run actually ended,
        // and inventing a completion time would make the duration column lie.
        //
        // Batched, like the deletes above. Unbounded, this took row and page locks across JobExecutions
        // in one transaction — which on the first run after an upgrade, against an installation with a
        // backlog of stranded rows, blocks BeginExecution for every job that starts meanwhile. Nothing
        // else in this class holds locks for long, and a sweep that cannot be interrupted between
        // batches cannot honour Stop() either.
        return UpdateInBatches(dbContext, cutoff, batchSize, cancellationToken);
    }

    private static int UpdateInBatches(
        ScheduledJobsInsightsDbContext dbContext,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var total = 0;
        int updatedThisBatch;

        do
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Take() before ExecuteUpdate, and the predicate is self-consuming: each batch flips rows
            // out of Running, so they cannot match again and the loop always converges.
            updatedThisBatch = dbContext.JobExecutions
                .Where(e => e.Status == ExecutionStatus.Running && e.StartedAt < cutoff)
                .Take(batchSize)
                .ExecuteUpdate(setters => setters
                    .SetProperty(e => e.Status, ExecutionStatus.Interrupted)
                    .SetProperty(e => e.ResultMessage, "No outcome was recorded; the process is presumed to have stopped mid-run."));

            total += updatedThisBatch;
        }
        while (updatedThisBatch > 0);

        return total;
    }
}
