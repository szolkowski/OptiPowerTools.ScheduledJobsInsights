using EPiServer.DataAbstraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>Default <see cref="IJobRetentionService"/>.</summary>
internal sealed class JobRetentionService : IJobRetentionService
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;
    private readonly JobRetentionPolicyStore _policies;
    private readonly RegisteredJobNames _registeredJobs;
    private readonly LoggedJobTypeIndex _jobTypes;
    private readonly OptiPowerToolsScheduledJobsInsightsOptions _options;
    private readonly ILogger<JobRetentionService> _logger;
    private readonly TimeProvider _timeProvider;

    private static readonly TimeSpan CountCacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>Serialises refreshes so a cache miss produces one query, not one per caller.</summary>
    private readonly SemaphoreSlim _countRefreshGate = new(1, 1);

    private Dictionary<string, int> _cachedCounts = [];

    /// <summary>Ticks, and volatile, for the same reason as the job-name cache: see that one.</summary>
    private long _countsExpireAtTicks = DateTimeOffset.MinValue.UtcTicks;

    public JobRetentionService(
        IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory,
        JobRetentionPolicyStore policies,
        RegisteredJobNames registeredJobs,
        LoggedJobTypeIndex jobTypes,
        IOptions<OptiPowerToolsScheduledJobsInsightsOptions> options,
        ILogger<JobRetentionService> logger,
        TimeProvider timeProvider)
    {
        // Required, not an optional parameter defaulting to TimeProvider.System. TimeProvider is
        // registered in the container and every other collaborator here takes it as a dependency; an
        // optional one with a static fallback is a service locator in disguise, and it silently
        // bypasses a host's test clock for whoever forgets to pass it.
        _timeProvider = timeProvider;
        _dbContextFactory = dbContextFactory;
        _policies = policies;
        _registeredJobs = registeredJobs;
        _jobTypes = jobTypes;
        _options = options.Value;
        _logger = logger;
    }

    public RetentionPeriod DefaultPeriod =>
        _options.RetentionDays > 0 ? RetentionPeriod.OfDays(_options.RetentionDays) : RetentionPeriod.Indefinite;

    public async Task<IReadOnlyList<JobRetention>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var overrides = await _policies.GetAllAsync(cancellationToken).ConfigureAwait(false);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var history = await GetExecutionCountsAsync(dbContext, cancellationToken).ConfigureAwait(false);

        var registered = _registeredJobs.Read();

        // Logged jobs in the running application (configurable before they have ever run), plus job
        // types present only in history (still worth managing after the code is gone), plus any stale
        // override so it stays visible and removable.
        //
        // Deliberately *not* every registered scheduled job: a job on Optimizely's own ScheduledJobBase
        // never writes a row here, so it has no history to retain, and including them would bury the
        // handful that matter among the CMS's two dozen built-ins.
        var jobTypeNames = new HashSet<string>(StringComparer.Ordinal);
        jobTypeNames.UnionWith(_jobTypes.LoggedJobTypeNames);
        jobTypeNames.UnionWith(history.Keys);
        jobTypeNames.UnionWith(overrides.Keys);

        return [.. jobTypeNames
            .Select(jobTypeName => Build(jobTypeName, registered, history, overrides))
            // Ordinal: the server's locale is not the reader's, and this package renders dates and
            // numbers invariantly for the same reason. A list that reorders itself when the host's
            // culture changes is worse than one that ignores locale collation.
            .OrderBy(job => job.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    public Task SetOverrideAsync(
        string jobTypeName,
        RetentionPeriod? period,
        string modifiedBy,
        CancellationToken cancellationToken = default) =>
        _policies.SaveAsync(jobTypeName, period, modifiedBy, cancellationToken);

    public async Task<IReadOnlyDictionary<string, RetentionPeriod>> GetEffectiveOverridesAsync(
        CancellationToken cancellationToken = default)
    {
        // Built from the same records the screen shows, resolved by the same JobRetention.Resolve.
        // This used to walk attributes and then override rows itself — a second expression of the
        // precedence order, and the one the cleanup job actually acted on, so the copy that governed
        // deletion was not the copy the tests and the documentation pointed at. They agreed; nothing
        // made them agree.
        var jobs = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        var effective = new Dictionary<string, RetentionPeriod>(StringComparer.Ordinal);

        foreach (var job in jobs)
        {
            var (period, source) = job.Resolve(DefaultPeriod);

            // Only jobs with a rule of their own belong here. The cleanup job takes everything else
            // in its default sweep, and listing them would exclude every job from that sweep instead.
            if (source is not RetentionSource.Default)
                effective[job.JobTypeName] = period;
        }

        return effective;
    }

    /// <summary>
    /// Executions per job type, cached briefly.
    /// </summary>
    /// <remarks>
    /// The only query on this screen that scales with history — a <c>GROUP BY</c> over every
    /// execution row, measured at 104 logical reads against 10,000 executions and 980 against
    /// 100,000. Prerendering plus the circuit means the screen loads twice per view, so without a
    /// cache the most expensive query in the UI ran twice for every visit. Sixty seconds, matching
    /// the filter dropdown: a count column going a minute stale costs nothing, and the alternative
    /// is paying for it on every render.
    /// </remarks>
    private async Task<Dictionary<string, int>> GetExecutionCountsAsync(
        ScheduledJobsInsightsDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (IsCountCacheFresh())
            return _cachedCounts;

        await _countRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Re-checked inside the gate: prerender and the circuit start milliseconds apart, so
            // without this the query the cache exists to avoid simply happens twice anyway.
            if (IsCountCacheFresh())
                return _cachedCounts;

            _cachedCounts = await dbContext.JobExecutions
                .AsNoTracking()
                .GroupBy(e => e.JobTypeName)
                .Select(g => new { JobTypeName = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.JobTypeName, x => x.Count, cancellationToken)
                .ConfigureAwait(false);

            // Stamped only after a successful read, so a failed query retries rather than caching
            // an empty screen.
            Volatile.Write(ref _countsExpireAtTicks, (_timeProvider.GetUtcNow() + CountCacheDuration).UtcTicks);

            return _cachedCounts;
        }
        finally
        {
            _countRefreshGate.Release();
        }
    }

    private bool IsCountCacheFresh() =>
        _timeProvider.GetUtcNow().UtcTicks < Volatile.Read(ref _countsExpireAtTicks);

    private JobRetention Build(
        string jobTypeName,
        Dictionary<string, string> registered,
        Dictionary<string, int> history,
        Dictionary<string, JobRetentionPolicy> overrides)
    {
        var attribute = _jobTypes.FindAttribute(jobTypeName);
        var isRegistered = registered.TryGetValue(jobTypeName, out var registeredName);
        overrides.TryGetValue(jobTypeName, out var policy);

        var storedOverride = policy is null ? null : RetentionPeriod.FromStoredValue(policy.RetentionDays);

        if (policy is not null && storedOverride is null)
        {
            // Reported here rather than only flagged in the screen: the row is being ignored, and the
            // person who needs to know may never open that page. A non-positive stored value resolves
            // to a cutoff of now-or-later, which would delete the very history it was written to
            // govern — so the job falls back to its attribute or the default until it is corrected.
            _logger.LogWarning(
                "ScheduledJobsInsights is ignoring the stored retention of {RetentionDays} day(s) for {JobTypeName}: it must be a positive number of days, or null for indefinite.",
                policy.RetentionDays,
                jobTypeName);
        }

        return new JobRetention(
            JobTypeName: jobTypeName,
            DisplayName: isRegistered ? registeredName! : RegisteredJobNames.ShortNameOf(jobTypeName),
            IsRegistered: isRegistered,
            ExistsInCode: _jobTypes.Exists(jobTypeName),
            Attribute: attribute is null ? null : RetentionPeriod.FromAttribute(attribute),
            AttributeDescription: attribute?.Description,
            HasInvalidAttribute: attribute is { IsValid: false },
            Override: storedOverride,
            HasInvalidOverride: policy is not null && storedOverride is null,
            ModifiedBy: policy?.ModifiedBy,
            ModifiedAt: policy?.ModifiedAt,
            ExecutionCount: history.GetValueOrDefault(jobTypeName));
    }
}
