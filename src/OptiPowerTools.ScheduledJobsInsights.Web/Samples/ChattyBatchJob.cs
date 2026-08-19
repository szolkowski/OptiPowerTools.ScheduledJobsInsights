using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — emits ~5,000 log lines in a tight loop to exercise the buffered
/// channel writer under load and the virtualized scrolling log viewer with a large log volume.
/// </summary>
[ScheduledJob(DisplayName = "Sample: Chatty Batch", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class ChattyBatchJob : LoggedScheduledJobBase
{
    private const int RecordCount = 5_000;

    public ChattyBatchJob(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
        : base(writer, scheduledJobRepository)
    {
    }

    protected override string ExecuteJob()
    {
        for (var i = 1; i <= RecordCount; i++)
            Log($"Processed record {i} of {RecordCount}.");

        return $"Processed {RecordCount} records.";
    }
}
