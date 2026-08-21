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
/// <remarks>
/// It is also the sample for summaries on the failure path. The summary is appended to as the import
/// proceeds and is never flushed explicitly, yet the run that throws still shows everything recorded
/// before the exception — <see cref="LoggedScheduledJobBase.Execute"/> persists it on the way out of
/// both branches. Run it twice to compare the two.
/// </remarks>
[ScheduledJob(DisplayName = "Sample: Flaky Import", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class FlakyImportJob : LoggedScheduledJobBase
{
    private static int _runCount;

    public FlakyImportJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        var runNumber = Interlocked.Increment(ref _runCount);
        Log($"Starting import, run #{runNumber}.");

        Summary.AppendSection($"Import run #{runNumber}");
        Summary.AppendLine("  Validated file headers.");
        Log("Validated file headers.", LogSeverity.Success);

        if (runNumber % 2 == 0)
        {
            Log("Encountered a malformed record — aborting import.", LogSeverity.Error);

            // Recorded, then thrown. The summary is still persisted: nothing here flushes it, and
            // nothing needs to.
            Summary.AppendLine("  Aborted at row 42 — malformed record.");
            Summary.AppendLine("  No records were committed.");

            throw new InvalidOperationException($"Malformed record at row 42 on run #{runNumber}.");
        }

        Log("Imported 128 records.", LogSeverity.Success);
        Summary.AppendLine("  Imported 128 records.");
        Summary.AppendLine("  No rejects.");

        return "Import completed successfully.";
    }
}
