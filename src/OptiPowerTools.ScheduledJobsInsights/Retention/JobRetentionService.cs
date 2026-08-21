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
    private readonly IScheduledJobRepository _scheduledJobRepository;
    private readonly LoggedJobTypeIndex _jobTypes;
    private readonly OptiPowerToolScheduledJobsInsightsOptions _options;
    private readonly ILogger<JobRetentionService> _logger;

    public JobRetentionService(
        IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory,
        IScheduledJobRepository scheduledJobRepository,
        LoggedJobTypeIndex jobTypes,
        IOptions<OptiPowerToolScheduledJobsInsightsOptions> options,
        ILogger<JobRetentionService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _scheduledJobRepository = scheduledJobRepository;
        _jobTypes = jobTypes;
        _options = options.Value;
        _logger = logger;
    }

    public RetentionPeriod DefaultPeriod =>
        _options.RetentionDays > 0 ? RetentionPeriod.OfDays(_options.RetentionDays) : RetentionPeriod.Indefinite;

    public async Task<IReadOnlyList<JobRetention>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var overrides = await dbContext.JobRetentionPolicies
            .AsNoTracking()
            .ToDictionaryAsync(p => p.JobTypeName, cancellationToken)
            .ConfigureAwait(false);

        var history = await dbContext.JobExecutions
            .AsNoTracking()
            .GroupBy(e => e.JobTypeName)
            .Select(g => new { JobTypeName = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobTypeName, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        var registered = GetRegisteredJobs();

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
            .OrderBy(job => job.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task SetOverrideAsync(
        string jobTypeName,
        RetentionPeriod? period,
        string modifiedBy,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await dbContext.JobRetentionPolicies
            .FirstOrDefaultAsync(p => p.JobTypeName == jobTypeName, cancellationToken)
            .ConfigureAwait(false);

        if (period is null)
        {
            // Clearing an override means deleting the row: its absence is what lets the attribute, or
            // failing that the default, apply again.
            if (existing is not null)
                dbContext.JobRetentionPolicies.Remove(existing);
        }
        else if (existing is null)
        {
            dbContext.JobRetentionPolicies.Add(new JobRetentionPolicy
            {
                JobTypeName = jobTypeName,
                RetentionDays = period.Value.Days,
                ModifiedBy = modifiedBy,
                ModifiedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.RetentionDays = period.Value.Days;
            existing.ModifiedBy = modifiedBy;
            existing.ModifiedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "ScheduledJobsInsights retention for {JobTypeName} set to {Retention} by {ModifiedBy}.",
                jobTypeName,
                Describe(period),
                modifiedBy);
        }
    }

    public async Task<IReadOnlyDictionary<string, RetentionPeriod>> GetEffectiveOverridesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var effective = new Dictionary<string, RetentionPeriod>(StringComparer.Ordinal);

        // Attributes first, so a stored override written afterwards takes precedence.
        foreach (var jobTypeName in _jobTypes.LoggedJobTypeNames)
        {
            if (_jobTypes.FindAttribute(jobTypeName) is { } attribute && RetentionPeriod.FromAttribute(attribute) is { } declared)
                effective[jobTypeName] = declared;
        }

        var overrides = await dbContext.JobRetentionPolicies
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var policy in overrides)
        {
            if (RetentionPeriod.FromStoredValue(policy.RetentionDays) is { } stored)
            {
                effective[policy.JobTypeName] = stored;
                continue;
            }

            // Left to the attribute or the default rather than obeyed. A stored zero or negative
            // resolves to a cutoff of now-or-later, which would delete the very history the row was
            // written to govern — including the run in progress.
            _logger.LogWarning(
                "ScheduledJobsInsights is ignoring the stored retention of {RetentionDays} day(s) for {JobTypeName}: it must be a positive number of days, or null for indefinite. The job falls back to its attribute or the installation default until the row is corrected.",
                policy.RetentionDays,
                policy.JobTypeName);
        }

        return effective;
    }

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

        return new JobRetention(
            JobTypeName: jobTypeName,
            DisplayName: isRegistered ? registeredName! : ShortNameOf(jobTypeName),
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

    /// <summary>Job type name to display name, for everything the CMS currently has registered.</summary>
    private Dictionary<string, string> GetRegisteredJobs()
    {
        try
        {
            // Duplicate type names are possible in a misconfigured installation; the first wins rather
            // than throwing, since a duplicate must not take the whole screen down.
            var registered = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var job in _scheduledJobRepository.List().Where(job => !string.IsNullOrEmpty(job.TypeName)))
                registered.TryAdd(job.TypeName, string.IsNullOrEmpty(job.Name) ? ShortNameOf(job.TypeName) : job.Name);

            return registered;
        }
        catch (Exception ex)
        {
            // Same reasoning as LoggedScheduledJobBase's job-name lookup: the repository is
            // best-effort. Losing it costs display names and the registered flag, not the screen.
            _logger.LogWarning(ex, "ScheduledJobsInsights could not list registered scheduled jobs; the retention screen will show job type names instead of display names.");
            return [];
        }
    }

    /// <summary>How a retention change reads in the audit log line.</summary>
    private static string Describe(RetentionPeriod? period)
    {
        if (period is not { } set)
            return "the inherited default";

        return set.IsIndefinite ? "indefinite" : $"{set.Days} day(s)";
    }

    private static string ShortNameOf(string jobTypeName)
    {
        var lastDot = jobTypeName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < jobTypeName.Length - 1 ? jobTypeName[(lastDot + 1)..] : jobTypeName;
    }
}
