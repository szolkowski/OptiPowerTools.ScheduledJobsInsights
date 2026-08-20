using EPiServer.DataAbstraction;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Retention;

/// <summary>
/// Real logged jobs carrying real attributes, so discovery is exercised the way it works in
/// production — scanning loaded assemblies for <see cref="LoggedScheduledJobBase"/> subclasses —
/// rather than against a hand-built dictionary. They are never executed; only their types matter.
/// </summary>
internal abstract class RetentionTestJobBase : LoggedScheduledJobBase
{
    protected RetentionTestJobBase(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
        : base(writer, scheduledJobRepository)
    {
    }

    protected override string ExecuteJob() => "never run";
}

[JobRetention(7, Description = "Logs one line per row; a week is plenty.")]
internal sealed class ChattyTestJob : RetentionTestJobBase
{
    public ChattyTestJob(IJobExecutionWriter writer, IScheduledJobRepository repository) : base(writer, repository) { }
}

[JobRetention(JobRetentionAttribute.Indefinite, Description = "Compliance requires the full history.")]
internal sealed class ForeverTestJob : RetentionTestJobBase
{
    public ForeverTestJob(IJobExecutionWriter writer, IScheduledJobRepository repository) : base(writer, repository) { }
}

/// <summary>Declares a value that is neither positive nor <see cref="JobRetentionAttribute.Indefinite"/>.</summary>
[JobRetention(0)]
internal sealed class InvalidRetentionTestJob : RetentionTestJobBase
{
    public InvalidRetentionTestJob(IJobExecutionWriter writer, IScheduledJobRepository repository) : base(writer, repository) { }
}

/// <summary>A logged job with no attribute at all — it should still be listed, on the default.</summary>
internal sealed class PlainTestJob : RetentionTestJobBase
{
    public PlainTestJob(IJobExecutionWriter writer, IScheduledJobRepository repository) : base(writer, repository) { }
}
