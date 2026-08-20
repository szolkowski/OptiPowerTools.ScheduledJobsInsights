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
        var options = Options.Create(new OptiPowerToolScheduledJobsInsightsOptions { LogBatchSize = 100, LogFlushInterval = TimeSpan.FromSeconds(30) });

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
    public async Task AFailingFlush_IsRetriedAndSurvived_RatherThanStoppingTheHost()
    {
        // The important one. Since .NET 6 an unhandled exception in a BackgroundService stops the
        // whole application by default, so a transient SQL error while writing log lines would take
        // the CMS down with it. The batch is allowed to be lost; the process is not.
        var factory = new FailingDbContextFactory();
        var channel = Channel.CreateUnbounded<JobRecord>();
        var options = Options.Create(new OptiPowerToolScheduledJobsInsightsOptions
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
        var options = Options.Create(new OptiPowerToolScheduledJobsInsightsOptions
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
}
