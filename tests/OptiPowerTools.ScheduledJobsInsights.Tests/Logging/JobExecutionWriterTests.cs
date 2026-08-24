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

        var executionId = writer.BeginExecution(scheduledJobId, "My Job", "My.Job.Type")!.Value;

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
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;

        writer.Complete(executionId, ExecutionStatus.Succeeded, resultMessage: "all good", exception: null);

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
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;
        var exception = new InvalidOperationException("kaboom");

        writer.Complete(executionId, ExecutionStatus.Failed, resultMessage: null, exception: exception);

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
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;

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
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;

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
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;

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
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;
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
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;

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
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;

        writer.SetResultSummary(executionId, new string('x', 500));

        using var dbContext = factory.CreateDbContext();
        var stored = dbContext.JobExecutions.Single(e => e.Id == executionId).ResultSummary!;
        Assert.Equal(32, stored.Length);
        // Ends with the notice, so a truncated summary never reads as merely a short one.
        Assert.EndsWith(JobResultSummary.TruncationNotice, stored, StringComparison.Ordinal);
    }

    [Fact]
    public void SetResultSummary_DoesNotSplitASurrogatePair()
    {
        // Half a character renders as a replacement glyph; the notice budget must not cut one open.
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.WithSummaryLimit(32), NullLogger<JobExecutionWriter>.Instance);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;

        // Emoji are surrogate pairs, so every second char boundary falls inside one.
        writer.SetResultSummary(executionId, string.Concat(Enumerable.Repeat("😀", 200)));

        using var dbContext = factory.CreateDbContext();
        var stored = dbContext.JobExecutions.Single(e => e.Id == executionId).ResultSummary!;
        Assert.DoesNotContain('\uFFFD', stored);
        Assert.False(char.IsHighSurrogate(stored[^(JobResultSummary.TruncationNotice.Length + Environment.NewLine.Length + 1)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetResultSummary_FallsBackToTheDefaultBound_WhenConfiguredValueIsNotPositive(int configured)
    {
        // A misconfigured zero must not mean "no summary can ever be stored".
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.WithSummaryLimit(configured), NullLogger<JobExecutionWriter>.Instance);
        var executionId = writer.BeginExecution(Guid.NewGuid(), "My Job", "My.Job.Type")!.Value;

        writer.SetResultSummary(executionId, new string('x', 500));

        using var dbContext = factory.CreateDbContext();
        Assert.Equal(500, dbContext.JobExecutions.Single(e => e.Id == executionId).ResultSummary!.Length);
    }

    [Fact]
    public void RecordMetric_ClampsANameAndUnitTooLongForTheirColumns()
    {
        // A 300-character metric name does not merely lose itself: the insert fails, and it takes the
        // whole batch with it — including the log lines of every other job running at that moment.
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);

        writer.RecordMetric(1, new string('n', 300), 1, new string('u', 80));

        var record = Assert.IsType<MetricRecordItem>(Assert.Single(ReadAll(channel)));
        Assert.Equal(200, record.Name.Length);
        Assert.Equal(50, record.Unit!.Length);
    }

    [Fact]
    public void Log_ClampsAMessageBeyondTheConfiguredLimit()
    {
        // The column is unbounded, which is the problem: a job logging a response body per iteration
        // writes megabytes per row.
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions { MaxLogMessageLength = 32 });
        var writer = new JobExecutionWriter(factory, channel, options, NullLogger<JobExecutionWriter>.Instance);

        writer.Log(1, 1, LogSeverity.Info, new string('x', 500), LogEntrySource.DevLog);

        var record = Assert.IsType<LogRecordItem>(Assert.Single(ReadAll(channel)));
        Assert.Equal(32, record.Message.Length);
        Assert.EndsWith("…", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_LeavesAnOrdinaryMessageAlone()
    {
        using var factory = new SqliteDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);

        writer.Log(1, 1, LogSeverity.Info, "a perfectly normal line", LogEntrySource.DevLog);

        Assert.Equal("a perfectly normal line", Assert.IsType<LogRecordItem>(Assert.Single(ReadAll(channel))).Message);
    }

    private static List<JobRecord> ReadAll(Channel<JobRecord> channel)
    {
        var records = new List<JobRecord>();
        while (channel.Reader.TryRead(out var record))
            records.Add(record);
        return records;
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
    public void BeginExecution_ReturnsNull_WhenTheDatabaseIsUnavailable()
    {
        // Signalled, not thrown. A job is about to run; an unreachable reporting database must cost
        // the history of that run, never the run itself.
        var factory = new FailingDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);

        Assert.Null(writer.BeginExecution(Guid.NewGuid(), "Job", "Job.Type"));
    }

    [Fact]
    public void TheImmediateWrites_DoNotThrow_WhenTheDatabaseIsUnavailable()
    {
        // Complete/SetInputData/SetResultSummary all run while a job is executing, so none of them
        // may escape into it either.
        var factory = new FailingDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var writer = new JobExecutionWriter(factory, channel, TestWriterOptions.Default, NullLogger<JobExecutionWriter>.Instance);

        writer.SetInputData(1, "{}");
        writer.SetResultSummary(1, "summary");
        writer.Complete(1, ExecutionStatus.Succeeded, resultMessage: "done", exception: null);

        // Reaching here at all is the assertion; the count just confirms each one genuinely tried.
        Assert.Equal(3, factory.Attempts);
    }
}
