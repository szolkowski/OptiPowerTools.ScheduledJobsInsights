using EPiServer.DataAbstraction;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

/// <summary>A logged job declaring a display name on its scheduled-job attribute, as real jobs do.</summary>
#if CMS12
[EPiServer.PlugIn.ScheduledPlugIn(DisplayName = DeclaredName)]
#else
[EPiServer.Scheduler.ScheduledJob(DisplayName = DeclaredName)]
#endif
internal sealed class DisplayNamedTestJob : LoggedScheduledJobBase
{
    public const string DeclaredName = "Nightly Catalog Sync";

    public DisplayNamedTestJob(IJobExecutionWriter writer, IScheduledJobRepository? scheduledJobRepository = null)
        : base(TestJobLoggingContext.For(writer, scheduledJobRepository, 0, null))
    {
    }

    protected override string ExecuteJob() => "done";
}
