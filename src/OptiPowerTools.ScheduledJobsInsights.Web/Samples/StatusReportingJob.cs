using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — the worked example for <c>OnStatusChanged</c>. Every call both
/// updates the live status the CMS admin's Scheduled Jobs list polls for, exactly as it would on a
/// plain <see cref="ScheduledJobBase"/>, and is persisted as a log line with
/// <see cref="LogEntrySource.StatusChanged"/>. Interleaving those calls with
/// <see cref="LoggedScheduledJobBase.Log"/> shows both sources landing in one execution's log in
/// call order — this is the only sample that produces <c>StatusChanged</c>-sourced lines.
/// </summary>
[ScheduledJob(DisplayName = "Sample: Status Reporting", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class StatusReportingJob : LoggedScheduledJobBase
{
    private static readonly string[] Phases =
    [
        "Collecting source documents",
        "Reindexing search",
        "Rebuilding link graph",
        "Warming caches"
    ];

    public StatusReportingJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        Log("Job starting — watch the Status column in the CMS Scheduled Jobs list.", LogSeverity.Info);

        for (var i = 0; i < Phases.Length; i++)
        {
            // Drives the native CMS live status *and* is captured as a StatusChanged log line.
            OnStatusChanged($"Phase {i + 1} of {Phases.Length}: {Phases[i]}");

            Thread.Sleep(400);
            Log($"{Phases[i]} finished.", LogSeverity.Success);
        }

        OnStatusChanged("Finalizing");
        return $"Completed {Phases.Length} phases.";
    }
}
