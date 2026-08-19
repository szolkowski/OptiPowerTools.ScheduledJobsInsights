using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);
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
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);
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
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);
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
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);
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
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);
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
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type");

        // Fill the only slot so the next write can't be buffered.
        channel.Writer.TryWrite(new LogRecordItem(executionId, 0, LogSeverity.Info, "filler", LogEntrySource.DevLog, DateTimeOffset.UtcNow));

        writer.Log(executionId, 1, LogSeverity.Error, "overflow", LogEntrySource.DevLog);

        using var dbContext = factory.CreateDbContext();
        Assert.Single(dbContext.JobLogEntries.Where(e => e.JobExecutionId == executionId && e.Message == "overflow"));
    }

    [Fact]
    public void SetResultSummary_PersistsMultiLineTextVerbatim()
    {
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type");
        var summary = "Totals\n------\n  Rows: 12\n  Skipped: 3";

        writer.SetResultSummary(executionId, summary);

        using var dbContext = factory.CreateDbContext();
        Assert.Equal(summary, dbContext.JobExecutions.Single(e => e.Id == executionId).ResultSummary);
    }

    [Fact]
    public void SetResultSummary_ReplacesAnyPreviousValue()
    {
        // FlushSummary can be called repeatedly while a long job checkpoints its progress, so each
        // write has to overwrite rather than accumulate.
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type");

        writer.SetResultSummary(executionId, "first");
        writer.SetResultSummary(executionId, "second");

        using var dbContext = factory.CreateDbContext();
        Assert.Equal("second", dbContext.JobExecutions.Single(e => e.Id == executionId).ResultSummary);
    }

    [Fact]
    public void SetResultSummary_TruncatesToConfiguredLimit()
    {
        // Callers using IJobExecutionWriter directly bypass JobResultSummary's own bound, so the
        // writer is the backstop that keeps the column from growing without limit.
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.WithSummaryLimit(32), NullLogger<JobExecutionWriter>.Instance);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type");

        writer.SetResultSummary(executionId, new string('x', 500));

        using var dbContext = factory.CreateDbContext();
        Assert.Equal(32, dbContext.JobExecutions.Single(e => e.Id == executionId).ResultSummary!.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxResultSummaryLength_FallsBackToDefault_WhenConfiguredValueIsNotPositive(int configured)
    {
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();

        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.WithSummaryLimit(configured), NullLogger<JobExecutionWriter>.Instance);

        Assert.Equal(JobResultSummary.DefaultMaxLength, writer.MaxResultSummaryLength);
    }

    [Fact]
    public void Log_DoesNotThrow_WhenTheSynchronousFallbackFails()
    {
        // A full buffer forces the synchronous path, and the database is unavailable. The job that
        // called Log() must carry on regardless — this package observes executions, it does not get
        // to fail them.
        var factory = new FailingDbContextFactory();
        var channel = Channel.CreateBounded<JobRecord>(1);
        channel.Writer.TryWrite(new LogRecordItem(1, 1, LogSeverity.Info, "fills the buffer", LogEntrySource.DevLog, DateTimeOffset.UtcNow));
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);

        writer.Log(1, 2, LogSeverity.Info, "dropped but harmless", LogEntrySource.DevLog);

        Assert.Equal(1, factory.Attempts);
    }

    [Fact]
    public void RecordMetric_DoesNotThrow_WhenTheSynchronousFallbackFails()
    {
        var factory = new FailingDbContextFactory();
        var channel = Channel.CreateBounded<JobRecord>(1);
        channel.Writer.TryWrite(new MetricRecordItem(1, "FillsTheBuffer", 1, null, DateTimeOffset.UtcNow));
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);

        writer.RecordMetric(1, "DroppedButHarmless", 1, null);

        Assert.Equal(1, factory.Attempts);
    }

    [Fact]
    public void BeginExecution_StillThrows_WhenTheDatabaseIsUnavailable()
    {
        // The deliberate exception to the rule above: everything else is keyed on the id this
        // returns, so there is nothing sensible to carry on with.
        var factory = new FailingDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);

        Assert.Throws<InvalidOperationException>(() => writer.BeginExecution(Guid.NewGuid(), "Job", "Job.Type"));
    }
}
