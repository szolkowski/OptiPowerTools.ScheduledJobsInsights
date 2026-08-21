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
    protected RetentionTestJobBase(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob() => "never run";
}

[JobRetention(7, Description = "Logs one line per row; a week is plenty.")]
internal sealed class ChattyTestJob : RetentionTestJobBase
{
    public ChattyTestJob(JobLoggingContext context) : base(context) { }
}

[JobRetention(JobRetentionAttribute.Indefinite, Description = "Compliance requires the full history.")]
internal sealed class ForeverTestJob : RetentionTestJobBase
{
    public ForeverTestJob(JobLoggingContext context) : base(context) { }
}

/// <summary>Declares a value that is neither positive nor <see cref="JobRetentionAttribute.Indefinite"/>.</summary>
[JobRetention(0)]
internal sealed class InvalidRetentionTestJob : RetentionTestJobBase
{
    public InvalidRetentionTestJob(JobLoggingContext context) : base(context) { }
}

/// <summary>A logged job with no attribute at all — it should still be listed, on the default.</summary>
internal sealed class PlainTestJob : RetentionTestJobBase
{
    public PlainTestJob(JobLoggingContext context) : base(context) { }
}
