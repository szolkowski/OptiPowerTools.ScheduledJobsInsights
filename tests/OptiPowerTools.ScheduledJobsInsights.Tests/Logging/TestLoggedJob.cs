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

    /// <summary>Records whether the run observed a stop request.</summary>
    public bool SawStopRequest { get; private set; }

    /// <summary>When true, <c>ExecuteJob</c> calls <c>Stop()</c> on itself part-way through.</summary>
    public bool StopMidRun { get; set; }

    public TestLoggedJob(
        IJobExecutionWriter writer,
        IScheduledJobRepository? scheduledJobRepository = null,
        int maxResultSummaryLength = 0,
        TimeProvider? timeProvider = null)
        : base(TestJobLoggingContext.For(writer, scheduledJobRepository, maxResultSummaryLength, timeProvider))
    {
    }

    public TestLoggedJob(JobLoggingContext context)
        : base(context)
    {
    }

    protected override string ExecuteJob()
    {
        if (StopMidRun)
            Stop();

        SawStopRequest = IsStopRequested;

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

/// <summary>A node that references itself — the shape of an EF navigation or an IContent.</summary>
internal sealed class CyclicNode
{
    public string Name { get; init; } = "root";

    public CyclicNode? Self { get; set; }
}

/// <summary>Logs input data that <c>System.Text.Json</c> cannot serialize without help.</summary>
internal sealed class CyclicInputJob : LoggedScheduledJobBase
{
    public CyclicInputJob(IJobExecutionWriter writer)
        : base(TestJobLoggingContext.For(writer))
    {
    }

    protected override string ExecuteJob()
    {
        var node = new CyclicNode();
        node.Self = node;
        LogInputData(node);
        return "done";
    }
}

/// <summary>Logs a value whose type <c>System.Text.Json</c> refuses outright.</summary>
internal sealed class UnserializableInputJob : LoggedScheduledJobBase
{
    public UnserializableInputJob(IJobExecutionWriter writer)
        : base(TestJobLoggingContext.For(writer))
    {
    }

    protected override string ExecuteJob()
    {
        // A dictionary keyed by a type with no supported converter.
        LogInputData(new Dictionary<CyclicNode, string> { [new CyclicNode()] = "value" });
        return "done";
    }
}
