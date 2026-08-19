using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Repositories;

namespace OptiPowerTools.ScheduledJobsInsights.Jobs;

/// <summary>
/// Removes job executions (and their cascade-deleted log/metric rows) older than
/// <see cref="OptiPowerToolScheduledJobsInsightsOptions.RetentionDays"/>. Auto-discovered by Optimizely
/// into the CMS's own Scheduled Jobs admin list, like any other <c>[ScheduledJob]</c> — the actual run
/// interval and enabled state are managed there after installation, not via options.
/// </summary>
[ScheduledJob(
    DisplayName = "Scheduled Jobs Insights - Log Cleanup",
    Description = "Removes job execution logs older than the configured retention period.",
    IntervalType = ScheduledIntervalType.Days,
    IntervalLength = 1,
    DefaultEnabled = true)]
public sealed class ScheduledJobsInsightsCleanupJob : LoggedScheduledJobBase
{
    private readonly ICleanupRepository _cleanupRepository;
    private readonly OptiPowerToolScheduledJobsInsightsOptions _options;

    /// <summary>Initializes a new instance of <see cref="ScheduledJobsInsightsCleanupJob"/>.</summary>
    public ScheduledJobsInsightsCleanupJob(
        IJobExecutionWriter writer,
        IScheduledJobRepository scheduledJobRepository,
        ICleanupRepository cleanupRepository,
        IOptions<OptiPowerToolScheduledJobsInsightsOptions> options)
        : base(writer, scheduledJobRepository)
    {
        _cleanupRepository = cleanupRepository;
        _options = options.Value;
    }

    /// <summary>Deletes executions older than <see cref="OptiPowerToolScheduledJobsInsightsOptions.RetentionDays"/> in batches.</summary>
    protected override string ExecuteJob()
    {
        LogInputData(new { _options.RetentionDays, _options.CleanupBatchSize });

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
        var totalDeleted = 0;
        int deletedThisBatch;

        do
        {
            deletedThisBatch = _cleanupRepository.DeleteExecutionsOlderThan(cutoff, _options.CleanupBatchSize);
            totalDeleted += deletedThisBatch;
            if (deletedThisBatch > 0)
                Log($"Deleted batch of {deletedThisBatch} execution(s) older than {cutoff:u}. Running total: {totalDeleted}.");
        } while (deletedThisBatch > 0);

        RecordMetric("ExecutionsDeleted", totalDeleted);
        return $"Deleted {totalDeleted} job execution(s) older than {_options.RetentionDays} day(s).";
    }
}
