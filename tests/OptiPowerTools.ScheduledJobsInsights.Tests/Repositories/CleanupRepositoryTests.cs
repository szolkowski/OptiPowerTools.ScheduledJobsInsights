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
        var deleted = repository.DeleteExecutionsOlderThan(cutoff, batchSize: 100, excludedJobTypeNames: []);

        Assert.Equal(2, deleted);
        using var verifyContext = factory.CreateDbContext();
        var remaining = Assert.Single(verifyContext.JobExecutions);
        Assert.Equal("Recent", remaining.JobName);
    }

    [Fact]
    public void DeleteExecutionsOlderThan_LeavesARunningExecutionAlone_HoweverOld()
    {
        // A job can legitimately run for longer than its own retention: a 25-hour import under a
        // one-day rule. Deleting the row underneath it loses that run's history entirely — the
        // still-buffered log lines then violate the foreign key, poisoning the batch they travel in.
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow;

        using (var dbContext = factory.CreateDbContext())
        {
            dbContext.JobExecutions.AddRange(
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "StillRunning", JobTypeName = "Slow", StartedAt = cutoff.AddDays(-3), Status = ExecutionStatus.Running, MachineName = "m" },
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Finished", JobTypeName = "Slow", StartedAt = cutoff.AddDays(-3), Status = ExecutionStatus.Succeeded, MachineName = "m" });
            dbContext.SaveChanges();
        }

        var repository = new CleanupRepository(factory);
        var deleted = repository.DeleteExecutionsOlderThan(cutoff, batchSize: 100, excludedJobTypeNames: []);

        Assert.Equal(1, deleted);
        using var verifyContext = factory.CreateDbContext();
        var remaining = Assert.Single(verifyContext.JobExecutions);
        Assert.Equal("StillRunning", remaining.JobName);
    }

    [Fact]
    public void DeleteExecutionsOlderThan_ForOneJobType_LeavesARunningExecutionAlone()
    {
        // The per-job-type overload is the one a short explicit retention actually goes through, so
        // it is the more likely of the two to meet a run that outlives its own rule.
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow;

        using (var dbContext = factory.CreateDbContext())
        {
            dbContext.JobExecutions.AddRange(
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "StillRunning", JobTypeName = "Slow", StartedAt = cutoff.AddDays(-3), Status = ExecutionStatus.Running, MachineName = "m" },
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Finished", JobTypeName = "Slow", StartedAt = cutoff.AddDays(-3), Status = ExecutionStatus.Failed, MachineName = "m" });
            dbContext.SaveChanges();
        }

        var repository = new CleanupRepository(factory);
        var deleted = repository.DeleteExecutionsOlderThan("Slow", cutoff, batchSize: 100);

        Assert.Equal(1, deleted);
        using var verifyContext = factory.CreateDbContext();
        var remaining = Assert.Single(verifyContext.JobExecutions);
        Assert.Equal(ExecutionStatus.Running, remaining.Status);
    }

    [Fact]
    public void DeleteExecutionsOlderThan_StillDeletesInterruptedAndStoppedExecutions()
    {
        // Only Running is protected. An interrupted or stopped run is finished — it will never be
        // written to again — so retention must still be able to age it out, or a process that
        // recycles regularly accumulates history nothing can ever remove.
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow;

        using (var dbContext = factory.CreateDbContext())
        {
            dbContext.JobExecutions.AddRange(
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Interrupted", JobTypeName = "T", StartedAt = cutoff.AddDays(-1), Status = ExecutionStatus.Interrupted, MachineName = "m" },
                new JobExecution { ScheduledJobId = Guid.NewGuid(), JobName = "Stopped", JobTypeName = "T", StartedAt = cutoff.AddDays(-1), Status = ExecutionStatus.Stopped, MachineName = "m" });
            dbContext.SaveChanges();
        }

        var repository = new CleanupRepository(factory);
        var deleted = repository.DeleteExecutionsOlderThan(cutoff, batchSize: 100, excludedJobTypeNames: []);

        Assert.Equal(2, deleted);
        using var verifyContext = factory.CreateDbContext();
        Assert.Empty(verifyContext.JobExecutions);
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
        var deleted = repository.DeleteExecutionsOlderThan(cutoff, batchSize: 2, excludedJobTypeNames: []);

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
        repository.DeleteExecutionsOlderThan(cutoff, batchSize: 100, excludedJobTypeNames: []);

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
        var deleted = repository.DeleteExecutionsOlderThan(cutoff, batchSize: 100, excludedJobTypeNames: []);

        Assert.Equal(0, deleted);
    }

    [Fact]
    public void DeleteExecutionsOlderThan_SkipsExcludedJobTypes()
    {
        // The mechanism behind per-job retention: a job with its own rule must survive the default
        // sweep even when it is older than the default cutoff.
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        Seed(factory,
            ("Contoso.Jobs.Ordinary", cutoff.AddDays(-1)),
            ("Contoso.Jobs.Governed", cutoff.AddDays(-1)));

        var deleted = new CleanupRepository(factory).DeleteExecutionsOlderThan(cutoff, 100, ["Contoso.Jobs.Governed"]);

        Assert.Equal(1, deleted);
        using var dbContext = factory.CreateDbContext();
        Assert.Equal(["Contoso.Jobs.Governed"], dbContext.JobExecutions.Select(e => e.JobTypeName).ToList());
    }

    [Fact]
    public void DeleteExecutionsOlderThan_ForOneJobType_LeavesOtherJobsAlone()
    {
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        Seed(factory,
            ("Contoso.Jobs.Chatty", cutoff.AddDays(-1)),   // old enough, and the target
            ("Contoso.Jobs.Chatty", cutoff.AddDays(1)),    // too recent
            ("Contoso.Jobs.Other", cutoff.AddDays(-1)));   // old enough, but a different job

        var deleted = new CleanupRepository(factory).DeleteExecutionsOlderThan("Contoso.Jobs.Chatty", cutoff, 100);

        Assert.Equal(1, deleted);
        using var dbContext = factory.CreateDbContext();
        Assert.Equal(2, dbContext.JobExecutions.Count());
    }

    [Fact]
    public void MarkInterruptedExecutions_ResolvesOnlyRunsLeftHangingByADeadProcess()
    {
        // A process recycled mid-run records nothing further, so nothing else ever finishes its row.
        // Left alone they accumulate and every count, filter and "is it still running?" is wrong.
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        SeedWithStatus(factory,
            ("Contoso.Jobs.Abandoned", cutoff.AddHours(-1), ExecutionStatus.Running),   // old and unfinished
            ("Contoso.Jobs.StillGoing", cutoff.AddHours(1), ExecutionStatus.Running),   // unfinished but recent
            ("Contoso.Jobs.Finished", cutoff.AddHours(-1), ExecutionStatus.Succeeded)); // old but done

        var marked = new CleanupRepository(factory).MarkInterruptedExecutions(cutoff);

        Assert.Equal(1, marked);
        using var dbContext = factory.CreateDbContext();
        Assert.Equal(ExecutionStatus.Interrupted, dbContext.JobExecutions.Single(e => e.JobTypeName == "Contoso.Jobs.Abandoned").Status);
        Assert.Equal(ExecutionStatus.Running, dbContext.JobExecutions.Single(e => e.JobTypeName == "Contoso.Jobs.StillGoing").Status);
        Assert.Equal(ExecutionStatus.Succeeded, dbContext.JobExecutions.Single(e => e.JobTypeName == "Contoso.Jobs.Finished").Status);
    }

    [Fact]
    public void MarkInterruptedExecutions_LeavesCompletedAtNull()
    {
        // Nothing is known about when the run actually ended, and inventing a completion time would
        // make the duration column lie.
        using var factory = new SqliteDbContextFactory();
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        SeedWithStatus(factory, ("Contoso.Jobs.Abandoned", cutoff.AddHours(-1), ExecutionStatus.Running));

        new CleanupRepository(factory).MarkInterruptedExecutions(cutoff);

        using var dbContext = factory.CreateDbContext();
        Assert.Null(dbContext.JobExecutions.Single().CompletedAt);
    }

    private static void SeedWithStatus(
        SqliteDbContextFactory factory,
        params (string JobTypeName, DateTimeOffset StartedAt, ExecutionStatus Status)[] executions)
    {
        using var dbContext = factory.CreateDbContext();
        foreach (var (jobTypeName, startedAt, status) in executions)
        {
            dbContext.JobExecutions.Add(new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = jobTypeName,
                JobTypeName = jobTypeName,
                StartedAt = startedAt,
                CompletedAt = status == ExecutionStatus.Running ? null : startedAt.AddSeconds(1),
                Status = status,
                MachineName = "test"
            });
        }
        dbContext.SaveChanges();
    }

    private static void Seed(SqliteDbContextFactory factory, params (string JobTypeName, DateTimeOffset StartedAt)[] executions)
    {
        using var dbContext = factory.CreateDbContext();
        foreach (var (jobTypeName, startedAt) in executions)
        {
            dbContext.JobExecutions.Add(new JobExecution
            {
                ScheduledJobId = Guid.NewGuid(),
                JobName = jobTypeName,
                JobTypeName = jobTypeName,
                StartedAt = startedAt,
                Status = ExecutionStatus.Succeeded,
                MachineName = "test"
            });
        }
        dbContext.SaveChanges();
    }
}
