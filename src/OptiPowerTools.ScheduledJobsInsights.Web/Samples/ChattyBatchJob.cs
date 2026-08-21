using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — emits ~5,000 log lines in a tight loop to exercise the buffered
/// channel writer under load and the virtualized scrolling log viewer with a large log volume.
/// </summary>
[ScheduledJob(DisplayName = "Sample: Chatty Batch", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
// The worked example for per-job retention. This job is the reason the feature exists: at ~5,000
// log lines a run it dominates storage, and its history is only diagnostically useful briefly.
// Visible in the Job Retention screen, where an administrator can override it either way.
[JobRetention(7, Description = "Emits ~5,000 log lines per run; only useful for diagnosing a recent failure.")]
public sealed class ChattyBatchJob : LoggedScheduledJobBase
{
    private const int RecordCount = 5_000;

    public ChattyBatchJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        for (var i = 1; i <= RecordCount; i++)
            Log($"Processed record {i} of {RecordCount}.");

        return $"Processed {RecordCount} records.";
    }
}
