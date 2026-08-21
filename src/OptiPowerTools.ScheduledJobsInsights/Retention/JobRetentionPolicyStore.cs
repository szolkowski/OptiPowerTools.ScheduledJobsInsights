using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// Reads and writes the stored per-job retention rows.
/// </summary>
/// <remarks>
/// Split out of <see cref="JobRetentionService"/>, which was doing four unrelated jobs at once:
/// persistence, precedence resolution, the screen's projection, and adapting Optimizely's job
/// registry. This is the persistence one, and the only part that touches
/// <c>JobRetentionPolicies</c>.
/// </remarks>
internal sealed class JobRetentionPolicyStore
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _dbContextFactory;
    private readonly ILogger<JobRetentionPolicyStore> _logger;

    public JobRetentionPolicyStore(
        IDbContextFactory<ScheduledJobsInsightsDbContext> dbContextFactory,
        ILogger<JobRetentionPolicyStore> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    /// <summary>Every stored override, keyed by job type.</summary>
    public async Task<Dictionary<string, JobRetentionPolicy>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.JobRetentionPolicies
            .AsNoTracking()
            .ToDictionaryAsync(policy => policy.JobTypeName, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Stores, updates or clears one job's override.
    /// </summary>
    /// <remarks>
    /// Read-then-write against a unique index, so two administrators saving at once — or one
    /// double-fired change event — can both see no existing row and both insert. The loser hits the
    /// constraint; re-reading and retrying once turns it into an update. Only once, because a second
    /// failure is no longer a race.
    /// </remarks>
    public async Task SaveAsync(
        string jobTypeName,
        RetentionPeriod? period,
        string modifiedBy,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteAsync(jobTypeName, period, modifiedBy, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            await WriteAsync(jobTypeName, period, modifiedBy, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(
        string jobTypeName,
        RetentionPeriod? period,
        string modifiedBy,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await dbContext.JobRetentionPolicies
            .FirstOrDefaultAsync(policy => policy.JobTypeName == jobTypeName, cancellationToken)
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

    /// <summary>How a retention change reads in the audit log line.</summary>
    private static string Describe(RetentionPeriod? period)
    {
        if (period is not { } set)
            return "the inherited default";

        return set.IsIndefinite ? "indefinite" : $"{set.Days} day(s)";
    }
}
