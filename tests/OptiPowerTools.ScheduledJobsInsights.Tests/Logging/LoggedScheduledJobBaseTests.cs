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
        writer.Received(1).Complete(42L, succeeded: true, resultMessage: "all good", exception: null);
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
        writer.Received(1).Complete(7L, succeeded: false, resultMessage: null, exception: thrown);
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
            writer.Complete(12L, true, Arg.Any<string>(), null);
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
        writer.Received(1).Complete(14L, false, null, thrown);
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
    public void Summary_HonoursTheWriterConfiguredLimit()
    {
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(17L);
        writer.MaxResultSummaryLength.Returns(24);
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>())
        {
            SummaryToAppend = new string('x', 500)
        };

        job.Execute();

        writer.Received(1).SetResultSummary(17L, Arg.Is<string>(text => text.Length <= 24));
    }

    [Fact]
    public void Summary_FallsBackToTheDefaultLimit_WhenTheWriterReportsNone()
    {
        // A substituted writer reports 0 for an int property, and a job must not blow up on that.
        var writer = Substitute.For<IJobExecutionWriter>();
        writer.BeginExecution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(18L);
        var job = new TestLoggedJob(writer, Substitute.For<IScheduledJobRepository>())
        {
            SummaryToAppend = "still recorded"
        };

        job.Execute();

        writer.Received(1).SetResultSummary(18L, Arg.Is<string>(text => text.Contains("still recorded")));
    }
}
