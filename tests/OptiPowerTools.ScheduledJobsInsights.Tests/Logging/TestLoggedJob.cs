using EPiServer.DataAbstraction;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

/// <summary>Test double exercising every protected member of <see cref="LoggedScheduledJobBase"/>.</summary>
internal sealed class TestLoggedJob : LoggedScheduledJobBase
{
    public Exception? ExceptionToThrow { get; set; }

    public string ResultToReturn { get; set; } = "done";

    public bool RaiseStatusChanged { get; set; }

    public string StatusChangedMessage { get; set; } = "status update";

    public TestLoggedJob(IJobExecutionWriter writer, IScheduledJobRepository scheduledJobRepository)
        : base(writer, scheduledJobRepository)
    {
    }

    protected override string ExecuteJob()
    {
        if (RaiseStatusChanged)
            OnStatusChanged(StatusChangedMessage);

        Log("a plain log line", LogSeverity.Warning);
        LogInputData(new { Sample = "input" });
        RecordMetric("CustomMetric", 42, "count");

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return ResultToReturn;
    }
}
