using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — emits one line at every <see cref="LogSeverity"/> value, so a
/// single execution renders the complete colour and label set the console log viewer can produce.
/// Useful when changing <c>LogSeverityStyles</c>, which is the only place severity becomes a colour.
/// </summary>
[ScheduledJob(DisplayName = "Sample: Severity Showcase", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class SeverityShowcaseJob : LoggedScheduledJobBase
{
    public SeverityShowcaseJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        // Log() defaults to LogSeverity.Default, rendered neutral grey and labelled "Log".
        Log("Default — an unclassified line, the severity you get when you do not pass one.");

        Log("Info — routine progress worth surfacing.", LogSeverity.Info);
        Log("Success — a step that completed as intended.", LogSeverity.Success);
        Log("Warning — recoverable, but someone should look.", LogSeverity.Warning);
        Log("Error — a failure that did not abort the run.", LogSeverity.Error);
        Log("Debug — verbose diagnostic detail, muted in the viewer.", LogSeverity.Debug);

        // OnStatusChanged lines are always Info, but carry LogEntrySource.StatusChanged.
        OnStatusChanged("Status line — same Info colour, different source.");

        return $"Emitted one line per severity ({Enum.GetValues<LogSeverity>().Length} total).";
    }
}
