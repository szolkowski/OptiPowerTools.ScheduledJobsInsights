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
/// <para>
/// It is also the sample that answers "what happens to the detail page when the job I am watching
/// finishes?". It builds a summary as it migrates but deliberately never calls
/// <see cref="LoggedScheduledJobBase.FlushSummary"/>, so no summary exists while the execution is
/// Running. Leave the detail page open and the whole <em>Result summary</em> section appears on the
/// poll tick after the job completes, together with the result message and the automatic metrics.
/// <see cref="SummaryShowcaseJob"/> is the opposite case, checkpointing so its summary fills in live.
/// </para>
/// <para>
/// Overrides <c>Stop()</c> so the CMS admin's Stop button works. Stopping mid-run still completes
/// the execution normally — it is a cooperative early exit, not a failure, and the partial summary
/// is persisted just the same.
/// </para>
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

        Summary.AppendSection("Batches");

        var migrated = 0;
        for (var batch = 1; batch <= BatchCount; batch++)
        {
            if (_stopRequested)
            {
                Log($"Stop requested — halting after batch {migrated}.", LogSeverity.Warning);
                Summary.AppendLine($"  Stopped early — {BatchCount - migrated} batch(es) not attempted.");
                return $"Stopped early after {migrated} of {BatchCount} batches.";
            }

            OnStatusChanged($"Migrating batch {batch} of {BatchCount}");
            Thread.Sleep(BatchDuration);

            migrated++;
            Log($"Batch {batch} migrated ({migrated * 100 / BatchCount}% complete).", LogSeverity.Success);

            // Appended, never flushed: the summary stays invisible until the job ends, which is the
            // whole point of this sample.
            Summary.AppendLine($"  Batch {batch,2} of {BatchCount} migrated.");
        }

        Summary.AppendSection("Totals");
        Summary.AppendLine($"  Batches migrated : {migrated} of {BatchCount}");

        RecordMetric("BatchesMigrated", migrated);
        return $"Migrated {migrated} batches.";
    }
}
