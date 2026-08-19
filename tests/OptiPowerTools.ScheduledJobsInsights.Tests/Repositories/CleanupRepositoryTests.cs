using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;
using OptiPowerTools.ScheduledJobsInsights.Repositories;
using OptiPowerTools.ScheduledJobsInsights.Tests.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Repositories;

public class CleanupRepositoryTests
{
    [Fact]
    public void DeleteExecutionsOlderThan_DeletesOnlyExecutionsBeforeCutoff()
    {
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow;

        using (var dbContext = factory.CreateDbContext())
        {
            dbContext.JobExecutions.AddRange(
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Old1", JobTypeName = "Old", StartedAt = cutoff.AddDays(-2), Status = ExecutionStatus.Succeeded, MachineName = "m" },
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Old2", JobTypeName = "Old", StartedAt = cutoff.AddDays(-1), Status = ExecutionStatus.Succeeded, MachineName = "m" },
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Recent", JobTypeName = "Recent", StartedAt = cutoff.AddDays(1), Status = ExecutionStatus.Succeeded, MachineName = "m" });
            dbContext.SaveChanges();
        }

        var repository = new CleanupRepository(factory);
        var deleted = repository.DeleteExecutionsOlderThan(cutoff, batchSize: 100);

        Assert.Equal(2, deleted);
        using var verifyContext = factory.CreateDbContext();
        var remaining = Assert.Single(verifyContext.JobExecutions);
        Assert.Equal("Recent", remaining.JobName);
    }

    [Fact]
    public void DeleteExecutionsOlderThan_RespectsBatchSize()
    {
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow;

        using (var dbContext = factory.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
            {
                dbContext.JobExecutions.Add(new JobExecution
                {
                    ScheduledJobId = Guid.NewGuid(),
                    JobName = $"Old{i}",
                    JobTypeName = "Old",
                    StartedAt = cutoff.AddDays(-1),
                    Status = ExecutionStatus.Succeeded,
                    MachineName = "m"
                });
            }
            dbContext.SaveChanges();
        }

        var repository = new CleanupRepository(factory);
        var deleted = repository.DeleteExecutionsOlderThan(cutoff, batchSize: 2);

        Assert.Equal(2, deleted);
        using var verifyContext = factory.CreateDbContext();
        Assert.Equal(3, verifyContext.JobExecutions.Count());
    }

    [Fact]
    public void DeleteExecutionsOlderThan_CascadeDeletesLogEntriesAndMetrics()
    {
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow;
        long executionId;

        using (var dbContext = factory.CreateDbContext())
        {
            var execution = new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Old", JobTypeName = "Old", StartedAt = cutoff.AddDays(-1), Status = ExecutionStatus.Succeeded, MachineName = "m" };
            dbContext.JobExecutions.Add(execution);
            dbContext.SaveChanges();
            executionId = execution.Id;

            dbContext.JobLogEntries.Add(new JobLogEntry { JobExecutionId = executionId, Sequence = 1, Timestamp = DateTimeOffset.UtcNow, Message = "log" });
            dbContext.JobMetrics.Add(new JobMetric { JobExecutionId = executionId, Name = "Metric", Value = 1, RecordedAt = DateTimeOffset.UtcNow });
            dbContext.SaveChanges();
        }

        var repository = new CleanupRepository(factory);
        repository.DeleteExecutionsOlderThan(cutoff, batchSize: 100);

        using var verifyContext = factory.CreateDbContext();
        Assert.Empty(verifyContext.JobExecutions);
        Assert.Empty(verifyContext.JobLogEntries);
        Assert.Empty(verifyContext.JobMetrics);
    }

    [Fact]
    public void DeleteExecutionsOlderThan_NoExecutionsBeforeCutoff_ReturnsZero()
    {
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        using (var dbContext = factory.CreateDbContext())
        {
            dbContext.JobExecutions.Add(new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Recent", JobTypeName = "Recent", StartedAt = DateTimeOffset.UtcNow, Status = ExecutionStatus.Succeeded, MachineName = "m" });
            dbContext.SaveChanges();
        }

        var repository = new CleanupRepository(factory);
        var deleted = repository.DeleteExecutionsOlderThan(cutoff, batchSize: 100);

        Assert.Equal(0, deleted);
    }
}
