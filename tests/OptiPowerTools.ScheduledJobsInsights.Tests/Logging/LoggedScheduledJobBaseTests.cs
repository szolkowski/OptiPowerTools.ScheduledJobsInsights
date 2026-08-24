using EPiServer.DataAbstraction;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

public class LoggedScheduledJobBaseTests
{
    [Fact]
    public void Execute_OnSuccess_BeginsAndCompletesExecutionWithResult()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(42L);
        var repository = Substitute.For<IScheduledJobRepository>();
        var job = new TestLoggedJob(writer, repository) { ResultToReturn = "all good" };

        var result = job.Execute();

        Assert.Equal("all good", result);
        writer.Received(1).BeginExecution(job.ScheduledJobId, Arg.Any<string>(), typeof(TestLoggedJob).FullName!);
        writer.Received(1).Complete(42L, ExecutionStatus.Succeeded, resultMessage: "all good", exception: null);
    }

    [Fact]
    public void Execute_OnFailure_CompletesAsFailedBeforeRethrowing()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(7L);
        var repository = Substitute.For<IScheduledJobRepository>();
        var thrown = new InvalidOperationException("boom");
        var job = new TestLoggedJob(writer, repository) { ExceptionToThrow = thrown };

        var caught = Assert.Throws<InvalidOperationException>(() => job.Execute());

        Assert.Same(thrown, caught);
        writer.Received(1).Complete(7L, ExecutionStatus.Failed, resultMessage: null, exception: thrown);
    }

    [Fact]
    public void Execute_RecordsAutomaticMetrics()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(1L);
        var repository = Substitute.For<IScheduledJobRepository>();
        var job = new TestLoggedJob(writer, repository);

        job.Execute();

        writer.Received(1).RecordMetric(1L, JobMetricNames.DurationMs, Arg.Any<double>(), "ms");
        writer.Received(1).RecordMetric(1L, JobMetricNames.AllocatedBytes, Arg.Any<double>(), "bytes");
        writer.Received(1).RecordMetric(1L, JobMetricNames.CpuTimeMs, Arg.Any<double>(), "ms");
        writer.Received(1).RecordMetric(1L, JobMetricNames.GcGen0Collections, Arg.Any<double>(), null);
        // All three generations: asserting only Gen0 let the Gen1/Gen2 lines be deleted silently.
        writer.Received(1).RecordMetric(1L, JobMetricNames.GcGen1Collections, Arg.Any<double>(), null);
        writer.Received(1).RecordMetric(1L, JobMetricNames.GcGen2Collections, Arg.Any<double>(), null);
    }

    [Fact]
    public void Execute_CustomLogAndMetricCalls_ForwardToWriter()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(9L);
        var repository = Substitute.For<IScheduledJobRepository>();
        var job = new TestLoggedJob(writer, repository);

        job.Execute();

        writer.Received(1).Log(9L, Arg.Any<int>(), LogSeverity.Warning, "a plain log line", LogEntrySource.DevLog);
        writer.Received(1).SetInputData(9L, Arg.Is<string>(json => json.Contains("input")));
        writer.Received(1).RecordMetric(9L, "CustomMetric", 42, "count");
    }

    [Fact]
    public void OnStatusChanged_LogsMessageAndRaisesNativeEvent()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(3L);
        var repository = Substitute.For<IScheduledJobRepository>();
        var job = new TestLoggedJob(writer, repository) { RaiseStatusChanged = true, StatusChangedMessage = "halfway there" };

        string? capturedMessage = null;
        job.StatusChanged += (_, args) => capturedMessage = args.Message;

        job.Execute();

        Assert.Equal("halfway there", capturedMessage);
        writer.Received(1).Log(3L, Arg.Any<int>(), LogSeverity.Info, "halfway there", LogEntrySource.StatusChanged);
    }

    [Fact]
    public void Execute_ResolvesJobName_FromScheduledJobRepository()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(1L);
        var repository = Substitute.For<IScheduledJobRepository>();
        repository.Get(Arg.Any<Guid>()).Returns(new ScheduledJob { Name = "Configured Job Name" });
        var job = new TestLoggedJob(writer, repository);

        job.Execute();

        writer.Received(1).BeginExecution(Arg.Any<Guid>(), "Configured Job Name", Arg.Any<string>());
    }

    [Fact]
    public void Execute_FallsBackToTypeName_WhenRepositoryLookupFails()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(1L);
        var repository = Substitute.For<IScheduledJobRepository>();
        repository.Get(Arg.Any<Guid>()).Returns(_ => throw new InvalidOperationException("not found"));
        var job = new TestLoggedJob(writer, repository);

        job.Execute();

        writer.Received(1).BeginExecution(Arg.Any<Guid>(), nameof(TestLoggedJob), Arg.Any<string>());
    }

    [Fact]
    public void Execute_WithNoSummaryRecorded_NeverWritesOne()
    {
        // The summary is opt-in: a job that never touches it must not cost an extra round trip.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(11L);
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>());

        job.Execute();

        writer.DidNotReceive().SetResultSummary(Arg.Any<long>(), Arg.Any<string>());
    }

    [Fact]
    public void Execute_WithSummaryRecorded_PersistsItBeforeCompleting()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(12L);
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>())
        {
            SummaryToAppend = "12 rows exported"
        };

        job.Execute();

        // Ordering matters: the detail view reads both from the same row, and a summary landing after
        // the execution is marked finished would briefly show a completed run with no summary.
        Received.InOrder(() =>
        {
            writer.SetResultSummary(12L, Arg.Is<string>(text => text.StartsWith("12 rows exported")));
            writer.Complete(12L, ExecutionStatus.Succeeded, Arg.Any<string>(), null);
        });
    }

    [Fact]
    public void Execute_PreservesNewlinesInTheSummary()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(13L);
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>())
        {
            SummaryToAppend = "first line"
        };

        job.Execute();

        writer.Received(1).SetResultSummary(13L, $"first line{Environment.NewLine}");
    }

    [Fact]
    public void Execute_OnFailure_StillPersistsTheSummary()
    {
        // Whatever the job managed to summarise before throwing is usually the most useful thing on
        // the page when diagnosing that failure.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(14L);
        var thrown = new InvalidOperationException("boom");
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>())
        {
            SummaryToAppend = "aborted at row 42",
            ExceptionToThrow = thrown
        };

        Assert.Throws<InvalidOperationException>(() => job.Execute());

        writer.Received(1).SetResultSummary(14L, Arg.Is<string>(text => text.Contains("aborted at row 42")));
        writer.Received(1).Complete(14L, ExecutionStatus.Failed, null, thrown);
    }

    [Fact]
    public void SetSummary_ReplacesTheSummaryContent()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(15L);
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>())
        {
            SummaryToAppend = "the whole summary",
            UseSetSummary = true
        };

        job.Execute();

        writer.Received(1).SetResultSummary(15L, "the whole summary");
    }

    [Fact]
    public void FlushSummary_WritesACheckpoint_AndTheFinalFlushStillFollows()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(16L);
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>())
        {
            SummaryToAppend = "batch 1 committed",
            CheckpointSummary = true
        };

        job.Execute();

        // Once mid-run, once on the way out — each overwriting the last.
        writer.Received(2).SetResultSummary(16L, Arg.Any<string>());
    }

    [Fact]
    public void Summary_HonoursTheConfiguredLimit()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(17L);
        var job = new TestLoggedJob(writer, maxResultSummaryLength: 24)
        {
            SummaryToAppend = new string('x', 500)
        };

        job.Execute();

        writer.Received(1).SetResultSummary(17L, Arg.Is<string>(text => text.Length <= 24));
    }

    [Fact]
    public void Summary_FallsBackToTheDefaultLimit_WhenNoneIsConfigured()
    {
        // Zero means "not configured" rather than "no summaries allowed"; a job must not blow up on it.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(18L);
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>())
        {
            SummaryToAppend = "still recorded"
        };

        job.Execute();

        writer.Received(1).SetResultSummary(18L, Arg.Is<string>(text => text.Contains("still recorded")));
    }

    [Fact]
    public void Execute_WhenTheJobWasStopped_RecordsStoppedRatherThanSucceeded()
    {
        // A run cut short by an administrator did not succeed, whatever it managed to return.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(50L);
        var job = new TestLoggedJob(writer) { StopMidRun = true, ResultToReturn = "stopped early" };

        job.Execute();

        Assert.True(job.SawStopRequest);
        writer.Received(1).Complete(50L, ExecutionStatus.Stopped, "stopped early", null);
    }

    [Fact]
    public void Execute_WithoutAStopRequest_StillRecordsTheNaturalOutcome()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(51L);
        var job = new TestLoggedJob(writer);

        job.Execute();

        Assert.False(job.SawStopRequest);
        writer.Received(1).Complete(51L, ExecutionStatus.Succeeded, Arg.Any<string>(), null);
    }

    [Fact]
    public void StopToken_IsCancelledByStop_AndIsNoneOnceTheRunIsOver()
    {
        // The source is created per run and disposed with it, so a nightly job does not accumulate
        // one per execution. Outside a run the token is simply None rather than a disposed source.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(57L);
        var job = new TokenObservingJob(writer);

        job.Execute();

        Assert.True(job.TokenWasCancellable);
        Assert.True(job.TokenWasCancelledAfterStop);
        Assert.False(job.CurrentToken.CanBeCanceled);
    }

    [Fact]
    public void Stop_AfterTheRunHasFinished_DoesNotThrow()
    {
        // The CMS calls Stop, not the job. A stop arriving just as the run ended must not surface as
        // an ObjectDisposedException out of Optimizely's own scheduler.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(58L);
        var job = new TestLoggedJob(writer);
        job.Execute();

        Assert.Null(Record.Exception(job.Stop));
    }

    [Fact]
    public void LogInputData_WithACyclicObjectGraph_DoesNotThrowIntoTheJob()
    {
        // An EF navigation or an IContent is a reference cycle, and System.Text.Json throws on one.
        // A job that merely described its own input must not fail because of it.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(52L);
        var job = new CyclicInputJob(writer);

        var result = job.Execute();

        Assert.Equal("done", result);
        writer.Received(1).Complete(52L, ExecutionStatus.Succeeded, Arg.Any<string>(), null);
    }

    [Fact]
    public void LogInputData_WithAnUnserializableValue_RecordsWhyRatherThanNothing()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(53L);

        new UnserializableInputJob(writer).Execute();

        writer.Received(1).SetInputData(53L, Arg.Is<string>(json => json.Contains("InputDataUnavailable")));
    }

    [Fact]
    public void LogInputData_WhenAPropertyGetterThrows_DoesNotThrowIntoTheJob()
    {
        // The case a filtered catch missed: System.Text.Json wraps its own failures, but propagates
        // whatever a getter throws. A disposed lazy-loading proxy is the everyday shape of this, and
        // it used to escape LogInputData, escape ExecuteJob, and be reported as the job's failure.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(55L);
        var job = new ThrowingGetterInputJob(writer);

        var result = job.Execute();

        Assert.Equal("done", result);
        writer.Received(1).Complete(55L, ExecutionStatus.Succeeded, Arg.Any<string>(), null);
        writer.Received(1).SetInputData(55L, Arg.Is<string>(json => json.Contains("InputDataUnavailable")));
    }

    [Fact]
    public void LogInputData_WhenTheExceptionMessageAlsoThrows_RecordsTheTypeName()
    {
        // Exception.Message is overridable, so describing the failure can fail too. The type name is
        // the one thing that cannot throw.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(56L);

        var result = new HostileGetterInputJob(writer).Execute();

        Assert.Equal("done", result);
        writer.Received(1).SetInputData(56L, Arg.Is<string>(json => json.Contains(nameof(HostileException))));
    }

    [Fact]
    public void Stop_IsSealed_SoTheBookkeepingCannotBeSkipped()
    {
        // The reason it is sealed: an override that forgot base.Stop() lost IsStopRequested and
        // StopToken silently, and a run cut short was then recorded as Succeeded.
        var stop = typeof(LoggedScheduledJobBase).GetMethod(nameof(LoggedScheduledJobBase.Stop));

        Assert.NotNull(stop);
        Assert.True(stop.IsFinal, "Stop() must stay sealed; override OnStopRequested instead.");
    }

    [Fact]
    public void OnStopRequested_RunsAfterTheStopIsAlreadyRecorded()
    {
        // Ordering is the contract: by the time a job's own handler runs, the stop is registered and
        // the token cancelled, so the handler cannot change the recorded outcome.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(57L);
        var job = new StopSeamJob(writer);

        job.Execute();

        Assert.True(job.SeamCalled);
        Assert.True(job.StopWasAlreadyRecorded);
        writer.Received(1).Complete(57L, ExecutionStatus.Stopped, Arg.Any<string>(), null);
    }

    [Fact]
    public void OnStopRequested_Throwing_DoesNotLoseTheStopOrEscapeIntoTheCms()
    {
        // Stop() is called by the CMS, not by the job, so a careless handler must not throw there —
        // and the run must still be recorded as stopped rather than as a success.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(58L);
        var job = new StopSeamJob(writer) { SeamThrows = true };

        Assert.Null(Record.Exception(() => job.Execute()));

        writer.Received(1).Complete(58L, ExecutionStatus.Stopped, Arg.Any<string>(), null);
        writer.Received().Log(58L, Arg.Any<int>(), LogSeverity.Warning,
            Arg.Is<string>(m => m.Contains("OnStopRequested")), Arg.Any<LogEntrySource>());
    }

    [Fact]
    public void Execute_WhenRecordingMetricsThrows_StillReportsTheRunAsSucceeded()
    {
        // Metrics are the least important thing this class does, and must never be able to turn a
        // clean run into a reported failure.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(54L);
        writer.When(w => w.RecordMetric(Arg.Any<long>(), JobMetricNames.DurationMs, Arg.Any<double>(), Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("metric sink exploded"));
        var job = new TestLoggedJob(writer) { ResultToReturn = "fine" };

        var result = job.Execute();

        Assert.Equal("fine", result);
        writer.Received(1).Complete(54L, ExecutionStatus.Succeeded, "fine", null);
    }

    [Fact]
    public void Execute_WhenRecordingMetricsThrowsOnTheFailurePath_StillRethrowsTheJobsOwnException()
    {
        // Otherwise the metrics exception replaces the real one and the row is stranded at Running.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(55L);
        // Only the automatic metric: a throw from the job's own RecordMetric call is the job's
        // problem, and this test is about the wrapper's.
        writer.When(w => w.RecordMetric(Arg.Any<long>(), JobMetricNames.DurationMs, Arg.Any<double>(), Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("metric sink exploded"));
        var thrown = new InvalidOperationException("the real failure");
        var job = new TestLoggedJob(writer) { ExceptionToThrow = thrown };

        var caught = Assert.Throws<InvalidOperationException>(() => job.Execute());

        Assert.Same(thrown, caught);
        writer.Received(1).Complete(55L, ExecutionStatus.Failed, null, thrown);
    }

    [Fact]
    public void Execute_WhenCompletingTheExecutionThrows_StillRethrowsTheJobsOwnException()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(56L);
        writer.When(w => w.Complete(Arg.Any<long>(), Arg.Any<ExecutionStatus>(), Arg.Any<string>(), Arg.Any<Exception>()))
            .Do(_ => throw new InvalidOperationException("writer exploded"));
        var thrown = new InvalidOperationException("the real failure");
        var job = new TestLoggedJob(writer) { ExceptionToThrow = thrown };

        Assert.Same(thrown, Assert.Throws<InvalidOperationException>(() => job.Execute()));
    }

    /// <summary>
    /// A writer that cannot record anything — what every method sees when the insights database is
    /// unreachable. NSubstitute returns null for the nullable BeginExecution by default, so this is
    /// really just naming the condition.
    /// </summary>
    private static IJobExecutionWriter UnavailableWriter()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns((long?)null);
        return writer;
    }

    [Fact]
    public void Execute_WhenTheExecutionCannotBeRecorded_StillRunsTheJob()
    {
        // The whole point: an unreachable reporting database costs the history of a run, never the
        // run. A package that observes jobs must not be able to stop them.
        var writer = UnavailableWriter();
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>()) { ResultToReturn = "did the work" };

        var result = job.Execute();

        Assert.Equal("did the work", result);
    }

    [Fact]
    public void Execute_WhenTheExecutionCannotBeRecorded_WritesNothingElse()
    {
        // Every later write is keyed on an execution id that does not exist. Attempting them would
        // produce a foreign-key violation per log line and bury the one warning that matters.
        var writer = UnavailableWriter();
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>())
        {
            RaiseStatusChanged = true,
            SummaryToAppend = "not recorded"
        };

        job.Execute();

        writer.DidNotReceive().Complete(Arg.Any<long>(), Arg.Any<ExecutionStatus>(), Arg.Any<string>(), Arg.Any<Exception>());
        writer.DidNotReceive().Log(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<LogSeverity>(), Arg.Any<string>(), Arg.Any<LogEntrySource>());
        writer.DidNotReceive().RecordMetric(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<double>(), Arg.Any<string>());
        writer.DidNotReceive().SetInputData(Arg.Any<long>(), Arg.Any<string>());
        writer.DidNotReceive().SetResultSummary(Arg.Any<long>(), Arg.Any<string>());
    }

    [Fact]
    public void Execute_WhenTheExecutionCannotBeRecorded_StillRaisesTheNativeStatusChangedEvent()
    {
        // The CMS admin's live status column is driven by this event, and it belongs to Optimizely,
        // not to us. It has to keep firing even when nothing can be persisted.
        var job = new TestLoggedJob(UnavailableWriter(), Substitute.For<IScheduledJobRepository>())
        {
            RaiseStatusChanged = true,
            StatusChangedMessage = "halfway"
        };

        var observed = new List<string>();
        job.StatusChanged += (_, args) => observed.Add(args.Message);

        job.Execute();

        Assert.Contains("halfway", observed);
    }

    [Fact]
    public void Execute_WhenTheExecutionCannotBeRecorded_StillRethrowsAJobFailure()
    {
        // Optimizely sets HasLastExecutionFailed from what Execute() throws. Losing our recording
        // must not quietly turn a failed job into a successful one.
        var thrown = new InvalidOperationException("boom");
        var job = new TestLoggedJob(UnavailableWriter(), Substitute.For<IScheduledJobRepository>())
        {
            ExceptionToThrow = thrown
        };

        Assert.Same(thrown, Assert.Throws<InvalidOperationException>(() => job.Execute()));
    }
}
