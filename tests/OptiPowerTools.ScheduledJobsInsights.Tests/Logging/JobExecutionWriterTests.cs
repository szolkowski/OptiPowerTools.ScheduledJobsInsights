using System.Threading.Channels;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Tests.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

public class JobExecutionWriterTests
{
    [Fact]
    public void BeginExecution_InsertsRunningExecution_AndReturnsGeneratedId()
    {
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel);
        var scheduledJobId = Guid.NewGuid();

        var executionId = writer.BeginExecution(scheduledJobId, "My Job", "My.Job.Type");

        using var dbContext = factory.CreateDbContext();
        var execution = dbContext.JobExecutions.Single(e => e.Id == executionId);
        Assert.Equal(scheduledJobId, execution.ScheduledJobId);
        Assert.Equal("My Job", execution.JobName);
        Assert.Equal(ExecutionStatus.Running, execution.Status);
        Assert.Null(execution.CompletedAt);
    }

    [Fact]
    public void Complete_UpdatesStatusAndResult()
    {
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type");

        writer.Complete(executionId, succeeded: true, resultMessage: "all good", exception: null);

        using var dbContext = factory.CreateDbContext();
        var execution = dbContext.JobExecutions.Single(e => e.Id == executionId);
        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal("all good", execution.ResultMessage);
        Assert.NotNull(execution.CompletedAt);
    }

    [Fact]
    public void Complete_OnFailure_RecordsExceptionDetails()
    {
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type");
        var exception = new InvalidOperationException("kaboom");

        writer.Complete(executionId, succeeded: false, resultMessage: null, exception: exception);

        using var dbContext = factory.CreateDbContext();
        var execution = dbContext.JobExecutions.Single(e => e.Id == executionId);
        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        Assert.Equal("kaboom", execution.ExceptionMessage);
    }

    [Fact]
    public void SetInputData_UpdatesInputDataJson()
    {
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type");

        writer.SetInputData(executionId, "{\"foo\":\"bar\"}");

        using var dbContext = factory.CreateDbContext();
        Assert.Equal("{\"foo\":\"bar\"}", dbContext.JobExecutions.Single(e => e.Id == executionId).InputDataJson);
    }

    [Fact]
    public void Log_WhenChannelHasCapacity_BuffersInsteadOfWritingImmediately()
    {
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type");

        writer.Log(executionId, 1, LogSeverity.Info, "buffered", LogEntrySource.DevLog);

        using (var dbContext = factory.CreateDbContext())
            Assert.Empty(dbContext.JobLogEntries.Where(e => e.JobExecutionId == executionId));

        Assert.True(channel.Reader.TryRead(out var buffered));
        var logRecord = Assert.IsType<LogRecordItem>(buffered);
        Assert.Equal("buffered", logRecord.Message);
    }

    [Fact]
    public void Log_WhenChannelIsFull_FallsBackToSynchronousInsert()
    {
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateBounded<JobRecord>(1);
        var writer = new JobExecutionWriter(factory, channel);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type");

        // Fill the only slot so the next write can't be buffered.
        channel.Writer.TryWrite(new LogRecordItem(executionId, 0, LogSeverity.Info, "filler", LogEntrySource.DevLog, DateTimeOffset.UtcNow));

        writer.Log(executionId, 1, LogSeverity.Error, "overflow", LogEntrySource.DevLog);

        using var dbContext = factory.CreateDbContext();
        Assert.Single(dbContext.JobLogEntries.Where(e => e.JobExecutionId == executionId && e.Message == "overflow"));
    }
}
