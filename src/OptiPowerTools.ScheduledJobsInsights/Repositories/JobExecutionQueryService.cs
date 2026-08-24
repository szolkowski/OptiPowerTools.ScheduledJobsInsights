using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
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
    /// <summary>
    /// How long the job-name list backing the filter dropdown is reused before being re-read.
    /// </summary>
    /// <remarks>
    /// Short enough that a newly introduced job appears in the filter within a minute — nobody
    /// notices — and long enough to collapse what was a per-page-view table scan into at most one
    /// query a minute. That query is <c>SELECT DISTINCT JobName</c>, which cost 681 logical reads at
    /// 100,000 executions and grows linearly, and prerendering meant it ran <em>twice</em> for every
    /// single page view.
    /// </remarks>
    private static readonly TimeSpan JobNameCacheDuration = TimeSpan.FromSeconds(60);

    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxLogEntries;

    /// <summary>Serialises refreshes so a cache miss produces one query, not one per caller.</summary>
    private readonly SemaphoreSlim _jobNameRefreshGate = new(1, 1);

    private IReadOnlyList<string> _cachedJobNames = [];
    /// <summary>
    /// When the cached list goes stale, as UTC ticks.
    /// </summary>
    /// <remarks>
    /// Ticks rather than a <see cref="DateTimeOffset"/>, read and written with
    /// <see cref="Volatile"/>: this is read outside the gate, and a <see cref="DateTimeOffset"/> is
    /// wider than a machine word, so a concurrent write can be observed half-applied. A torn read
    /// here yields a garbage expiry — an eternally fresh cache, or a permanently expired one.
    /// </remarks>
    private long _jobNamesExpireAtTicks = DateTimeOffset.MinValue.UtcTicks;

    public JobExecutionQueryService(
        IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory,
        TimeProvider timeProvider,
        IOptions<OptiPowerToolsScheduledJobsInsightsOptions>? options = null)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;

        var configured = options?.Value.MaxLogEntriesPerExecution ?? 0;
        _maxLogEntries = configured > 0
            ? configured
            : OptiPowerToolsScheduledJobsInsightsOptions.DefaultMaxLogEntriesPerExecution;
    }

    public async Task<ExecutionPage> GetExecutionsAsync(ExecutionFilter filter, ExecutionCursor? after, int pageSize, CancellationToken cancellationToken = default)
    {
        // Startup validation rejects a non-positive PageSize, but this takes the size as an argument
        // and zero produces nonsense: an empty page reporting HasMore with no cursor, so Next stays
        // enabled and silently returns to the first page.
        pageSize = Math.Max(1, pageSize);

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

    /// <summary>
    /// Distinct job names for the filter dropdown, cached for <see cref="JobNameCacheDuration"/>.
    /// </summary>
    /// <remarks>
    /// Cached because the underlying query has to look at every row — there is no way to produce a
    /// distinct list without doing so — while the answer changes only when a job runs for the very
    /// first time. This service is registered as a singleton, so the cache is process-wide.
    /// </remarks>
    public async Task<IReadOnlyList<string>> GetDistinctJobNamesAsync(CancellationToken cancellationToken = default)
    {
        if (IsJobNameCacheFresh())
            return _cachedJobNames;

        await _jobNameRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-checked inside the gate: prerendering and the circuit start this within milliseconds
            // of each other, so without this the "saved" query would simply happen twice anyway.
            if (IsJobNameCacheFresh())
                return _cachedJobNames;

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            _cachedJobNames = await dbContext.JobExecutions
                .AsNoTracking()
                .Select(e => e.JobName)
                .Distinct()
                .OrderBy(name => name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Stamped only after a successful read, so a failed query is retried rather than caching
            // an empty dropdown for a minute.
            Volatile.Write(ref _jobNamesExpireAtTicks, (_timeProvider.GetUtcNow() + JobNameCacheDuration).UtcTicks);

            return _cachedJobNames;
        }
        finally
        {
            _jobNameRefreshGate.Release();
        }
    }

    public async Task<JobExecution?> GetExecutionAsync(long executionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await dbContext.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ExecutionStatusSnapshot?> GetExecutionStatusAsync(long executionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await dbContext.JobExecutions
            .AsNoTracking()
            .Where(e => e.Id == executionId)
            // Projected in SQL: the length is computed by the database, so the summary text itself
            // never crosses the wire. Same technique as ExecutionListItem's HasResultSummary.
            .Select(e => new ExecutionStatusSnapshot(
                e.Status,
                e.CompletedAt,
                e.ResultMessage,
                e.ExceptionMessage,
                e.ResultSummary == null ? 0 : e.ResultSummary.Length))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobLogEntry>> GetLogEntriesAsync(long executionId, int afterSequence = 0, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await dbContext.JobLogEntries
            .AsNoTracking()
            .Where(e => e.JobExecutionId == executionId && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            // Capped because the caller is a Blazor Server circuit that holds every line it is given
            // for as long as the page is open. An unbounded read of a two-million-line execution is
            // an out-of-memory on the *server*, once per viewer.
            .Take(_maxLogEntries)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsJobNameCacheFresh() =>
        _timeProvider.GetUtcNow().UtcTicks < Volatile.Read(ref _jobNamesExpireAtTicks);

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
