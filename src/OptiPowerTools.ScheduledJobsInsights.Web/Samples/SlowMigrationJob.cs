using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — the only sample that stays running long enough to observe an
/// execution mid-flight. Start it, then open the execution from the list to watch the detail page's
/// two-second polling append log lines live while the badge still reads "Running" and the duration
/// column shows "—". Because it runs for about a minute it is also the only sample whose finished
/// duration crosses one second, so the list renders the "0.0 s" branch rather than "0 ms".
/// </summary>
/// <remarks>
/// Overrides <c>Stop()</c> so the CMS admin's Stop button works. Stopping mid-run still completes
/// the execution normally — it is a cooperative early exit, not a failure.
/// </remarks>
[ScheduledJob(DisplayName = "Sample: Slow Migration", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class SlowMigrationJob : LoggedScheduledJobBase
{
    private const int BatchCount = 30;
    private static readonly TimeSpan BatchDuration = TimeSpan.FromSeconds(2);

    private volatile bool _stopRequested;

    public SlowMigrationJob(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
        : base(writer, scheduledJobRepository)
    {
    }

    public override void Stop()
    {
        _stopRequested = true;
        base.Stop();
    }

    protected override string ExecuteJob()
    {
        Log($"Migrating {BatchCount} batches, roughly {BatchCount * BatchDuration.TotalSeconds:0} seconds total.", LogSeverity.Info);

        var migrated = 0;
        for (var batch = 1; batch <= BatchCount; batch++)
        {
            if (_stopRequested)
            {
                Log($"Stop requested — halting after batch {migrated}.", LogSeverity.Warning);
                return $"Stopped early after {migrated} of {BatchCount} batches.";
            }

            OnStatusChanged($"Migrating batch {batch} of {BatchCount}");
            Thread.Sleep(BatchDuration);

            migrated++;
            Log($"Batch {batch} migrated ({migrated * 100 / BatchCount}% complete).", LogSeverity.Success);
        }

        RecordMetric("BatchesMigrated", migrated);
        return $"Migrated {migrated} batches.";
    }
}
