using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Repositories;
using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Jobs;

/// <summary>
/// Removes job executions (and their cascade-deleted log/metric rows) once they pass the retention
/// that applies to their job. Auto-discovered by Optimizely into the CMS's own Scheduled Jobs admin
/// list, like any other <c>[ScheduledJob]</c> — the run interval and enabled state are managed there
/// after installation, not via options.
/// </summary>
/// <remarks>
/// Retention is resolved per job type, in the order override, then
/// <see cref="JobRetentionAttribute"/>, then
/// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.RetentionDays"/>. Jobs resolving to
/// indefinite are skipped entirely. Everything else is deleted in batches so no single transaction
/// holds locks for long.
/// </remarks>
[ScheduledJob(
    DisplayName = "Scheduled Jobs Insights - Log Cleanup",
    Description = "Removes job execution logs once they pass the retention configured for their job.",
    IntervalType = ScheduledIntervalType.Days,
    IntervalLength = 1,
    DefaultEnabled = true)]
public sealed class ScheduledJobsInsightsCleanupJob : LoggedScheduledJobBase
{
    private readonly ICleanupRepository _cleanupRepository;
    private readonly IJobRetentionPolicySource _retentionService;
    private readonly OptiPowerToolsScheduledJobsInsightsOptions _options;

    /// <summary>Initializes a new instance of <see cref="ScheduledJobsInsightsCleanupJob"/>.</summary>
    /// <param name="context">Collaborators the base class records this job's own runs with.</param>
    /// <param name="services">
    /// Supplies the deletion and retention services this job runs on. Both are internal to the
    /// package.
    /// </param>
    /// <param name="options">Package options; supplies the batch size.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    /// <remarks>
    /// Takes an <see cref="IServiceProvider"/> rather than its two collaborators directly, which is a
    /// service locator and is the point. Optimizely discovers this job by type, so the class and this
    /// constructor must be public — and a public constructor cannot take a less accessible parameter
    /// type. Naming the collaborators here would therefore force two implementation-detail interfaces
    /// onto the permanently frozen 1.0 surface, purely as a side effect of how the CMS constructs
    /// jobs. One awkward constructor is the cheaper price.
    /// </remarks>
    public ScheduledJobsInsightsCleanupJob(
        JobLoggingContext context,
        IServiceProvider services,
        IOptions<OptiPowerToolsScheduledJobsInsightsOptions> options)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _cleanupRepository = services.GetRequiredService<ICleanupRepository>();
        _retentionService = services.GetRequiredService<IJobRetentionPolicySource>();
        _options = options.Value;

        // A first run against years of accumulated history can take a long time; an administrator
        // watching it must be able to call it off. The base class owns the token itself, so there is
        // no Stop() override to write and nothing here to dispose.
        IsStoppable = true;
    }

    /// <summary>Deletes executions that have outlived the retention applying to their job.</summary>
    protected override string ExecuteJob()
    {
        var now = DateTimeOffset.UtcNow;
        var cancellationToken = StopToken;
        var perJob = _retentionService.GetEffectiveOverridesAsync(cancellationToken).GetAwaiter().GetResult();
        var defaultPeriod = _retentionService.DefaultPeriod;

        LogInputData(new
        {
            DefaultRetention = Describe(defaultPeriod),
            _options.CleanupBatchSize,
            JobsWithOwnRetention = perJob.ToDictionary(x => x.Key, x => Describe(x.Value))
        });

        var totalDeleted = 0;

        // Jobs with their own rule are excluded from the default sweep whether or not that rule is
        // shorter — otherwise the default would delete history a job explicitly asked to keep.
        var governedJobTypes = perJob.Keys.ToArray();

        if (defaultPeriod.CutoffFrom(now) is { } defaultCutoff)
        {
            totalDeleted += DeleteInBatches(
                $"default ({Describe(defaultPeriod)})",
                batch => _cleanupRepository.DeleteExecutionsOlderThan(defaultCutoff, batch, governedJobTypes, cancellationToken),
                cancellationToken);
        }
        else
        {
            Log("Default retention is indefinite; only jobs with their own retention will be trimmed.", LogSeverity.Info);
        }

        foreach (var (jobTypeName, period) in perJob.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (period.CutoffFrom(now) is not { } cutoff)
            {
                Log($"{jobTypeName}: retention is indefinite, skipping.", LogSeverity.Debug);
                continue;
            }

            totalDeleted += DeleteInBatches(
                $"{jobTypeName} ({Describe(period)})",
                batch => _cleanupRepository.DeleteExecutionsOlderThan(jobTypeName, cutoff, batch, cancellationToken),
                cancellationToken);
        }

        // After the deletes, never before. Marking a row Interrupted makes it deletable — Interrupted
        // is a finished state, and only Running is protected — so a pass that marked first stripped
        // the guard off the very rows the guard exists for, and the sweep that followed deleted the
        // history of a job that was still working. A job may legitimately outlive its own retention
        // (a 25-hour import under a one-day rule), and age alone cannot tell "stranded" from "still
        // working". Resolving stranded rows last costs them one extra interval before they age out,
        // which is the cheap side of this trade: the alternative loses a live run's history outright.
        //
        // Skipped entirely when the run was stopped: nothing here is urgent, and a half-applied sweep
        // followed by a status rewrite is a worse thing to leave behind than an unresolved row.
        var interrupted = cancellationToken.IsCancellationRequested
            ? 0
            : MarkInterruptedExecutions(now, cancellationToken);

        RecordMetric(JobMetricNames.ExecutionsDeleted, totalDeleted);
        RecordMetric(JobMetricNames.ExecutionsMarkedInterrupted, interrupted);

        Summary.AppendLine($"Default retention: {Describe(defaultPeriod)}");
        Summary.AppendLine($"Jobs with their own retention: {perJob.Count}");
        Summary.AppendLine($"Executions deleted: {totalDeleted:N0}");

        if (interrupted > 0)
            Summary.AppendLine($"Unfinished executions marked interrupted: {interrupted:N0}");

        if (cancellationToken.IsCancellationRequested)
        {
            Summary.AppendLine("Stopped before the sweep completed.");
            return $"Stopped after deleting {totalDeleted} job execution(s).";
        }

        return $"Deleted {totalDeleted} job execution(s).";
    }

    /// <summary>
    /// Gives up on executions that have sat unfinished past the configured threshold.
    /// </summary>
    private int MarkInterruptedExecutions(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_options.InterruptedExecutionThreshold <= TimeSpan.Zero)
            return 0;

        var cutoff = now - _options.InterruptedExecutionThreshold;
        var marked = _cleanupRepository.MarkInterruptedExecutions(cutoff, _options.CleanupBatchSize, cancellationToken);

        if (marked > 0)
        {
            Log($"Marked {marked} execution(s) still running since before {cutoff:u} as interrupted.",
                LogSeverity.Warning);
        }

        return marked;
    }

    /// <summary>
    /// Runs one delete repeatedly until it stops finding anything. Each call is its own transaction,
    /// so a large backlog is cleared without holding locks across the whole of it.
    /// </summary>
    private int DeleteInBatches(string what, Func<int, int> deleteBatch, CancellationToken cancellationToken)
    {
        var deletedForThisRule = 0;
        int deletedThisBatch;

        do
        {
            // Checked between batches rather than within one: a delete already in flight finishes,
            // so Stop never leaves a half-applied batch behind.
            if (cancellationToken.IsCancellationRequested)
            {
                Log($"Stopped during {what} after {deletedForThisRule} execution(s).", LogSeverity.Warning);
                break;
            }

            deletedThisBatch = deleteBatch(_options.CleanupBatchSize);
            deletedForThisRule += deletedThisBatch;

            if (deletedThisBatch > 0)
                Log($"Deleted {deletedThisBatch} execution(s) under {what}. Running total: {deletedForThisRule}.");
        } while (deletedThisBatch > 0);

        if (deletedForThisRule > 0)
            Summary.AppendLine($"  {what}: {deletedForThisRule:N0} deleted");

        return deletedForThisRule;
    }

    private static string Describe(RetentionPeriod period) =>
        period.IsIndefinite ? "indefinite" : $"{period.Days} day(s)";
}
