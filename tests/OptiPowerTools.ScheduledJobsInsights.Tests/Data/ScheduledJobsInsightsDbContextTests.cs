using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Data;

public class ScheduledJobsInsightsDbContextTests
{
    [Fact]
    public async Task SavingAnExecution_CascadeDeletesItsLogEntriesAndMetrics()
    {
        using var factory = new SqliteDbContextFactory();
        long executionId;

        await using (var dbContext = factory.CreateDbContext())
        {
            var execution = new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = "Test Job",
                JobTypeName = "Test.Job",
                StartedAt = DateTimeOffset.UtcNow,
                Status = ExecutionStatus.Succeeded,
                MachineName = "test-machine"
            };
            dbContext.JobExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            executionId = execution.Id;

            dbContext.JobLogEntries.Add(new JobLogEntry { JobExecutionId = executionId, Sequence = 1, Timestamp = DateTimeOffset.UtcNow, Message = "line 1" });
            dbContext.JobMetrics.Add(new JobMetric { JobExecutionId = executionId, Name = "DurationMs", Value = 12.3, RecordedAt = DateTimeOffset.UtcNow });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = factory.CreateDbContext())
        {
            Assert.Equal(1, dbContext.JobLogEntries.Count(e => e.JobExecutionId == executionId));
            Assert.Equal(1, dbContext.JobMetrics.Count(e => e.JobExecutionId == executionId));

            dbContext.JobExecutions.Remove(dbContext.JobExecutions.Single(e => e.Id == executionId));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = factory.CreateDbContext())
        {
            Assert.Empty(dbContext.JobLogEntries.Where(e => e.JobExecutionId == executionId));
            Assert.Empty(dbContext.JobMetrics.Where(e => e.JobExecutionId == executionId));
        }
    }
}
