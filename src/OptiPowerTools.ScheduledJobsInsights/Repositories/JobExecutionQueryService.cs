using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Repositories;

/// <summary>
/// Default <see cref="IJobExecutionQueryService"/>. Uses keyset (seek) pagination — ordered by
/// <c>StartedAt DESC, Id DESC</c> — rather than offset/<c>Skip(n)</c>, since this is a large,
/// append-heavy, time-ordered table where offset paging degrades and shifts under concurrent inserts.
/// </summary>
internal sealed class JobExecutionQueryService : IJobExecutionQueryService
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;

    public JobExecutionQueryService(IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<ExecutionPage> GetExecutionsAsync(ExecutionFilter filter, ExecutionCursor? after, int pageSize, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = dbContext.JobExecutions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filter.JobName))
            query = query.Where(e => e.JobName == filter.JobName);
        if (filter.Status.HasValue)
            query = query.Where(e => e.Status == filter.Status.Value);
        if (filter.From.HasValue)
            query = query.Where(e => e.StartedAt >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(e => e.StartedAt <= filter.To.Value);

        if (after is not null)
            query = query.Where(e => e.StartedAt < after.StartedAt || (e.StartedAt == after.StartedAt && e.Id < after.Id));

        var items = await query
            .OrderByDescending(e => e.StartedAt)
            .ThenByDescending(e => e.Id)
            .Take(pageSize + 1)
            // Projected rather than materialising entities: JobExecution holds three unbounded
            // columns the list never shows, and a page is 50 rows of them.
            .Select(e => new ExecutionListItem(
                e.Id,
                e.JobName,
                e.Status,
                e.StartedAt,
                e.CompletedAt,
                e.ResultMessage,
                e.ExceptionMessage,
                e.ResultSummary != null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);

        var nextCursor = items.Count > 0 ? new ExecutionCursor(items[^1].StartedAt, items[^1].Id) : null;

        return new ExecutionPage(items, hasMore ? nextCursor : null, hasMore);
    }

    public async Task<IReadOnlyList<string>> GetDistinctJobNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await dbContext.JobExecutions
            .AsNoTracking()
            .Select(e => e.JobName)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JobExecution?> GetExecutionAsync(long executionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await dbContext.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobLogEntry>> GetLogEntriesAsync(long executionId, int afterSequence = 0, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await dbContext.JobLogEntries
            .AsNoTracking()
            .Where(e => e.JobExecutionId == executionId && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobMetric>> GetMetricsAsync(long executionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await dbContext.JobMetrics
            .AsNoTracking()
            .Where(e => e.JobExecutionId == executionId)
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
