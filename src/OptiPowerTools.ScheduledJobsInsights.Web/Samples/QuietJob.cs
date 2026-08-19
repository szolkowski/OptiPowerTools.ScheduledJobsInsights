using EPiServer.DataAbstraction;
using EPiServer.Scheduler;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — the deliberately boring case: a job that logs nothing at all and
/// only returns a result message. Deriving from <see cref="LoggedScheduledJobBase"/> still records
/// the execution, its outcome and the automatic metrics, so this is what an unmodified job looks
/// like after simply changing its base class. It is also the only sample that exercises the detail
/// page's "No log lines recorded." empty state.
/// </summary>
[ScheduledJob(DisplayName = "Sample: Quiet", IntervalType = ScheduledIntervalType.Days, DefaultEnabled = false)]
public sealed class QuietJob : LoggedScheduledJobBase
{
    public QuietJob(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
        : base(writer, scheduledJobRepository)
    {
    }

    protected override string ExecuteJob() => "Nothing to do.";
}
