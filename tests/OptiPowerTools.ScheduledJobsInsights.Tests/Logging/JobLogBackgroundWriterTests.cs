using System.Threading.Channels;
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

        var backgroundWriter = new JobLogBackgroundWriter(channel, factory, options);
        await backgroundWriter.StartAsync(CancellationToken.None);
        await backgroundWriter.StopAsync(CancellationToken.None);

        await using var verifyContext = factory.CreateDbContext();
        Assert.Single(verifyContext.JobLogEntries.Where(e => e.JobExecutionId == executionId && e.Message == "buffered line"));
        Assert.Single(verifyContext.JobMetrics.Where(e => e.JobExecutionId == executionId && e.Name == "BufferedMetric"));
    }
}
