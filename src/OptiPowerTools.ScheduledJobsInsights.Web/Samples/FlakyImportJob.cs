using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — deliberately throws partway through on alternating runs, to
/// prove that a thrown exception still surfaces as <c>HasLastExecutionFailed=true</c> in the
/// native CMS admin while the full detail (logs up to the failure point, stack trace) remains
/// visible in the ScheduledJobsInsights Blazor UI for that run.
/// </summary>
[ScheduledJob(DisplayName = "Sample: Flaky Import", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class FlakyImportJob : LoggedScheduledJobBase
{
    private static int _runCount;

    public FlakyImportJob(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
        : base(writer, scheduledJobRepository)
    {
    }

    protected override string ExecuteJob()
    {
        var runNumber = Interlocked.Increment(ref _runCount);
        Log($"Starting import, run #{runNumber}.");
        Log("Validated file headers.", LogSeverity.Success);

        if (runNumber % 2 == 0)
        {
            Log("Encountered a malformed record — aborting import.", LogSeverity.Error);
            throw new InvalidOperationException($"Malformed record at row 42 on run #{runNumber}.");
        }

        Log("Imported 128 records.", LogSeverity.Success);
        return "Import completed successfully.";
    }
}
