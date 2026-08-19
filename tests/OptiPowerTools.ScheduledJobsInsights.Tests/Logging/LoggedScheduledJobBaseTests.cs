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
}
