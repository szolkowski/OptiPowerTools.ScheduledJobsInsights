using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Tests.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

public class JobLogBackgroundWriterTests
{
    [Fact]
    public async Task StopAsync_FlushesAnyRecordsStillBuffered()
    {
        using var factory = new SqliteDbContextFactory();
        long executionId;
        await using (var dbContext = factory.CreateDbContext())
        {
            var execution = new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = "Job",
                JobTypeName = "Job",
                StartedAt = DateTimeOffset.UtcNow,
                Status = ExecutionStatus.Running,
                MachineName = "test"
            };
            dbContext.JobExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            executionId = execution.Id;
        }

        var channel = Channel.CreateUnbounded<JobRecord>();
        // A long flush interval proves the drain-on-shutdown path (not the periodic flush) is what delivers these records.
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions { LogBatchSize = 100, LogFlushInterval = TimeSpan.FromSeconds(30) });

        channel.Writer.TryWrite(new LogRecordItem(executionId, 1, LogSeverity.Info, "buffered line", LogEntrySource.DevLog, DateTimeOffset.UtcNow));
        channel.Writer.TryWrite(new MetricRecordItem(executionId, "BufferedMetric", 1, null, DateTimeOffset.UtcNow));

        var backgroundWriter = new JobLogBackgroundWriter(channel, factory, options, NullLogger<JobLogBackgroundWriter>.Instance);
        await backgroundWriter.StartAsync(CancellationToken.None);
        await backgroundWriter.StopAsync(CancellationToken.None);

        await using var verifyContext = factory.CreateDbContext();
        Assert.Single(verifyContext.JobLogEntries.Where(e => e.JobExecutionId == executionId && e.Message == "buffered line"));
        Assert.Single(verifyContext.JobMetrics.Where(e => e.JobExecutionId == executionId && e.Name == "BufferedMetric"));
    }

    [Fact]
    public async Task StopAsync_DoesNotDrainConcurrentlyWithTheCollector()
    {
        // base.StopAsync returns when *either* ExecuteAsync completes or the host's shutdown timeout
        // fires. On the timeout path it abandons a slow drain rather than awaiting it, and StopAsync
        // then starts a second one over the same List<JobRecord> — two threads mutating one list,
        // inside a hosted service whose whole design rule is "must not throw".
        using var sqlite = new SqliteDbContextFactory();
        var executionId = await SeedExecutionAsync(sqlite);

        using var factory = new GatedDbContextFactory(sqlite);
        var channel = Channel.CreateUnbounded<JobRecord>();
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            LogBatchSize = 10,
            LogFlushInterval = TimeSpan.FromMilliseconds(10)
        });

        channel.Writer.TryWrite(new LogRecordItem(executionId, 1, LogSeverity.Info, "first", LogEntrySource.DevLog, DateTimeOffset.UtcNow));
        channel.Writer.TryWrite(new LogRecordItem(executionId, 2, LogSeverity.Info, "second", LogEntrySource.DevLog, DateTimeOffset.UtcNow));

        var backgroundWriter = new JobLogBackgroundWriter(channel, factory, options, NullLogger<JobLogBackgroundWriter>.Instance);
        await backgroundWriter.StartAsync(CancellationToken.None);

        // A write is now in flight and held open, with the records already out of the channel.
        await factory.FirstCallEntered.WaitAsync(TimeSpan.FromSeconds(10));

        // An already-cancelled token is the shutdown-timeout path: base.StopAsync gives up waiting
        // for ExecuteAsync straight away.
        using var expired = new CancellationTokenSource();
        await expired.CancelAsync();
        var stopping = backgroundWriter.StopAsync(expired.Token);

        // Give the second drain every chance to barge in before the first one finishes.
        await Task.Delay(100);
        Assert.Equal(1, factory.Calls);

        factory.Release();
        await stopping;

        await using var verifyContext = sqlite.CreateDbContext();
        Assert.Equal(2, verifyContext.JobLogEntries.Count(e => e.JobExecutionId == executionId));
    }

    [Fact]
    public async Task OnePoisonedRecord_DoesNotCostTheWholeBatch()
    {
        // A batch mixes records from different executions. One unwritable row — here a log line whose
        // parent execution no longer exists, which happens when a job outlives its own retention —
        // fails the SaveChanges for all of them, so every other job running at that moment loses its
        // log lines too.
        using var factory = new SqliteDbContextFactory();
        var executionId = await SeedExecutionAsync(factory);

        var channel = Channel.CreateUnbounded<JobRecord>();
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            LogBatchSize = 10,
            LogFlushInterval = TimeSpan.FromMilliseconds(10)
        });

        channel.Writer.TryWrite(new LogRecordItem(executionId, 1, LogSeverity.Info, "before", LogEntrySource.DevLog, DateTimeOffset.UtcNow));
        // No such execution: violates the foreign key and fails the batch it travels in.
        channel.Writer.TryWrite(new LogRecordItem(9999, 1, LogSeverity.Info, "poison", LogEntrySource.DevLog, DateTimeOffset.UtcNow));
        channel.Writer.TryWrite(new LogRecordItem(executionId, 2, LogSeverity.Info, "after", LogEntrySource.DevLog, DateTimeOffset.UtcNow));

        var backgroundWriter = new JobLogBackgroundWriter(channel, factory, options, NullLogger<JobLogBackgroundWriter>.Instance);
        await backgroundWriter.StartAsync(CancellationToken.None);
        await backgroundWriter.StopAsync(CancellationToken.None);

        await using var verifyContext = factory.CreateDbContext();
        var written = verifyContext.JobLogEntries
            .Where(e => e.JobExecutionId == executionId)
            .Select(e => e.Message)
            .OrderBy(m => m)
            .ToList();

        Assert.Equal(["after", "before"], written);
    }

    /// <summary>Inserts a parent execution for log rows to hang off.</summary>
    private static async Task<long> SeedExecutionAsync(SqliteDbContextFactory factory)
    {
        await using var dbContext = factory.CreateDbContext();
        var execution = new JobExecution
        {
            ScheduledJobId = Guid.NewGuid(),
            JobName = "Job",
            JobTypeName = "Job",
            StartedAt = DateTimeOffset.UtcNow,
            Status = ExecutionStatus.Running,
            MachineName = "test"
        };
        dbContext.JobExecutions.Add(execution);
        await dbContext.SaveChangesAsync();
        return execution.Id;
    }

    [Fact]
    public async Task AFailingFlush_IsRetriedAndSurvived_RatherThanStoppingTheHost()
    {
        // The important one. Since .NET 6 an unhandled exception in a BackgroundService stops the
        // whole application by default, so a transient SQL error while writing log lines would take
        // the CMS down with it. The batch is allowed to be lost; the process is not.
        var factory = new FailingDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            LogBatchSize = 10,
            LogFlushInterval = TimeSpan.FromMilliseconds(20)
        });

        channel.Writer.TryWrite(new LogRecordItem(1, 1, LogSeverity.Info, "never lands", LogEntrySource.DevLog, DateTimeOffset.UtcNow));

        var backgroundWriter = new JobLogBackgroundWriter(channel, factory, options, NullLogger<JobLogBackgroundWriter>.Instance);
        await backgroundWriter.StartAsync(CancellationToken.None);

        // Give the retry sequence room to finish before shutting down.
        await Task.Delay(TimeSpan.FromSeconds(1));
        await backgroundWriter.StopAsync(CancellationToken.None);

        // Retried rather than dropped on the first error, and it gave up rather than looping forever.
        Assert.InRange(factory.Attempts, 2, 6);
        Assert.NotNull(backgroundWriter.ExecuteTask);
        Assert.True(backgroundWriter.ExecuteTask!.IsCompletedSuccessfully, "The service must not fault — a faulted BackgroundService stops the host.");
    }

    [Fact]
    public async Task WritingContinues_AfterAFailedBatch()
    {
        // A dropped batch must not poison the loop: whatever arrives next still gets written.
        using var workingFactory = new SqliteDbContextFactory();
        long executionId;
        await using (var dbContext = workingFactory.CreateDbContext())
        {
            var execution = new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = "Job",
                JobTypeName = "Job",
                StartedAt = DateTimeOffset.UtcNow,
                Status = ExecutionStatus.Running,
                MachineName = "test"
            };
            dbContext.JobExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            executionId = execution.Id;
        }

        var factory = new IntermittentDbContextFactory(workingFactory, failuresBeforeRecovery: 3);
        var channel = Channel.CreateUnbounded<JobRecord>();
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            LogBatchSize = 1,
            LogFlushInterval = TimeSpan.FromMilliseconds(20)
        });

        channel.Writer.TryWrite(new LogRecordItem(executionId, 1, LogSeverity.Error, "lost to the outage", LogEntrySource.DevLog, DateTimeOffset.UtcNow));

        var backgroundWriter = new JobLogBackgroundWriter(channel, factory, options, NullLogger<JobLogBackgroundWriter>.Instance);
        await backgroundWriter.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));

        channel.Writer.TryWrite(new LogRecordItem(executionId, 2, LogSeverity.Info, "written after recovery", LogEntrySource.DevLog, DateTimeOffset.UtcNow));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await backgroundWriter.StopAsync(CancellationToken.None);

        await using var verifyContext = workingFactory.CreateDbContext();
        Assert.Single(verifyContext.JobLogEntries.Where(e => e.Message == "written after recovery"));
    }

    [Fact]
    public async Task StopAsync_FlushesRecordsAlreadyTakenFromTheChannel()
    {
        // The same guarantee as above, but pinned to the ordering that actually loses data: here the
        // collector is given time to pull the records out of the channel and park waiting for more,
        // so at shutdown they exist only in the in-flight batch. The sibling test above happens to
        // stop before the collector runs at all, which is why it passed locally for months and only
        // failed once a slower CI runner scheduled things the other way round.
        using var factory = new SqliteDbContextFactory();
        long executionId;
        await using (var dbContext = factory.CreateDbContext())
        {
            var execution = new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = "Job",
                JobTypeName = "Job",
                StartedAt = DateTimeOffset.UtcNow,
                Status = ExecutionStatus.Running,
                MachineName = "test"
            };
            dbContext.JobExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            executionId = execution.Id;
        }

        var channel = Channel.CreateUnbounded<JobRecord>();
        // Batch size well above what we write, and a long interval, so the collector cannot decide to
        // flush on its own — it holds the records and waits.
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            LogBatchSize = 100,
            LogFlushInterval = TimeSpan.FromSeconds(30)
        });

        channel.Writer.TryWrite(new LogRecordItem(executionId, 1, LogSeverity.Info, "collected then abandoned", LogEntrySource.DevLog, DateTimeOffset.UtcNow));

        var backgroundWriter = new JobLogBackgroundWriter(channel, factory, options, NullLogger<JobLogBackgroundWriter>.Instance);
        await backgroundWriter.StartAsync(CancellationToken.None);

        // Let the collector drain the channel into its batch and settle into the wait.
        await WaitUntil(() => !channel.Reader.TryPeek(out _), TimeSpan.FromSeconds(5));

        await backgroundWriter.StopAsync(CancellationToken.None);

        await using var verifyContext = factory.CreateDbContext();
        Assert.Single(verifyContext.JobLogEntries.Where(e => e.Message == "collected then abandoned"));
    }

    /// <summary>Polls rather than sleeping a fixed amount, so the test is not tuned to one machine.</summary>
    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !condition())
            await Task.Delay(10);
    }

    [Fact]
    public async Task StopAsync_FlushesBufferedRecords_EvenIfExecuteAsyncNeverRan()
    {
        // BackgroundService starts ExecuteAsync on the thread pool, and a host that stops promptly
        // can cancel that task before its body is ever entered — it ends up Canceled having executed
        // nothing. A drain that lived only inside ExecuteAsync was skipped exactly then, silently
        // dropping everything buffered. Never calling StartAsync reproduces that state exactly.
        using var factory = new SqliteDbContextFactory();
        long executionId;
        await using (var dbContext = factory.CreateDbContext())
        {
            var execution = new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = "Job",
                JobTypeName = "Job",
                StartedAt = DateTimeOffset.UtcNow,
                Status = ExecutionStatus.Running,
                MachineName = "test"
            };
            dbContext.JobExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            executionId = execution.Id;
        }

        var channel = Channel.CreateUnbounded<JobRecord>();
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions());
        channel.Writer.TryWrite(new LogRecordItem(executionId, 1, LogSeverity.Info, "never collected", LogEntrySource.DevLog, DateTimeOffset.UtcNow));

        var backgroundWriter = new JobLogBackgroundWriter(channel, factory, options, NullLogger<JobLogBackgroundWriter>.Instance);

        await backgroundWriter.StopAsync(CancellationToken.None);

        await using var verifyContext = factory.CreateDbContext();
        Assert.Single(verifyContext.JobLogEntries.Where(e => e.Message == "never collected"));
    }
}
