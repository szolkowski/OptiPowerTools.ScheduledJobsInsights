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

    /// <summary>Appended to <c>Summary</c> when set. Left null so the default job records no summary.</summary>
    public string? SummaryToAppend { get; set; }

    /// <summary>Writes <see cref="SummaryToAppend"/> through <c>SetSummary</c> instead of appending.</summary>
    public bool UseSetSummary { get; set; }

    /// <summary>Calls <c>FlushSummary</c> mid-run, as a long job checkpointing its progress would.</summary>
    public bool CheckpointSummary { get; set; }

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

        if (SummaryToAppend is not null)
        {
            if (UseSetSummary)
                SetSummary(SummaryToAppend);
            else
                Summary.AppendLine(SummaryToAppend);

            if (CheckpointSummary)
                FlushSummary();
        }

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return ResultToReturn;
    }
}
