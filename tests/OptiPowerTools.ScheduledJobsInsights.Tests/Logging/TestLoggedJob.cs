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

    /// <summary>
    /// Registers a <c>StopToken</c> callback that throws — the shape of a job cancelling an
    /// already-disposed HttpClient. Cancel() runs registrations synchronously on the caller's thread.
    /// </summary>
    public bool RegisterThrowingStopCallback { get; set; }

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
        if (RegisterThrowingStopCallback)
            StopToken.Register(static () => throw new InvalidOperationException("stop callback boom"));

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

/// <summary>
/// A node whose getter throws, which is what a disposed lazy-loading proxy or a computed
/// <c>IContent</c> property does.
/// </summary>
/// <remarks>
/// The distinction that matters: <c>System.Text.Json</c> raises its own failures as
/// <c>JsonException</c>/<c>NotSupportedException</c>, but propagates a getter's exception unchanged.
/// So this is not "serializable with difficulty", it is a throw from user code arriving through the
/// serializer — which a filtered catch does not see.
/// </remarks>
internal sealed class ThrowingGetterNode
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Must stay an instance property: System.Text.Json only visits instance properties, so as a static member the getter would never be called and the test would silently stop exercising the throwing-getter path it exists for.")]
    public string Name => throw new ObjectDisposedException("SomeDbContext");
}

/// <summary>Logs input data whose property getter throws an exception of its own.</summary>
internal sealed class ThrowingGetterInputJob : LoggedScheduledJobBase
{
    public ThrowingGetterInputJob(IJobExecutionWriter writer)
        : base(TestJobLoggingContext.For(writer))
    {
    }

    protected override string ExecuteJob()
    {
        LogInputData(new ThrowingGetterNode());
        return "done";
    }
}

/// <summary>An exception whose <c>Message</c> throws — the fallback path of the guard.</summary>
internal sealed class HostileException : Exception
{
    public override string Message => throw new InvalidOperationException("even the message throws");
}

/// <summary>A node whose getter throws a <see cref="HostileException"/>.</summary>
internal sealed class HostileNode
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Must stay an instance property: System.Text.Json only visits instance properties, so as a static member the getter would never be called and the test would silently stop exercising the throwing-getter path it exists for.")]
    public string Name => throw new HostileException();
}

/// <summary>Logs input data whose getter throws an exception that cannot be described either.</summary>
internal sealed class HostileGetterInputJob : LoggedScheduledJobBase
{
    public HostileGetterInputJob(IJobExecutionWriter writer)
        : base(TestJobLoggingContext.For(writer))
    {
    }

    protected override string ExecuteJob()
    {
        LogInputData(new HostileNode());
        return "done";
    }
}

/// <summary>Uses the <c>OnStopRequested</c> seam that replaced overriding <c>Stop()</c>.</summary>
internal sealed class StopSeamJob : LoggedScheduledJobBase
{
    public StopSeamJob(IJobExecutionWriter writer)
        : base(TestJobLoggingContext.For(writer))
    {
    }

    public bool SeamCalled { get; private set; }

    /// <summary>Whether the base class had already recorded the stop by the time the seam ran.</summary>
    public bool StopWasAlreadyRecorded { get; private set; }

    /// <summary>Set to have the seam throw, as a careless override would.</summary>
    public bool SeamThrows { get; set; }

    protected override void OnStopRequested()
    {
        SeamCalled = true;
        StopWasAlreadyRecorded = IsStopRequested && StopToken.IsCancellationRequested;

        if (SeamThrows)
            throw new InvalidOperationException("handler exploded");
    }

    protected override string ExecuteJob()
    {
        Stop();
        return "done";
    }
}

/// <summary>Observes <c>StopToken</c> across the boundaries of a run.</summary>
internal sealed class TokenObservingJob : LoggedScheduledJobBase
{
    public TokenObservingJob(IJobExecutionWriter writer)
        : base(TestJobLoggingContext.For(writer))
    {
    }

    public bool TokenWasCancellable { get; private set; }

    public bool TokenWasCancelledAfterStop { get; private set; }

    /// <summary>Reads the protected token from outside the run, which is the point of the test.</summary>
    public CancellationToken CurrentToken => StopToken;

    protected override string ExecuteJob()
    {
        TokenWasCancellable = StopToken.CanBeCanceled;
        Stop();
        TokenWasCancelledAfterStop = StopToken.IsCancellationRequested;
        return "done";
    }
}
